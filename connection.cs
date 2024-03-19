using System.Collections.Generic;
using Google.Protobuf;

namespace gnet_csharp
{
    public interface IConnection
    {
        int GetConnectionId();

        bool IsConnected();

        object GetTag();

        void SetTag(object tag);

        bool Connect(string address);

        bool Send(ushort command, IMessage message);

        bool SendPacket(IPacket packet);

        ICodec GetCodec();

        void SetCodec(ICodec codec);

        void Close();
    }

    public class ConnectionConfig
    {
        public int SendBufferSize;
        public int RecvBufferSize;
        public int MaxPacketSize;
        
        /// TcpClient.ReceiveTimeout (millisecond)
        public int RecvTimeout;
        public int HeartBeatInterval;
        
        /// TcpClient.SendTimeout (millisecond)
        public int WriteTimeout;
        public ICodec Codec;
    }

    public class baseConnection
    {
        protected int m_ConnectionId;
        protected ConnectionConfig m_Config;
        protected bool m_IsConnected;
        protected ICodec m_Codec;
        protected object m_Tag;
        protected Queue<IPacket> m_Packets = new Queue<IPacket>();
        protected object m_PacketsLock = new object();

        public int GetConnectionId()
        {
            return m_ConnectionId;
        }

        public bool IsConnected()
        {
            return m_IsConnected;
        }

        public ICodec GetCodec()
        {
            return m_Codec;
        }

        public void SetCodec(ICodec c)
        {
            m_Codec = c;
        }

        public object GetTag()
        {
            return m_Tag;
        }

        public void SetTag(object obj)
        {
            m_Tag = obj;
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
                if (m_Packets.Count == 0)
                {
                    return null;
                }
                return m_Packets.Dequeue();
            }
        }

        protected void clearPackets()
        {
            lock (m_PacketsLock)
            {
                m_Packets.Clear();
            }
        }
    }
}