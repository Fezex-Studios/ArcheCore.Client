using MMO.Shared;
using Shared;
using UnityEngine;

public static class WorldLogger
{
    public static void Info(string message)
    {
        Debug.Log(message);
    }

    public static void Network(Opcode opcode)
    {
        Debug.Log($"[NET] {opcode}");
    }

    public static void Persistence(string message)
    {
        Debug.Log($"[Persistence] {message}");
    }

    public static void Player(string message)
    {
        Debug.Log($"[Player] {message}");
    }
    public static void Warning(string message)
    {
        Debug.LogWarning($"[Warning] {message}");
    }
}