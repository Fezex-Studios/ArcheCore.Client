using ArchCore.Client;
using ArcheCore.Client.Gameplay;
using ArcheCore.Client.Networking.C2W;
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

            if (packet.IsLocalPlayer && pc != null)
            {
                ClientNetwork.Instance.LocalPlayer = pc;

                // Scene is loaded and the local player object exists - this is
                // the actual "ready" moment. HUD elements react to
                // PlayerStatEvents.OnCharacterDataChanged from the reply.
                C2WPlayerSpawnedPacketSender.Send(ClientNetwork.Instance.ServerPeer);
            }
        }
    }
}