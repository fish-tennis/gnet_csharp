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

        IPacketHeader CreatePacketHeader(IConnection connection, IPacket packet, byte[] packetData);

        /// <summary>
        ///     encode a packet to stream data
        /// </summary>
        byte[] Encode(IConnection connection, IPacket packet);

        /// <summary>
        ///     decode a packet from stream data
        /// </summary>
        IPacket Decode(IConnection connection, byte[] data);
    }

    public class ProtoCodec : ICodec
    {
        /// <summary>
        ///     map of Command and Protobuf MessageDescriptor
        /// </summary>
        private readonly Hashtable m_MessageDescriptors = new Hashtable();

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

        public byte[] Encode(IConnection connection, IPacket packet)
        {
            var command = packet.Command();
            var protoMessage = packet.Message();
            var bodyData = protoMessage != null ? protoMessage.ToByteArray() : packet.GetStreamData();
            var bodyDataLen = bodyData?.Length ?? 0;
            var fullPacketData = new byte[PacketHeaderSize() + 2 + bodyDataLen];
            var packetHeader = new DefaultPacketHeader(Convert.ToUInt32(2 + bodyDataLen), 0);
            packetHeader.WriteTo(fullPacketData);
            var stream = new MemoryStream(fullPacketData);
            var writer = new BinaryWriter(stream);
            writer.Seek(PacketHeaderSize(), SeekOrigin.Begin);
            writer.Write(command);
            if (bodyData != null) writer.Write(bodyData);
            writer.Flush();
            return stream.ToArray();
        }

        public IPacket Decode(IConnection connection, byte[] data)
        {
            if (data.Length < PacketHeaderSize() + 2) return null;

            var packetHeader = new DefaultPacketHeader();
            packetHeader.ReadFrom(data);
            var command = BitConverter.ToUInt16(data, PacketHeaderSize());
            Console.WriteLine("command:" + command);
            var messageLen = Convert.ToInt32(packetHeader.Len()) - 2;
            if (messageLen <= 0) Console.WriteLine("messageLen:" + messageLen);

            var messageBuffer = data.Skip(PacketHeaderSize() + 2).Take(messageLen).ToArray();
            var messageDescriptor = getMessageDescriptor(command);
            if (messageDescriptor == null) return new ProtoPacket(command, messageBuffer);
            var protoMessage = messageDescriptor.Parser.ParseFrom(messageBuffer);
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
}