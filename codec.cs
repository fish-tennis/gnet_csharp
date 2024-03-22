﻿using System;
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

        IPacketHeader DecodePacketHeader(IConnection connection, byte[] data, int startIndex, int length);

        /// <summary>
        ///     encode a packet to stream data
        /// </summary>
        byte[] Encode(IConnection connection, IPacket packet);

        /// <summary>
        ///     decode a packet from stream data
        /// </summary>
        IPacket Decode(IConnection connection, byte[] data, int startIndex, int length);
    }

    public delegate byte[] PacketDataEncoder(IConnection connection, IPacket packet, byte[] data, int startIndex,
        int length);

    public delegate byte[] PacketDataDecoder(IConnection connection, byte[] data, int startIndex, int length);

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

        public IPacketHeader DecodePacketHeader(IConnection connection, byte[] data, int startIndex, int length)
        {
            if (length < PacketHeaderSize())
            {
                return null;
            }

            var decodeHeaderData = data;
            if (DataDecoder != null)
            {
                decodeHeaderData = DataDecoder.Invoke(connection, data, startIndex, PacketHeaderSize());
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
            packetHeader.WriteTo(fullPacketData);
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
                : DataEncoder.Invoke(connection, packet, packetBytes, 0, packetBytes.Length);
        }

        public IPacket Decode(IConnection connection, byte[] data, int startIndex, int length)
        {
            if (data.Length < PacketHeaderSize() + 2) return null;
            // Q:DataDecoder可以对data进行解码,如异或,解密,解压等
            // DataDecoder can decode data here, such as XOR, decryption, decompression, etc
            if (DataDecoder != null)
            {
                data = DataDecoder.Invoke(connection, data, startIndex, length);
            }

            var packetHeader = new DefaultPacketHeader();
            packetHeader.ReadFrom(data);
            var command = BitConverter.ToUInt16(data, PacketHeaderSize());
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
            for (var i = startIndex; i < startIndex + length; i++)
            {
                data[i] = (byte) (data[i] ^ m_XorKey[i % m_XorKey.Length]);
            }

            return data;
        }

        private byte[] xorDataEncoder(IConnection connection, IPacket packet, byte[] data, int startIndex, int length)
        {
            return xorEncode(data, startIndex, length);
        }

        private byte[] xorDataDecoder(IConnection connection, byte[] data, int startIndex, int length)
        {
            return xorEncode(data, startIndex, length);
        }
    }
}