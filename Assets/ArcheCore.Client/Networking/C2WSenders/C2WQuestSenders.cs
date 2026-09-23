using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking.C2WSenders
{
    /// <summary>The three quest requests. The server re-checks all of them.</summary>
    public static class C2WQuestPacketSender
    {
        public static void Accept(NetPeer peer, int npcNetworkId, int questId)
        {
            if (peer == null) return;
            ClientPacketSender.SendPacket(peer, Opcodes.C2WQuestAccept,
                new C2WQuestAcceptPacket { NpcNetworkId = npcNetworkId, QuestId = questId });
        }

        public static void Complete(NetPeer peer, int npcNetworkId, int questId)
        {
            if (peer == null) return;
            ClientPacketSender.SendPacket(peer, Opcodes.C2WQuestComplete,
                new C2WQuestCompletePacket { NpcNetworkId = npcNetworkId, QuestId = questId });
        }

        public static void Abandon(NetPeer peer, int questId)
        {
            if (peer == null) return;
            ClientPacketSender.SendPacket(peer, Opcodes.C2WQuestAbandon,
                new C2WQuestAbandonPacket { QuestId = questId });
        }
    }
}
