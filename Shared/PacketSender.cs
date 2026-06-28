using LiteNetLib;
using LiteNetLib.Utils;
using MessagePack;
using MMO.Shared;

namespace Shared
{
    public static class PacketSender
    {
        public static void SendPacket<T>(
            NetPeer peer,
            Opcode opcode,
            T payload,
            DeliveryMethod delivery = DeliveryMethod.ReliableOrdered)
        {
            NetDataWriter writer =
                new();

            writer.Put(
                (ushort)opcode);

            writer.Put(
                MessagePackSerializer
                    .Serialize(payload));

            peer.Send(
                writer,
                delivery);
        }
    }
}