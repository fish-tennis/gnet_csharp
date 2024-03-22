using System;
using System.Collections;
using System.IO;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace gnet_csharp
{
    public interface ICodec
    {
        int PacketHeaderSize();

        IPacketHeader DecodePacketHeader(IConnection connection, Slice<byte> headerData);

        /// <summary>
        ///     encode a packet to stream data
        /// </summary>
        byte[] Encode(IConnection connection, IPacket packet);

        /// <summary>
        ///     decode a packet from stream data
        /// </summary>
        IPacket Decode(IConnection connection, Slice<byte> data);
    }

    public delegate byte[] PacketDataEncoder(IConnection connection, IPacket packet, Slice<byte> data);

    public delegate Slice<byte> PacketDataDecoder(IConnection connection, Slice<byte> data);

    public class ProtoCodec : ICodec
    {
        /// <summary>
        ///     map of Command and Protobuf MessageDescriptor
        /// </summary>
        private readonly Hashtable m_MessageDescriptors = new Hashtable();

        public PacketDataEncoder DataEncoder { get; set; }
        public PacketDataDecoder DataDecoder { get; set; }

        public int PacketHeaderSize()
        {
            return DefaultPacketHeader.DefaultPacketHeaderSize;
        }

        public IPacketHeader DecodePacketHeader(IConnection connection, Slice<byte> headerData)
        {
            if (headerData.Length < PacketHeaderSize())
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
            var fullPacketData = new byte[PacketHeaderSize() + 2 + bodyDataLen];
            var packetHeader = new DefaultPacketHeader(Convert.ToUInt32(2 + bodyDataLen), 0);
            packetHeader.WriteTo(new Slice<byte>(fullPacketData));
            var stream = new MemoryStream(fullPacketData);
            var writer = new BinaryWriter(stream);
            writer.Seek(PacketHeaderSize(), SeekOrigin.Begin);
            writer.Write(command);
            if (bodyData != null) writer.Write(bodyData);
            writer.Flush();
            var packetBytes = stream.ToArray();
            // DataEncoder可以继续对packetBytes进行编码,如异或,加密,压缩等
            // DataEncoder can continue to encode packetBytes here, such as XOR, encryption, compression, etc
            return DataEncoder == null
                ? packetBytes
                : DataEncoder.Invoke(connection, packet, new Slice<byte>(packetBytes));
        }

        public IPacket Decode(IConnection connection, Slice<byte> data)
        {
            if (data.Length < PacketHeaderSize() + 2) return null;
            // Q:DataDecoder可以对data进行解码,如异或,解密,解压等
            // DataDecoder can decode data here, such as XOR, decryption, decompression, etc
            if (DataDecoder != null)
            {
                data = DataDecoder.Invoke(connection, data);
            }

            var packetHeader = new DefaultPacketHeader();
            packetHeader.ReadFrom(data);
            var command = BitConverter.ToUInt16(data.OriginalArray, data.StartIndex + PacketHeaderSize());
            var messageLen = Convert.ToInt32(packetHeader.Len()) - 2;
            if (messageLen <= 0) Console.WriteLine("command:" + command + " messageLen:" + messageLen);
            if (messageLen > data.Length)
            {
                Console.WriteLine("command:" + command + " messageLenErr:" + messageLen);
                return null;
            }

            var messageBuffer = new Slice<byte>(data, PacketHeaderSize() + 2, messageLen);
            // var messageBuffer = data.Skip(PacketHeaderSize() + 2).Take(messageLen).ToArray();
            var messageDescriptor = getMessageDescriptor(command);
            if (messageDescriptor == null) return new ProtoPacket(command, messageBuffer.ToArray());
            var protoMessage = messageDescriptor.Parser.ParseFrom(messageBuffer.ToArray());
            return new ProtoPacket(command, protoMessage);
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

    public class XorProtoCodec : ProtoCodec
    {
        private byte[] m_XorKey;

        public XorProtoCodec(byte[] xorKey)
        {
            m_XorKey = xorKey;
            DataEncoder = xorDataEncoder;
            DataDecoder = xorDataDecoder;
        }

        private byte[] xorEncode(byte[] data, int startIndex, int length)
        {
            for (var i = 0; i < length; i++)
            {
                var index = startIndex + i;
                data[index] = (byte) (data[index] ^ m_XorKey[i % m_XorKey.Length]);
            }

            return data;
        }

        private byte[] xorDataEncoder(IConnection connection, IPacket packet, Slice<byte> data)
        {
            return xorEncode(data.OriginalArray, data.StartIndex, data.Length);
        }

        private Slice<byte> xorDataDecoder(IConnection connection, Slice<byte> data)
        {
            xorEncode(data.OriginalArray, data.StartIndex, data.Length);
            return data;
        }
    }
}