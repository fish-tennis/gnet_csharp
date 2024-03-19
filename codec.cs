using System;
using System.Collections;
using System.IO;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace gnet_csharp
{
    public interface ICodec
    {
        int PacketHeaderSize();

        IPacketHeader CreatePacketHeader(IConnection connection, IPacket packet, byte[] packetData);

        byte[] Encode(IConnection connection, IPacket packet);

        IPacket Decode(IConnection connection, byte[] data);
    }

    public class SimpleProtoCodec : ICodec
    {
        private Hashtable m_MessageDescriptors = new Hashtable();

        public int PacketHeaderSize()
        {
            return DefaultPacketHeader.DefaultPacketHeaderSize;
        }

        public IPacketHeader CreatePacketHeader(IConnection connection, IPacket packet, byte[] packetData)
        {
            return packetData == null
                ? new DefaultPacketHeader()
                : new DefaultPacketHeader(Convert.ToUInt32(packetData.Length), 0);
        }

        public static void TryWriteBytes(Span<byte> bytes, ushort v)
        {
            var stream = new MemoryStream(bytes.ToArray());
            var writer = new BinaryWriter(stream);
            writer.Write(v);
        }

        public byte[] Encode(IConnection connection, IPacket packet)
        {
            var command = packet.Command();
            var protoMessage = packet.Message();
            if (protoMessage != null)
            {
                var protoMessageLen = protoMessage.CalculateSize();
                var fullPacketData = new byte[PacketHeaderSize() + 2 + protoMessageLen];
                var packetHeader = new DefaultPacketHeader(Convert.ToUInt32(2 + protoMessageLen), 0);
                packetHeader.WriteTo(fullPacketData);
                var stream = new MemoryStream(fullPacketData);
                var writer = new BinaryWriter(stream);
                writer.Seek(PacketHeaderSize(), SeekOrigin.Begin);
                writer.Write(command);
                var protoMessageBytes = protoMessage.ToByteArray();
                if (protoMessageBytes.Length != protoMessageLen)
                {
                    Console.WriteLine("ProtoLenErr protoMessageLen:"+protoMessageLen+" bytesCount:" + protoMessageBytes.Length);
                }
                writer.Write(protoMessageBytes);
                writer.Flush();
                return stream.ToArray();
            }
            else
            {
                var streamData = packet.GetStreamData();
                var streamDataLen = streamData.Length;
                var fullPacketData = new byte[PacketHeaderSize() + 2 + streamDataLen];
                var packetHeader = new DefaultPacketHeader(Convert.ToUInt32(2 + streamDataLen), 0);
                packetHeader.WriteTo(fullPacketData);
                var stream = new MemoryStream(fullPacketData);
                var writer = new BinaryWriter(stream);
                writer.Seek(PacketHeaderSize(), SeekOrigin.Begin);
                writer.Write(command);
                writer.Write(streamData);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public IPacket Decode(IConnection connection, byte[] data)
        {
            if (data.Length < PacketHeaderSize() + 2)
            {
                return null;
            }

            var fullBytes = data.AsSpan();
            var headerBuffer = fullBytes.Slice(0, PacketHeaderSize());
            var packetHeader = new DefaultPacketHeader();
            packetHeader.ReadFrom(headerBuffer);
            var commandBuffer = fullBytes.Slice(PacketHeaderSize(), 2);
            var command = BitConverter.ToUInt16(commandBuffer.ToArray(), 0);
            Console.WriteLine("command:"+command);
            var messageLen = Convert.ToInt32(packetHeader.Len()) - 2;
            if (messageLen <= 0)
            {
                Console.WriteLine("messageLen:"+messageLen);
            }
            var messageBuffer = fullBytes.Slice(PacketHeaderSize() + 2, messageLen);
            if (!m_MessageDescriptors.Contains(command)) return new ProtoPacket(command, messageBuffer.ToArray());
            var messageDescriptor = m_MessageDescriptors[command] as MessageDescriptor;
            var protoMessage = messageDescriptor.Parser.ParseFrom(messageBuffer);
            return new ProtoPacket(command, protoMessage);
        }

        public void Register(ushort command, MessageDescriptor messageDescriptor)
        {
            m_MessageDescriptors[command] = messageDescriptor;
        }
    }
}