using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Google.Protobuf;

namespace gnet_csharp
{
    public class TcpConnectionSimple : baseConnection, IConnection
    {
        private TcpClient m_TcpClient;
        private NetworkStream m_OutStream;
        private MemoryStream m_MemStream;
        private BinaryReader m_Reader;
        private readonly byte[] m_ReadBuffer;
        private int m_ReadLength;
        private string m_HostAddress;

        public TcpConnectionSimple(ConnectionConfig connectionConfig, int connectionId)
        {
            m_ConnectionId = connectionId;
            m_Config = connectionConfig;
            m_Codec = m_Config.Codec;
            m_ReadBuffer = new byte[m_Config.RecvBufferSize];
        }

        public bool Connect(string address)
        {
            string[] ipPortStr = address.Split(':');
            if (ipPortStr.Length != 2)
            {
                Console.WriteLine("address err:"+address);
                return false;
            }

            string host = ipPortStr[0];
            var port = int.Parse(ipPortStr[1]);
            IPAddress[] ipAddresses = Dns.GetHostAddresses(host);
            if (ipAddresses.Length == 0)
            {
                Console.WriteLine("ipAddresses err:"+address);
                return false;
            }

            if (ipAddresses[0].AddressFamily == AddressFamily.InterNetworkV6)
            {
                m_TcpClient = new TcpClient(AddressFamily.InterNetworkV6);
            }
            else
            {
                m_TcpClient = new TcpClient(AddressFamily.InterNetwork);
            }

            m_TcpClient.SendTimeout = m_Config.WriteTimeout;
            m_TcpClient.ReceiveTimeout = m_Config.RecvTimeout;
            m_TcpClient.NoDelay = true;
            m_HostAddress = address;
            Console.WriteLine("BeginConnect:"+address);
            m_TcpClient.BeginConnect(host, port, OnAsyncConnect, this);
            return true;
        }

        private static void OnAsyncConnect(IAsyncResult asr)
        {
            TcpConnectionSimple connection = (TcpConnectionSimple) asr.AsyncState;
            connection.OnConnected();
        }

        private void OnConnected()
        {
            try
            {
                m_MemStream = new MemoryStream();
                m_Reader = new BinaryReader(m_MemStream);

                m_OutStream = m_TcpClient.GetStream();
                Console.WriteLine("OnConnected");
                m_IsConnected = true;
                m_OutStream.BeginRead(m_ReadBuffer, m_ReadLength, m_ReadBuffer.Length - m_ReadLength, OnAsyncRead, this);
            }
            catch (Exception e)
            {
                Console.WriteLine("OnConnected failed:" + e.Message);
                Close();
            }
        }

        private static void OnAsyncRead(IAsyncResult asr)
        {
            TcpConnectionSimple connection = (TcpConnectionSimple) asr.AsyncState;
            connection.OnRead(asr);
        }

        private void OnRead(IAsyncResult asr)
        {
            try
            {
                int bytesRead;
                lock (m_OutStream)
                {
                    //读取字节流到缓冲区
                    bytesRead = m_OutStream.EndRead(asr);
                }

                if (bytesRead <= 0)
                {
                    Console.WriteLine("OnReadErr " + bytesRead);
                    //包尺寸有问题，断线处理,服务器运行异常
                    Close();
                    return;
                }

                m_ReadLength += bytesRead;
                Console.WriteLine("OnRead " + bytesRead);
                if (!decodePackets())
                {
                    Close();
                    return;
                }

                //OnReceive(readBuffer, bytesRead);
                lock (m_OutStream)
                {
                    m_OutStream.BeginRead(m_ReadBuffer, m_ReadLength, m_ReadBuffer.Length - m_ReadLength, OnAsyncRead, this);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("OnReadErr ex:" + ex);
                if (m_TcpClient != null)
                {
                    Close();
                }
            }
        }

        private bool decodePackets()
        {
            while (IsConnected())
            {
                if (m_ReadLength < GetCodec().PacketHeaderSize())
                {
                    return true;
                }

                var newPacketHeader = GetCodec().CreatePacketHeader(this, null, null);
                newPacketHeader.ReadFrom(m_ReadBuffer);
                var fullPacketLength = Convert.ToInt32(newPacketHeader.Len() + GetCodec().PacketHeaderSize());
                if (m_ReadLength < fullPacketLength)
                {
                    return true;
                }

                var newPacket = GetCodec().Decode(this, m_ReadBuffer);
                if (newPacket == null)
                {
                    return false;
                }

                PushPacket(newPacket);
                Console.WriteLine("decodePackets " + newPacket + " fullPacketLength:" + fullPacketLength);
                Array.Copy(m_ReadBuffer, fullPacketLength, m_ReadBuffer, 0, m_ReadLength - fullPacketLength);
                m_ReadLength -= fullPacketLength;
            }

            return true;
        }

        public void Close()
        {
            Console.WriteLine("Close");
            m_IsConnected = false;
            if (m_TcpClient != null)
            {
                if (m_TcpClient.Connected)
                {
                    m_TcpClient.Close();
                }

                m_TcpClient = null;
            }

            if (m_Reader != null)
            {
                m_Reader.Close();
                m_MemStream.Close();
                m_Reader = null;
            }

            clearPackets();
        }

        public bool Send(ushort command, IMessage message)
        {
            return SendPacket(new ProtoPacket(command, message));
        }

        public bool SendPacket(IPacket packet)
        {
            if (!IsConnected())
            {
                Console.WriteLine("SendPacket !IsConnected");
                return false;
            }

            var bytes = GetCodec().Encode(this, packet);
            m_OutStream.BeginWrite(bytes, 0, bytes.Length, onAsyncWrite, this);
            return true;
        }

        private static void onAsyncWrite(IAsyncResult asr)
        {
            TcpConnectionSimple connection = (TcpConnectionSimple) asr.AsyncState;
            connection.onWrite(asr);
        }

        private void onWrite(IAsyncResult asr)
        {
            try
            {
                m_OutStream.EndWrite(asr);
                Console.WriteLine("onAsyncWrite");
            }
            catch (Exception ex)
            {
                Console.WriteLine("onAsyncWriteErr" + ex.Message);
                Close();
            }
        }
        
    }
}