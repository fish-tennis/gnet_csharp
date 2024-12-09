using System;
using System.Collections;
using System.IO;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace gnet_csharp
{
    /// <summary>
    /// SimplePacketHeader contains packet len and packet command
    /// use for WsConnection,which not use RingBuffer
    /// </summary>
    public class SimplePacketHeader
    {
        public const int SimplePacketHeaderSize = 6;
        private uint m_LenAndFlags;
        private ushort m_Command;

        public SimplePacketHeader()
        {
        }

        public SimplePacketHeader(uint len, byte flags, ushort command)
        {
            m_LenAndFlags = ((Convert.ToUInt32(flags) << 24) | len);
            m_Command = command;
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

        public ushort Command => m_Command;
        
        public void ReadFrom(ArraySegment<byte> packetHeaderData)
        {
            m_LenAndFlags = BitConverter.ToUInt32(packetHeaderData.Array, packetHeaderData.Offset);
            m_Command = BitConverter.ToUInt16(packetHeaderData.Array, packetHeaderData.Offset + 4);
        }

        public void WriteTo(ArraySegment<byte> packetHeaderData)
        {
            var stream = new MemoryStream(packetHeaderData.Array);
            var writer = new BinaryWriter(stream);
            writer.Seek(packetHeaderData.Offset, SeekOrigin.Begin);
            writer.Write(m_LenAndFlags);
            writer.Write(m_Command);
        }
    }
    
    /// <summary>
    /// codec for WsConnection,which not use RingBuffer
    /// use SimplePacketHeader as it's PacketHeader
    /// </summary>
    public class SimpleProtoCodec : ICodec
    {
        /// <summary>
        ///     map of Command and Protobuf MessageDescriptor
        /// </summary>
        private readonly Hashtable m_MessageDescriptors = new Hashtable();

        public PacketDataEncoder DataEncoder { get; set; }
        public PacketDataDecoder DataDecoder { get; set; }

        public int PacketHeaderSize()
        {
            return SimplePacketHeader.SimplePacketHeaderSize;
        }

        public IPacketHeader DecodePacketHeader(IConnection connection, ArraySegment<byte> headerData)
        {
            if (headerData.Count < PacketHeaderSize())
            {
                return null;
            }

            var decodeHeaderData = headerData;
            if (DataDecoder != null)
            {
                decodeHeaderData = DataDecoder.Invoke(connection, headerData);
            }

            var packetHeader = new DefaultPacketHeader();
            packetHeader.ReadFrom(decodeHeaderData);
            return packetHeader;
        }

        public byte[] Encode(IConnection connection, IPacket packet)
        {
            var command = packet.Command();
            var protoMessage = packet.Message();
            var bodyData = protoMessage != null ? protoMessage.ToByteArray() : packet.GetStreamData();
            var bodyDataLen = bodyData?.Length ?? 0;
            var fullPacketData = new byte[PacketHeaderSize() + bodyDataLen];
            var packetHeader = new SimplePacketHeader(Convert.ToUInt32(bodyDataLen), 0, command);
            packetHeader.WriteTo(new ArraySegment<byte>(fullPacketData));
            var stream = new MemoryStream(fullPacketData);
            var writer = new BinaryWriter(stream);
            writer.Seek(PacketHeaderSize(), SeekOrigin.Begin);
            if (bodyData != null) writer.Write(bodyData);
            writer.Flush();
            var packetBytes = stream.ToArray();
            // DataEncoder可以继续对packetBytes进行编码,如异或,加密,压缩等
            // DataEncoder can continue to encode packetBytes here, such as XOR, encryption, compression, etc
            return DataEncoder == null
                ? packetBytes
                : DataEncoder.Invoke(connection, packet, new ArraySegment<byte>(packetBytes));
        }

        public IPacket Decode(IConnection connection, ArraySegment<byte> data)
        {
            if (data.Count < PacketHeaderSize()) return null;
            // Q:DataDecoder可以对data进行解码,如异或,解密,解压等
            // DataDecoder can decode data here, such as XOR, decryption, decompression, etc
            if (DataDecoder != null)
            {
                data = DataDecoder.Invoke(connection, data);
            }

            var packetHeader = new SimplePacketHeader();
            packetHeader.ReadFrom(data);
            if (data.Count < PacketHeaderSize() + packetHeader.Len()) return null;
            var command = packetHeader.Command;
            var messageLen = Convert.ToInt32(packetHeader.Len());
            uint errorCode = 0;
            int offset = PacketHeaderSize();
            if (packetHeader.HasFlag(DefaultPacketHeader.HasErrorCode))
            {
                errorCode = BitConverter.ToUInt32(data.Array, data.Offset + offset);
                offset += 4;
                messageLen -= 4;
            }
            if (messageLen <= 0) Console.WriteLine("command:" + command + " messageLen:" + messageLen);
            var messageBuffer = new ArraySegment<byte>(data.Array, data.Offset + offset, messageLen);
            var messageDescriptor = getMessageDescriptor(command);
            if (messageDescriptor == null) return new ProtoPacket(command, messageBuffer.ToArray(), errorCode);
            try
            {
                var messageBytes = messageBuffer.ToArray();
                var protoMessage = messageDescriptor.Parser.ParseFrom(messageBytes);
                return new ProtoPacket(command, protoMessage);
            }
            catch (Exception e)
            {
                Console.WriteLine("command:" + command + " parseErr:" + e);
                return null;
            }
        }

        public void Register(ushort command, MessageDescriptor messageDescriptor)
        {
            m_MessageDescriptors[command] = messageDescriptor;
        }

        private MessageDescriptor getMessageDescriptor(ushort command)
        {
            if (m_MessageDescriptors.Contains(command))
                return m_MessageDescriptors[command] as MessageDescriptor;
            return null;
        }
    }
}