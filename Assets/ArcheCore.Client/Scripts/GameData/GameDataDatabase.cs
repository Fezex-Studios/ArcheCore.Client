using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ArcheCore.Client.GameData
{
    /// <summary>
    /// Loads gamedata into memory once and holds it for the session.
    /// Call Initialize(encryptedDbPath) once during bootstrap before any
    /// repository is used. encryptedDbPath should be the path resolved by
    /// GameDataBootstrap (persistentDataPath copy, kept current by the
    /// hash-check/download pipeline) — not StreamingAssets directly, since
    /// StreamingAssets only ever holds the day-0 baseline shipped in the
    /// build.
    ///
    /// This replaces the previous SQLite-backed version. Since gamedata is
    /// read-only on the client, there's no reason to keep a live connection
    /// or a decrypted temp file on disk at all — decrypt straight into
    /// memory, parse once, and hand repositories a plain Dictionary. This
    /// is both simpler and leaves no plaintext copy on disk, not even
    /// briefly in the temp cache.
    /// </summary>
    public static class GameDataDatabase
    {
        public static IReadOnlyDictionary<int, ItemRecord> Items { get; private set; }

        public static bool IsReady { get; private set; }

        public static void Initialize(string encryptedDbPath)
        {
            if (IsReady)
                return;

            if (!File.Exists(encryptedDbPath))
            {
                Debug.LogError(
                    $"[GameDataDatabase] gamedata file not found at: {encryptedDbPath}");
                return;
            }

            try
            {
                byte[] encrypted = File.ReadAllBytes(encryptedDbPath);
                byte[] plaintext = GameDataCrypto.Decrypt(encrypted);

                using var stream = new MemoryStream(plaintext);
                using var reader = new BinaryReader(stream);

                Items = GameDataBinaryFormat.ReadItems(reader);
            }
            catch (InvalidDataException e)
            {
                Debug.LogError(
                    $"[GameDataDatabase] gamedata file is malformed, wrong " +
                    $"format version, or not encrypted with the expected " +
                    $"key — refusing to use it. {e.Message}");
                return;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameDataDatabase] Load failed: {e.Message}");
                return;
            }

            IsReady = true;
            Debug.Log($"[GameDataDatabase] Loaded {Items.Count} item(s) into memory.");
        }

        public static void Close()
        {
            Items   = null;
            IsReady = false;
            Debug.Log("[GameDataDatabase] Cleared in-memory gamedata.");
        }
    }
}