using ArcheCore.Client.Gameplay.Quests;
using ArcheCore.Client.UI;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>Opcode 62. Every quest definition, once on entering the world.</summary>
    public class W2CQuestCatalogHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CQuestCatalogPacket>(reader.GetRemainingBytes());
            QuestState.SetCatalog(packet.Quests);
        }
    }

    /// <summary>Opcode 63. This character's whole quest log.</summary>
    public class W2CQuestLogHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CQuestLogPacket>(reader.GetRemainingBytes());
            QuestState.SetLog(packet.Quests);
        }
    }

    /// <summary>Opcode 64. One quest changed - accepted, progressed, done, dropped.</summary>
    public class W2CQuestUpdateHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CQuestUpdatePacket>(reader.GetRemainingBytes());

            QuestState.Apply(packet.Quest, packet.Removed);

            if (!string.IsNullOrEmpty(packet.Message))
                HudMessageDisplay.QueueOrShow(packet.Message);
        }
    }

    /// <summary>Opcode 65. What a quest giver has for you - opens their window.</summary>
    public class W2CQuestOffersHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CQuestOffersPacket>(reader.GetRemainingBytes());
            WorldLoader.RunWhenReady(() => QuestDialogueUI.Present(packet));
        }
    }
}
