using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;
using UnityEngine;

namespace ArcheCore.Client.Networking.W2C
{
    public class W2CTestPacketHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            W2CTestPacket packet = MessagePackSerializer.Deserialize<W2CTestPacket>(reader.GetRemainingBytes());
            Debug.Log($"{packet.Message}");
            
            
        }
    }
}