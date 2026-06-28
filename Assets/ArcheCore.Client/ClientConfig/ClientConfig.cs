using System;
using System.IO;
using UnityEngine;

[Serializable]
public class ClientConfig
{
    public string AuthServerUrl = "http://127.0.0.1:3000";
}

public static class ClientConfigService
{
    public static ClientConfig Config { get; private set; } = new ClientConfig();

    public static void Load()
    {
        string path = Path.Combine(
            Application.streamingAssetsPath,
            "ClientConfig.json");

        if (!File.Exists(path))
            return;

        Config = JsonUtility.FromJson<ClientConfig>(
            File.ReadAllText(path));
    }
}