using ArcheCore.Client.Gameplay;
using LiteNetLib;
using MessagePack;
using Shared.Components;
using UnityEngine;
using UnityEngine.SceneManagement;

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
                ClientNetwork.Instance
                        .LocalNetworkId =
                    packet.NetworkId;
            }

            ClientNetwork.Instance
                .StartCoroutine(
                    LoadWorldThenSpawn(packet));
        }

        private System.Collections.IEnumerator LoadWorldThenSpawn(
            W2CSpawnPlayerPacket packet)
        {
            if (SceneManager
                    .GetActiveScene().name != "main_world")
            {
                AsyncOperation load =
                    SceneManager
                        .LoadSceneAsync("main_world");

                yield return load;
            }

            PlayerRegistry.Instance?.Spawn(
                packet.NetworkId,
                new Vector3(
                    packet.x,
                    packet.y,
                    packet.z),
                packet.IsLocalPlayer);
        }
    }
}