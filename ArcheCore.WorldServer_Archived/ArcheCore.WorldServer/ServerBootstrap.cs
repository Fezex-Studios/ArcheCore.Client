using ArcheCore.WorldServer.ServerConfig;
using UnityEngine;

namespace ArcheCore.WorldServer
{
    public class ServerBootstrap : MonoBehaviour
    {
        private WorldServer server;

        private void Start()
        {
            ServerPaths.Initialize();

            ConfigService.Load();

            server =
                new WorldServer();

            server.Start();
        }

        private void Update()
        {
            server?.Update();
        }

        private void OnApplicationQuit()
        {
            server?.Stop();
        }
    }
}