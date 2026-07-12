using LiteNetLib;
using MessagePack;
using UnityEngine;
using UnityEngine.SceneManagement;
using ArcheCore.Client.World;
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

            ClientNetwork.Instance.StartCoroutine(SpawnNpc(packet));
        }

        private System.Collections.IEnumerator SpawnNpc(W2CSpawnNpcPacket packet)
        {
            if (SceneManager.GetActiveScene().name != "main_world")
                yield return new WaitUntil(() =>
                    SceneManager.GetActiveScene().name == "main_world");

            yield return new WaitUntil(() =>
                WorldObjectPrefabRegistry.Instance != null);

            var prefab = WorldObjectPrefabRegistry.Instance.GetPrefab(packet.ModelType);
            if (prefab == null) yield break;

            var obj = GameObject.Instantiate(prefab);
            obj.transform.position = new Vector3(packet.X, packet.Y, packet.Z);
            obj.name = $"{packet.Name}_{packet.NetworkId}";

            // Attach an identity component so other systems can reference this NPC
            var identity = obj.AddComponent<NpcIdentity>();
            identity.NetworkId  = packet.NetworkId;
            identity.TemplateId = packet.TemplateId;
            identity.NpcName    = packet.Name;
            identity.Level      = packet.Level;

            // Makes this NPC a valid target for PlayerInteraction's raycast.
            // NOTE: the prefab's collider also needs to be on the layer
            // PlayerInteraction is configured to raycast against - set that
            // in the Inspector on your NPC prefab, this can't be done from
            // code alone.
            var interactable = obj.AddComponent<InteractableIdentity>();
            interactable.NetworkId = packet.NetworkId;
            interactable.InteractRange = packet.InteractRange;

            Debug.Log($"[SpawnNpc] Spawned '{packet.Name}' (Lv{packet.Level}) at {obj.transform.position}");
        }
    }
}