using System;
using System.IO;
using Google.Protobuf;

namespace gnet_csharp
{
    public interface IPacketHeader
    {
        uint Len();

        void ReadFrom(Span<byte> packetHeaderData);

        void WriteTo(byte[] packetHeaderData);
    }

    public interface IPacket
    {
        ushort Command();

        IMessage Message();

        byte[] GetStreamData();
    }

    public class DefaultPacketHeader : IPacketHeader
    {
        public static readonly int DefaultPacketHeaderSize = 4;
        public static int MaxPacketDataSize = 0x00FFFFFF;
        
        private uint m_LenAndFlags;

        public DefaultPacketHeader()
        {
            m_LenAndFlags = 0;
        }

        public DefaultPacketHeader(uint len, byte flags)
        {
            m_LenAndFlags = (Convert.ToUInt32(flags) << 24) | len;
        }

        public uint Len()
        {
            return m_LenAndFlags & 0x00FFFFFF;
        }

        public byte Flags()
        {
            return Convert.ToByte(this.m_LenAndFlags >> 24);
        }

        public void ReadFrom(Span<byte> packetHeaderData)
        {
            m_LenAndFlags = BitConverter.ToUInt32(packetHeaderData.ToArray(), 0);
        }

        public void WriteTo(byte[] packetHeaderData)
        {
            var stream = new MemoryStream(packetHeaderData);
            var writer = new BinaryWriter(stream);
            writer.Write(m_LenAndFlags);
            // BitConverter.TryWriteBytes(packetHeaderData, m_LenAndFlags);
        }
    }

    public class ProtoPacket : IPacket
    {
        private ushort m_Command;
        private IMessage m_Message;
        private byte[] m_StreamData;

        public ProtoPacket(ushort mCommand)
        {
            m_Command = mCommand;
        }

        public ProtoPacket(ushort mCommand, IMessage mMessage)
        {
            this.m_Command = mCommand;
            this.m_Message = mMessage;
        }

        public ProtoPacket(ushort mCommand, byte[] mStreamData)
        {
            m_Command = mCommand;
            m_StreamData = mStreamData;
        }

        public ushort Command()
        {
            return m_Command;
        }

        public IMessage Message()
        {
            return m_Message;
        }

        public byte[] GetStreamData()
        {
            return m_StreamData;
        }
    }
}