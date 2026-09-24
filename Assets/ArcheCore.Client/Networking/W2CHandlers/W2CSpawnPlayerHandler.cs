using ArchCore.Client;
using ArcheCore.Client.Gameplay;
using ArcheCore.Client.UI;
using ArcheCore.Network.Client;
using LiteNetLib;
using MessagePack;
using ArcheCore.Network.Shared.Packets.W2C;
using UnityEngine;

namespace ArcheCore.Client.Networking.W2C
{
    public class W2CSpawnPlayerHandler
        : IClientPacketHandler
    {
        public void Handle(
            NetPacketReader reader)
        {
            W2CSpawnPlayerPacket packet =
                MessagePackSerializer
                    .Deserialize<W2CSpawnPlayerPacket>(
                        reader.GetRemainingBytes());

            if (packet.IsLocalPlayer)
            {
                // Set immediately so snapshots/positions for "me" are ignored
                // even before the scene finishes loading.
                ClientNetwork.Instance.LocalNetworkId = packet.NetworkId;
                CharacterFlowEvents.RaiseCharacterSpawned();
            }

            // Scene load is owned by WorldLoader - this only queues the spawn.
            WorldLoader.RunWhenReady(() => Spawn(packet));
        }

        private static void Spawn(W2CSpawnPlayerPacket packet)
        {
            var registry = PlayerRegistry.Instance;
            if (registry == null)
            {
                Debug.LogError($"[SpawnPlayer] No PlayerRegistry - cannot spawn NetworkId {packet.NetworkId}.");
                return;
            }

            PlayerController pc = registry.Spawn(
                packet.NetworkId,
                new Vector3(packet.x, packet.y, packet.z),
                packet.IsLocalPlayer);

            if (pc != null && !string.IsNullOrEmpty(packet.MountModelType))
            {
                // Already riding when they came into view.
                ArcheCore.Client.Gameplay.Mounts.MountVisuals.Apply(packet.NetworkId, packet.MountModelType, 1f);
            }

            if (pc != null)
            {
                pc.playerName = packet.Name;
                pc.health = packet.Health;
                pc.maxHealth = packet.MaxHealth;
            }

            if (packet.IsLocalPlayer && pc != null)
            {
                ClientNetwork.Instance.LocalPlayer = pc;
                // Local player object exists now. No ack needed - gold,
                // inventory and character data all arrive via
                // W2CEnterWorld the moment the server spawns the session,
                // and LocalCharacterState caches them regardless of
                // whether this point in scene load has been reached yet.
            }
        }
    }
}
