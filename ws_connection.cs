using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace gnet_csharp
{
    public class WsConnection : baseConnection, IConnection
    {
        private readonly byte[] m_ReadBuffer;
        protected ClientWebSocket m_WebSocket;
        protected int m_IsClosed;

        public WsConnection(ConnectionConfig connectionConfig, int connectionId)
        {
            m_ConnectionId = connectionId;
            m_Config = connectionConfig;
            Codec = m_Config.Codec;
            m_ReadBuffer = new byte[m_Config.RecvBufferSize];
        }

        public bool Connect(string address)
        {
            if (m_WebSocket != null)
            {
                Console.WriteLine("m_WebSocket not null:" + address);
                return false;
            }

            var uri = new Uri(address);
            m_IsConnected = false;
            Interlocked.Exchange(ref m_IsClosed, 0);
            Console.WriteLine("BeginConnect:" + uri);
            m_HostAddress = address;
            m_WebSocket = new ClientWebSocket();
            try
            {
                m_WebSocket.ConnectAsync(uri, CancellationToken.None).Wait();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return false;
            }
            m_IsConnected = m_WebSocket.State == WebSocketState.Open;
            OnConnected?.Invoke(this, m_IsConnected);
            if (m_IsConnected)
            {
                StartReceive();
            }

            return true;
        }

        protected void StartReceive()
        {
            Task.Run(() =>
            {
                Console.WriteLine("StartReceive Begin");
                while (IsConnected())
                {
                    var readResult =
                        m_WebSocket.ReceiveAsync(new ArraySegment<byte>(m_ReadBuffer), CancellationToken.None);
                    readResult.Wait();
                    if (readResult.Result.EndOfMessage)
                    {
                        var fullPacketData = new Slice<byte>(m_ReadBuffer, 0, readResult.Result.Count);
                        var newPacket = Codec.Decode(this, fullPacketData);
                        if (newPacket == null)
                        {
                            Console.WriteLine("StartReceive decode error");
                            // decode error
                            Close();
                            break;
                        }

                        PushPacket(newPacket);
                    }

                    if (readResult.Result.CloseStatus != null)
                    {
                        Console.WriteLine("StartReceive CloseStatus:" + readResult.Result.CloseStatusDescription);
                        Close();
                        break;
                    }
                }

                Console.WriteLine("StartReceive End");
            });
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

            var bytes = Codec.Encode(this, packet);
            try
            {
                m_WebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true,
                    CancellationToken.None).Wait();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Close();
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
                Console.WriteLine("Closed");
                if (m_WebSocket != null)
                {
                    m_WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "NormalClosure", CancellationToken.None).Wait();
                    m_WebSocket.Abort();

                    m_WebSocket = null;
                }

                OnClose?.Invoke(this);
            }

            ClearPackets();
        }
    }
}