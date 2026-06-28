using System.IO;
using UnityEngine;

namespace ArcheCore.WorldServer.ServerConfig
{
    public static class ConfigService
    {
        public static ServerConfig Config
        {
            get;
            private set;
        }

        public static void Load()
        {
            string configPath =
                Path.Combine(
                    ServerPaths.Config,
                    "ServerConfig.json");

            if(!File.Exists(configPath))
            {
                Config =
                    new ServerConfig();

                string json =
                    JsonUtility.ToJson(
                        Config,
                        true);

                File.WriteAllText(
                    configPath,
                    json);

                Debug.Log(
                    $"Created Config: {configPath}");
            }
            else
            {
                string json =
                    File.ReadAllText(
                        configPath);

                Config =
                    JsonUtility.FromJson<ServerConfig>(
                        json);

                Debug.Log(
                    $"Loaded Config: {configPath}");
            }
        }
    }
}