using ArcheCore.Client.Gameplay;
using ArcheCore.Client.UI;
using ArcheCore.Client.World;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;
using UnityEngine;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>
    /// Opcode 43. A node came into view. Mirrors W2CSpawnNpcHandler: the
    /// model comes from WorldObjectPrefabRegistry by ModelType, so a new
    /// kind of node is a prefab entry plus server data - no code.
    ///
    /// The prefab needs a Collider on the same layer PlayerInteraction
    /// raycasts against, exactly like NPC prefabs, or it can't be clicked.
    /// </summary>
    public class W2CSpawnHarvestNodeHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CSpawnHarvestNodePacket>(reader.GetRemainingBytes());
            WorldLoader.RunWhenReady(() => Spawn(packet));
        }

        private static void Spawn(W2CSpawnHarvestNodePacket packet)
        {
            var prefabs = WorldObjectPrefabRegistry.Instance;
            if (prefabs == null)
            {
                Debug.LogError($"[HarvestNode] No WorldObjectPrefabRegistry - cannot spawn '{packet.Name}' ({packet.NetworkId}).");
                return;
            }

            var prefab = prefabs.GetPrefab(packet.ModelType);
            if (prefab == null)
                return; // GetPrefab already warned which ModelType is missing

            var obj = Object.Instantiate(
                prefab,
                new Vector3(packet.X, packet.Y, packet.Z),
                Quaternion.Euler(0f, packet.Yaw, 0f));

            obj.name = $"{packet.Name}_{packet.NetworkId}";

            // Reuse components already on the prefab (that's where depleted
            // visuals are configured) and only add what's missing.
            // TryGetComponent, not "GetComponent() ?? AddComponent()": in the
            // Editor a missing component comes back as a fake-null object
            // that ?? treats as real, so the Add would silently never run.
            if (!obj.TryGetComponent(out HarvestNodeIdentity node))
                node = obj.AddComponent<HarvestNodeIdentity>();
            node.NetworkId  = packet.NetworkId;
            node.TemplateId = packet.TemplateId;
            node.NodeName   = packet.Name;

            if (!obj.TryGetComponent(out InteractableIdentity interactable))
                interactable = obj.AddComponent<InteractableIdentity>();
            interactable.NetworkId     = packet.NetworkId;
            interactable.InteractRange = packet.InteractRange;
            interactable.DisplayName   = packet.Name;
            interactable.ActionVerb    = "Gather";
            interactable.Kind          = InteractableKind.HarvestNode;
            interactable.Actions       = packet.Actions ?? System.Array.Empty<InteractionActionData>();

            HarvestNodeRegistry.Register(node);
            node.SetDepleted(packet.IsDepleted);
        }
    }

    /// <summary>Opcode 44. A visible node was depleted or respawned.</summary>
    public class W2CHarvestNodeStateHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CHarvestNodeStatePacket>(reader.GetRemainingBytes());
            WorldLoader.RunWhenReady(() => HarvestNodeRegistry.SetDepleted(packet.NetworkId, packet.IsDepleted));
        }
    }

    /// <summary>Opcode 45. Your harvest began - show the bar.</summary>
    public class W2CHarvestStartedHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CHarvestStartedPacket>(reader.GetRemainingBytes());

            if (HarvestProgressUI.Instance != null)
                HarvestProgressUI.Instance.Begin(packet.NodeName, packet.DurationMs);
            else
                HudMessageDisplay.QueueOrShow($"Gathering {packet.NodeName}...");
        }
    }

    /// <summary>
    /// Opcode 46. Your harvest finished. The item already arrived through
    /// W2CInventorySlotChanged; this is only the feedback.
    /// </summary>
    public class W2CHarvestCompletedHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CHarvestCompletedPacket>(reader.GetRemainingBytes());

            HarvestProgressUI.Instance?.End();
            HudMessageDisplay.QueueOrShow($"+{packet.Quantity} {packet.ItemName}");
        }
    }

    /// <summary>Opcode 47. Your harvest stopped early.</summary>
    public class W2CHarvestCancelledHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CHarvestCancelledPacket>(reader.GetRemainingBytes());

            HarvestProgressUI.Instance?.End();

            if (!string.IsNullOrEmpty(packet.Reason))
                HudMessageDisplay.QueueOrShow(packet.Reason);
        }
    }
}
