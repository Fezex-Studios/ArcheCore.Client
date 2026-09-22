using ArcheCore.Client.UI;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>Opcode 56. A corpse's contents - open or refresh the loot window.</summary>
    public class W2CLootWindowHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CLootWindowPacket>(reader.GetRemainingBytes());
            WorldLoader.RunWhenReady(() => LootWindowUI.Present(packet));
        }
    }
}
