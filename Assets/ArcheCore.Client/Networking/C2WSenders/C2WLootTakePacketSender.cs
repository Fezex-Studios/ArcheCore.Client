using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking.C2WSenders
{
    /// <summary>Take one entry from a corpse (itemTemplateId 0 = the gold).</summary>
    public static class C2WLootTakePacketSender
    {
        public static void Send(NetPeer peer, int corpseNetworkId, int itemTemplateId)
        {
            if (peer == null) return;

            ClientPacketSender.SendPacket(
                peer,
                Opcodes.C2WLootTake,
                new C2WLootTakePacket { CorpseNetworkId = corpseNetworkId, ItemTemplateId = itemTemplateId });
        }
    }
}
