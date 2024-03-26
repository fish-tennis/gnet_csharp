using System.Collections.Generic;
using Google.Protobuf;

namespace gnet_csharp
{
    public delegate void OnConnectedDelegate(IConnection connection, bool success);

    public delegate void OnCloseDelegate(IConnection connection);

    public interface IConnection
    {
        /// <summary>
        ///     user-defined object that bind the Connection
        /// </summary>
        object Tag { get; set; }

        ICodec Codec { get; set; }

        /// <summary>
        ///     callback of Connect operation
        /// </summary>
        OnConnectedDelegate OnConnected { get; set; }

        /// <summary>
        ///     callback of Close operation
        /// </summary>
        OnCloseDelegate OnClose { get; set; }

        int GetConnectionId();

        bool IsConnected();

        /// <summary>
        ///     connect to the host,it may be a asynchronous operation,
        ///     so use OnConnected to check if connected successful
        /// </summary>
        /// <param name="address">ip:port or url:port</param>
        bool Connect(string address);

        bool Send(ushort command, IMessage message);

        bool SendPacket(IPacket packet);

        void Close();
    }

    public class ConnectionConfig
    {
        public ICodec Codec;

        /// <summary>
        ///     seconds of heartbeat interval
        /// </summary>
        public int HeartBeatInterval;

        /// <summary>
        ///     the biggest packet size in your project
        /// </summary>
        public int MaxPacketSize;

        /// <summary>
        ///     the size of receive buffer,must not less than the biggest packet size in your project
        /// </summary>
        public int RecvBufferSize;

        /// <summary>
        ///     TcpClient.ReceiveTimeout (millisecond)
        /// </summary>
        public int RecvTimeout;

        /// <summary>
        ///     use for RingBuffer Codec
        /// </summary>
        public int SendBufferSize;

        /// <summary>
        ///     TcpClient.SendTimeout (millisecond)
        /// </summary>
        public int WriteTimeout;

        /// <summary>
        /// InsecureSkipVerify controls whether a client verifies the server's
        /// certificate chain and host name. If InsecureSkipVerify is true, crypto/tls
        /// accepts any certificate presented by the server and any host name in that
        /// certificate. In this mode, TLS is susceptible to machine-in-the-middle
        /// attacks unless custom verification is used
        /// </summary>
        public bool InsecureSkipVerify;

        /// <summary>
        /// cert file for wss
        /// only valid when InsecureSkipVerify is false 
        /// </summary>
        public string CertFile;
    }

    public class baseConnection
    {
        protected ConnectionConfig m_Config;
        protected int m_ConnectionId;
        protected bool m_IsConnected;
        protected string m_HostAddress;

        protected Queue<IPacket> m_Packets = new Queue<IPacket>();
        protected object m_PacketsLock = new object();
        public ICodec Codec { get; set; }
        public object Tag { get; set; }
        public OnConnectedDelegate OnConnected { get; set; }
        public OnCloseDelegate OnClose { get; set; }

        public int GetConnectionId()
        {
            return m_ConnectionId;
        }

        public bool IsConnected()
        {
            return m_IsConnected;
        }
        
        public string GetHostAddress()
        {
            return m_HostAddress;
        }
        
        public ConnectionConfig GetConfig()
        {
            return m_Config;
        }

        public void PushPacket(IPacket packet)
        {
            lock (m_PacketsLock)
            {
                m_Packets.Enqueue(packet);
            }
        }

        public IPacket PopPacket()
        {
            lock (m_PacketsLock)
            {
                if (m_Packets.Count == 0) return null;

                return m_Packets.Dequeue();
            }
        }

        protected void ClearPackets()
        {
            lock (m_PacketsLock)
            {
                m_Packets.Clear();
            }
        }
    }
}