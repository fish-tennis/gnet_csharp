using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Google.Protobuf;

namespace gnet_csharp
{
    /// <summary>
    ///     tcp connection,without RingBuffer
    /// </summary>
    public class TcpConnection : baseConnection, IConnection
    {
        private readonly byte[] m_ReadBuffer;
        private int m_IsClosed;
        private MemoryStream m_MemStream;
        private NetworkStream m_OutStream;
        private BinaryReader m_Reader;
        private int m_ReadLength;
        private IPacketHeader m_CurrentPacketHeader;
        private TcpClient m_TcpClient;

        public TcpConnection(ConnectionConfig connectionConfig, int connectionId)
        {
            m_ConnectionId = connectionId;
            m_Config = connectionConfig;
            Codec = m_Config.Codec;
            m_ReadBuffer = new byte[m_Config.RecvBufferSize];
        }

        // 异步连接
        public bool Connect(string address)
        {
            if (m_TcpClient != null)
            {
                Console.WriteLine("m_TcpClient not null:" + address);
                return false;
            }

            var ipPortStr = address.Split(':');
            if (ipPortStr.Length != 2)
            {
                Console.WriteLine("address err:" + address);
                return false;
            }

            var host = ipPortStr[0];
            var port = int.Parse(ipPortStr[1]);
            var ipAddresses = Dns.GetHostAddresses(host);
            if (ipAddresses.Length == 0)
            {
                Console.WriteLine("ipAddresses err:" + address);
                return false;
            }

            m_TcpClient = ipAddresses[0].AddressFamily == AddressFamily.InterNetworkV6
                ? new TcpClient(AddressFamily.InterNetworkV6)
                : new TcpClient(AddressFamily.InterNetwork);

            m_TcpClient.SendTimeout = m_Config.WriteTimeout;
            m_TcpClient.ReceiveTimeout = m_Config.RecvTimeout;
            m_TcpClient.NoDelay = true;
            m_HostAddress = address;
            m_IsConnected = false;
            Interlocked.Exchange(ref m_IsClosed, 0);
            Console.WriteLine("BeginConnect:" + address);
            try
            {
                m_TcpClient.BeginConnect(host, port, onAsyncConnected, this);
            }
            catch (Exception ex)
            {
                Console.WriteLine("BeginConnectErr:" + ex.Message);
                return false;
            }
            return true;
        }

        public void Close()
        {
            Console.WriteLine("Close");
            m_IsConnected = false;

            // 因为Close可能会被多个线程多次调用,所以这里用原子操作,防止OnClose被多次调用
            if (Interlocked.CompareExchange(ref m_IsClosed, 1, 0) == 0)
            {
                if (m_TcpClient != null)
                {
                    if (m_TcpClient.Connected) m_TcpClient.Close();

                    m_TcpClient = null;
                }

                if (m_Reader != null)
                {
                    m_Reader.Close();
                    m_MemStream.Close();
                    m_Reader = null;
                }

                OnClose?.Invoke(this);
            }

            ClearPackets();
        }

        /// <summary>
        ///     asynchronous send packet
        /// </summary>
        public bool Send(ushort command, IMessage message)
        {
            return SendPacket(new ProtoPacket(command, message));
        }

        /// <summary>
        ///     asynchronous send packet
        /// </summary>
        public bool SendPacket(IPacket packet)
        {
            if (!IsConnected())
            {
                Console.WriteLine("SendPacket !IsConnected");
                return false;
            }

            var bytes = Codec.Encode(this, packet);
            try
            {
                m_OutStream.BeginWrite(bytes, 0, bytes.Length, onAsyncWrite, this);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Close();
                return false;
            }

            return true;
        }

        private void onAsyncConnected(IAsyncResult asr)
        {
            try
            {
                m_MemStream = new MemoryStream();
                m_Reader = new BinaryReader(m_MemStream);

                m_OutStream = m_TcpClient.GetStream();
                Console.WriteLine("onConnected");
                m_IsConnected = true;
                OnConnected?.Invoke(this, true);
                m_ReadLength = 0;
                m_OutStream.BeginRead(m_ReadBuffer, m_ReadLength, m_ReadBuffer.Length - m_ReadLength, onAsyncRead,
                    this);
            }
            catch (Exception e)
            {
                Console.WriteLine("OnConnected failed:" + e);
                m_IsConnected = false;
                OnConnected?.Invoke(this, false);
                Close();
            }
        }

        private void onAsyncRead(IAsyncResult asr)
        {
            try
            {
                int bytesRead;
                lock (m_OutStream)
                {
                    bytesRead = m_OutStream.EndRead(asr);
                }

                if (bytesRead <= 0)
                {
                    Console.WriteLine("OnReadErr " + bytesRead);
                    Close();
                    return;
                }

                m_ReadLength += bytesRead;
                if (!decodePackets())
                {
                    Close();
                    return;
                }

                lock (m_OutStream)
                {
                    m_OutStream.BeginRead(m_ReadBuffer, m_ReadLength, m_ReadBuffer.Length - m_ReadLength, onAsyncRead,
                        this);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("OnReadErr ex:" + ex);
                if (m_TcpClient != null) Close();
            }
        }

        /// <summary>
        ///     decode packet from received buffer
        /// </summary>
        /// <returns></returns>
        private bool decodePackets()
        {
            var readIndex = 0;
            var remainLength = m_ReadLength;
            while (IsConnected())
            {
                if (m_CurrentPacketHeader == null)
                {
                    if (remainLength < Codec.PacketHeaderSize())
                        // received buffer size not enough for a full packet header
                        break;
                    var srcHeaderData = new ArraySegment<byte>(m_ReadBuffer, readIndex, Codec.PacketHeaderSize());
                    // 这里拷贝一份数据,在解析完整数据包之前,不修改原始数据
                    var copyHeaderData = new ArraySegment<byte>(srcHeaderData.ToArray());
                    m_CurrentPacketHeader = Codec.DecodePacketHeader(this, copyHeaderData);
                    if (m_CurrentPacketHeader == null)
                    {
                        // decode error
                        return false;
                    }
                }

                var fullPacketLength = Convert.ToInt32(m_CurrentPacketHeader.Len() + Codec.PacketHeaderSize());
                if (remainLength < fullPacketLength)
                    // received buffer size not enough for a full packet
                    break;

                var fullPacketData = new ArraySegment<byte>(m_ReadBuffer, readIndex, fullPacketLength);
                var newPacket = Codec.Decode(this, fullPacketData);
                if (newPacket == null)
                    // decode error
                    return false;
                PushPacket(newPacket);
                m_CurrentPacketHeader = null;
                remainLength -= fullPacketLength;
                readIndex += fullPacketLength;
            }

            if (readIndex > 0)
            {
                // remove the space of decoded packets
                Array.Copy(m_ReadBuffer, readIndex, m_ReadBuffer, 0, m_ReadLength - readIndex);
                m_ReadLength = remainLength;
            }

            return true;
        }

        private void onAsyncWrite(IAsyncResult asr)
        {
            try
            {
                m_OutStream.EndWrite(asr);
            }
            catch (Exception ex)
            {
                Console.WriteLine("onWriteErr" + ex);
                Close();
            }
        }
    }
}