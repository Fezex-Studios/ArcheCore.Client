using LiteNetLib;
using MessagePack;
using UnityEngine;
using ArcheCore.Client.World;
using ArcheCore.Client.Gameplay;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;

namespace ArcheCore.Client.Networking.W2C
{
    public class W2CSpawnNpcHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer
                .Deserialize<W2CSpawnNpcPacket>(reader.GetRemainingBytes());

            // Scene load and ordering are owned by WorldLoader.
            WorldLoader.RunWhenReady(() => SpawnNpc(packet));
        }

        private static void SpawnNpc(W2CSpawnNpcPacket packet)
        {
            var prefabs = WorldObjectPrefabRegistry.Instance;
            if (prefabs == null)
            {
                Debug.LogError($"[SpawnNpc] No WorldObjectPrefabRegistry - cannot spawn '{packet.Name}' ({packet.NetworkId}).");
                return;
            }

            var prefab = prefabs.GetPrefab(packet.ModelType);
            if (prefab == null)
                return; // GetPrefab already logged the missing ModelType

            var obj = Object.Instantiate(prefab);
            obj.transform.position = WorldOrigin.ToLocal(packet.X, packet.Y, packet.Z);
            obj.name = $"{packet.Name}_{packet.NetworkId}";

            // Attach an identity component so other systems can reference this NPC
            var identity = obj.AddComponent<NpcIdentity>();
            identity.NetworkId  = packet.NetworkId;
            identity.TemplateId = packet.TemplateId;
            identity.NpcName    = packet.Name;
            identity.Level      = packet.Level;
            identity.Health     = packet.Health;
            identity.MaxHealth  = packet.MaxHealth;

            // Without this, nothing can ever find this NPC again to move or
            // despawn it - W2CNpcPositionHandler/W2CNpcDespawnHandler both
            // look entities up by NetworkId through this registry.
            // (NpcRegistry.Register also destroys an older object with the
            // same id, so a duplicate spawn packet can't create two orcs.)
            NpcRegistry.Instance?.Register(identity);

            // Buffs and debuffs it already had when it came into view.
            ArcheCore.Client.Gameplay.Statuses.StatusState.SetAll(packet.NetworkId, packet.Statuses);

            // Makes this NPC a valid target for PlayerInteraction's raycast.
            // NOTE: the prefab's collider also needs to be on the layer
            // PlayerInteraction raycasts against - set that on the prefab.
            var interactable = obj.AddComponent<InteractableIdentity>();
            interactable.NetworkId     = packet.NetworkId;
            interactable.InteractRange = packet.InteractRange;
            interactable.DisplayName   = packet.Name;
            interactable.ActionVerb    = "Talk to";
            interactable.Kind          = InteractableKind.Npc;
            interactable.Title         = packet.Title ?? string.Empty;
            interactable.Actions       = packet.Actions ?? System.Array.Empty<ArcheCore.Network.Shared.Packets.W2C.InteractionActionData>();

            Debug.Log($"[SpawnNpc] Spawned '{packet.Name}' (Lv{packet.Level}) at {obj.transform.position}");
        }
    }
}
