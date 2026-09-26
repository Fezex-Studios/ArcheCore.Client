using ArcheCore.Client.GameData;
using ArcheCore.Client.Gameplay.Combat;
using ArcheCore.Client.Gameplay.Statuses;
using ArcheCore.Client.UI.State;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    // Phase 3 handlers (opcodes 83-89). Registered in ClientNetwork.RegisterHandlers.

    /// <summary>Opcode 83. Your whole stat sheet.</summary>
    public class W2CStatsHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CStatsPacket>(reader.GetRemainingBytes());
            LocalCharacterState.ApplyStats(packet.Values, packet.BaseValues);
        }
    }

    /// <summary>Opcode 84. Everything you're wearing.</summary>
    public class W2CEquipmentHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CEquipmentPacket>(reader.GetRemainingBytes());
            LocalCharacterState.ApplyEquipment(packet.Slots);
        }
    }

    /// <summary>Opcode 85. Slot, stats and binding of every gear item.</summary>
    public class W2CItemGearCatalogHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CItemGearCatalogPacket>(reader.GetRemainingBytes());
            GearCatalog.Set(packet.Items);
        }
    }

    /// <summary>Opcode 87. Every buff and debuff definition.</summary>
    public class W2CStatusCatalogHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CStatusCatalogPacket>(reader.GetRemainingBytes());
            StatusState.SetCatalog(packet.Statuses);
        }
    }

    /// <summary>
    /// Opcode 88. A status changed on something you can see. Queued behind
    /// spawns like combat events, so it never lands before its entity.
    /// </summary>
    public class W2CStatusUpdateHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CStatusUpdatePacket>(reader.GetRemainingBytes());
            WorldLoader.RunWhenReady(() => StatusState.Apply(packet.TargetId, packet.Status, packet.Removed));
        }
    }

    /// <summary>Opcode 89. Your skills, for the skill bar.</summary>
    public class W2CSkillCatalogHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CSkillCatalogPacket>(reader.GetRemainingBytes());
            SkillBook.SetCatalog(packet.Skills);
        }
    }
}
