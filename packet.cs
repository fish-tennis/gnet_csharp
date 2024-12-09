using System;
using System.IO;
using Google.Protobuf;

namespace gnet_csharp
{
    public interface IPacketHeader
    {
        /// <summary>
        ///     the length of packet body,exclude the length of PacketHeader
        ///     包体长度,不包含PacketHeader的长度
        /// </summary>
        uint Len();

        bool HasFlag(byte flag);

        /// <summary>
        ///     deserialize from stream data
        /// </summary>
        void ReadFrom(ArraySegment<byte> packetHeaderData);

        /// <summary>
        ///     serialize
        /// </summary>
        void WriteTo(ArraySegment<byte> packetHeaderData);
    }

    public interface IPacket
    {
        ushort Command();

        uint ErrorCode();

        IMessage Message();

        /// <summary>
        ///     in same cases,you may want to use streaming data instead of IMessage
        ///     某些特殊的业务场景,不适合使用protobuf,可以直接使用二进制数据
        /// </summary>
        byte[] GetStreamData();
    }

    public class DefaultPacketHeader : IPacketHeader
    {
        //public const byte RpcCall = 1 << 0; // 客户端暂不支持rpc
        public const byte Compress = 1 << 1; // TODO: 预留标记,暂未实现
        public const byte HasErrorCode = 1 << 2;
        /// <summary>
        ///     the fixed length of DefaultPacketHeader
        /// </summary>
        public const int DefaultPacketHeaderSize = 4;

        /// <summary>
        ///     the max packet body size supported by DefaultPacketHeader
        /// </summary>
        public const int MaxPacketDataSize = 0x00FFFFFF;

        /// <summary>
        ///     24 bit for packet body length, and 8 bit for flags
        /// </summary>
        private uint m_LenAndFlags;

        private uint m_ErrorCode;

        public DefaultPacketHeader()
        {
            m_LenAndFlags = 0;
        }

        public DefaultPacketHeader(uint len, byte flags)
        {
            m_LenAndFlags = (Convert.ToUInt32(flags) << 24) | len;
        }

        /// <summary>
        ///     length of packet body
        /// </summary>
        public uint Len()
        {
            return m_LenAndFlags & 0x00FFFFFF;
        }
        
        public byte Flags()
        {
            return Convert.ToByte(m_LenAndFlags >> 24);
        }

        public bool HasFlag(byte flag)
        {
            return (this.Flags() & flag) == flag;
        }

        public void ReadFrom(ArraySegment<byte> packetHeaderData)
        {
            m_LenAndFlags = BitConverter.ToUInt32(packetHeaderData.Array, packetHeaderData.Offset);
        }

        public void WriteTo(ArraySegment<byte> packetHeaderData)
        {
            var stream = new MemoryStream(packetHeaderData.Array);
            var writer = new BinaryWriter(stream);
            writer.Seek(packetHeaderData.Offset, SeekOrigin.Begin);
            writer.Write(m_LenAndFlags);
        }
    }

    /// <summary>
    ///     the default implementation of IPacket
    /// </summary>
    public class ProtoPacket : IPacket
    {
        private ushort m_Command;
        private uint m_ErrorCode;
        private IMessage m_Message;
        private byte[] m_StreamData;

        public ProtoPacket(ushort command)
        {
            m_Command = command;
        }

        public ProtoPacket(ushort command, IMessage message)
        {
            m_Command = command;
            m_Message = message;
        }

        public ProtoPacket(ushort command, IMessage message, uint errorCode)
        {
            m_Command = command;
            m_ErrorCode = errorCode;
            m_Message = message;
        }

        public ProtoPacket(ushort command, byte[] streamData, uint errorCode)
        {
            m_Command = command;
            m_ErrorCode = errorCode;
            m_StreamData = streamData;
        }

        public ushort Command()
        {
            return m_Command;
        }

        public uint ErrorCode()
        {
            return this.m_ErrorCode;
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