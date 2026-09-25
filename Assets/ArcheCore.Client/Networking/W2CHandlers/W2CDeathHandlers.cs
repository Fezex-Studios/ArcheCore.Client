using ArchCore.Client;
using ArcheCore.Client.Gameplay;
using ArcheCore.Client.Gameplay.Combat;
using ArcheCore.Client.UI;
using ArcheCore.Client.UI.State;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;
using UnityEngine;
using ArcheCore.Client.World;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>Opcode 58. You died: lock input and show the death screen.</summary>
    public class W2CPlayerDeathHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CPlayerDeathPacket>(reader.GetRemainingBytes());

            WorldLoader.RunWhenReady(() =>
            {
                DeathState.SetDead(true);

                // Nothing should carry on through death.
                CombatClient.SetTarget(0);
                ItemTooltipUI.Hide();

                DeathScreenUI.Show(packet.KillerName);
            });
        }
    }

    /// <summary>
    /// Opcode 60. You're back. Teleports the local player to where the
    /// server put them through ForcePosition - the same path a position
    /// correction uses, so the motor and the server agree straight away.
    /// </summary>
    public class W2CRespawnHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CRespawnPacket>(reader.GetRemainingBytes());

            WorldLoader.RunWhenReady(() =>
            {
                DeathState.SetDead(false);
                DeathScreenUI.Hide();

                LocalCharacterState.ApplyHealth(packet.Health, packet.MaxHealth);

                var position = WorldOrigin.ToLocal(packet.X, packet.Y, packet.Z);
                int localId = CombatClient.LocalPlayerId;

                if (localId != 0 && PlayerRegistry.Instance != null &&
                    PlayerRegistry.Instance.TryGetPlayer(localId, out var pc) && pc != null)
                    pc.ForcePosition(position);
                else
                    Debug.LogWarning("[Respawn] No local player to move - the server has you at " + position);
            });
        }
    }
}
