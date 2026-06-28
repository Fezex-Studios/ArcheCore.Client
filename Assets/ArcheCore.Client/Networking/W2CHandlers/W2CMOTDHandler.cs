using ArcheCore.Client.Networking;
using LiteNetLib;
using MessagePack;
using Shared.Packets;
using UnityEngine;

namespace ArcheCore.Client.Networking.W2C
{
    public class W2CMOTDHandler
        : IClientPacketHandler
    {
        public void Handle(
            NetPacketReader reader)
        {
            W2CMOTDPacket packet =
                MessagePackSerializer
                    .Deserialize<W2CMOTDPacket>(
                        reader.GetRemainingBytes());

            Debug.Log(
                $"MOTD: {packet.Message}");
        }
    }
}