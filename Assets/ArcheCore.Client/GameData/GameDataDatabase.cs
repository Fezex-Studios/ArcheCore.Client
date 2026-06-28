using System;
using System.IO;
using SQLite;
using UnityEngine;

namespace ArcheCore.Client.GameData
{
    /// <summary>
    /// Manages the single read-only connection to gamedata.db.
    /// Call Initialize(encryptedDbPath) once during bootstrap before any
    /// repository is used. encryptedDbPath should be the path resolved by
    /// GameDataBootstrap (persistentDataPath copy, kept current by the
    /// hash-check/download pipeline) — not StreamingAssets directly, since
    /// StreamingAssets only ever holds the day-0 baseline shipped in the
    /// build.
    /// </summary>
    public static class GameDataDatabase
    {
        public static SQLiteConnection Connection { get; private set; }

        public static bool IsReady { get; private set; }

        private static string decryptedTempPath;

        public static void Initialize(string encryptedDbPath)
        {
            if (IsReady)
                return;

            if (!File.Exists(encryptedDbPath))
            {
                Debug.LogError(
                    $"[GameDataDatabase] gamedata.db not found at: {encryptedDbPath}");
                return;
            }

            // Decrypt to a throwaway file in the OS temp/cache dir rather
            // than persistentDataPath — we don't want a plaintext copy
            // lingering on disk between sessions. Stale leftovers from a
            // crashed previous run get cleaned up here too.
            decryptedTempPath = Path.Combine(
                Application.temporaryCachePath, "gamedata.decrypted.db");

            if (File.Exists(decryptedTempPath))
                File.Delete(decryptedTempPath);

            try
            {
                if (!GameDataCrypto.DecryptToFile(encryptedDbPath, decryptedTempPath))
                {
                    Debug.LogError(
                        "[GameDataDatabase] gamedata.db is malformed or not encrypted " +
                        "with the expected key — refusing to open.");
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameDataDatabase] Decryption failed: {e.Message}");
                return;
            }

            SQLiteConnectionString options = new SQLiteConnectionString(
                decryptedTempPath,
                SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex,
                storeDateTimeAsTicks: false);

            Connection = new SQLiteConnection(options);
            IsReady    = true;

            Debug.Log("[GameDataDatabase] Opened gamedata.db (decrypted, read-only)");
        }

        public static void Close()
        {
            if (Connection != null)
            {
                Connection.Close();
                Connection = null;
            }

            IsReady = false;

            // Wipe the plaintext temp copy now that we're done with it.
            if (decryptedTempPath != null && File.Exists(decryptedTempPath))
            {
                try   { File.Delete(decryptedTempPath); }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"[GameDataDatabase] Could not delete temp decrypted db: {e.Message}");
                }
            }

            Debug.Log("[GameDataDatabase] Closed gamedata.db");
        }
    }
}