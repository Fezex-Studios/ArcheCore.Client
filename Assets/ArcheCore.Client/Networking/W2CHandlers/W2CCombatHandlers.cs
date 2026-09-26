using ArcheCore.Client.Gameplay.Combat;
using ArcheCore.Client.UI.State;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>Opcode 53. A hit landed on something you can see.</summary>
    public class W2CCombatEventHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CCombatEventPacket>(reader.GetRemainingBytes());
            // Same ordering guarantee as NPC spawns: never before the target exists.
            WorldLoader.RunWhenReady(() => CombatClient.HandleCombatEvent(packet));
        }
    }

    /// <summary>Opcode 54. Your own health and mana changed (potion, level-up, regen, a skill's cost).</summary>
    public class W2CHealthUpdateHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CHealthUpdatePacket>(reader.GetRemainingBytes());
            LocalCharacterState.ApplyHealth(packet.Health, packet.MaxHealth);
            LocalCharacterState.ApplyMana(packet.Mana, packet.MaxMana);
        }
    }

    /// <summary>Opcode 55. A corpse came into view. Removed by W2CNpcDespawn.</summary>
    public class W2CSpawnCorpseHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CSpawnCorpsePacket>(reader.GetRemainingBytes());
            WorldLoader.RunWhenReady(() => CorpseRegistry.Spawn(packet));
        }
    }
}
