using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;

namespace ArcheCore.Client.GameData
{
    /// <summary>
    /// Attach to a GameObject in the server_select scene.
    /// Checks for a new gamedata.db on the auth server, downloads if outdated,
    /// then opens the DB before the player connects to the world.
    ///
    /// Note: gamedata.db is stored encrypted on disk (both the bundled
    /// StreamingAssets copy and whatever the Authserver serves) — see
    /// GameDataCrypto.cs / encrypt_gamedata.py. This class only ever moves
    /// the encrypted bytes around; GameDataDatabase.Initialize() is what
    /// decrypts it to a temp file right before opening the connection.
    /// </summary>
    public class GameDataBootstrap : MonoBehaviour
    {
        public static bool IsReady { get; private set; }

        private static readonly HttpClient Http = new HttpClient();

        private const string AuthServerUrl = "http://127.0.0.1:3000";

        private string GameDataDir => Path.Combine(
            Application.persistentDataPath, "GameData");

        private string DbPath   => Path.Combine(GameDataDir, "gamedata.db");
        private string HashPath => Path.Combine(GameDataDir, "gamedata.hash");

        private string BundledDbPath => Path.Combine(
            Application.streamingAssetsPath, "GameData", "gamedata.db");

        private async void Start()
        {
            try
            {
                await InitializeAsync();
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[GameDataBootstrap] Initialization failed: {e.Message}. " +
                    "Falling back to bundled database.");

                TryOpenBundled();
            }
        }

        private async Task InitializeAsync()
        {
            Directory.CreateDirectory(GameDataDir);

            if (!File.Exists(DbPath))
                CopyBundledToLocal();

            string localHash  = ReadLocalHash();
            string remoteHash = await FetchRemoteHash();

            if (remoteHash == null)
            {
                Debug.LogWarning(
                    "[GameDataBootstrap] Could not reach version endpoint. " +
                    "Using existing local database.");
            }
            else if (remoteHash != localHash)
            {
                Debug.Log(
                    $"[GameDataBootstrap] New version detected " +
                    $"({localHash ?? "none"} → {remoteHash}). Downloading...");

                await DownloadDatabase(remoteHash);

                Debug.Log("[GameDataBootstrap] Download complete.");
            }
            else
            {
                Debug.Log("[GameDataBootstrap] Game data is up to date.");
            }

            // Bug fix: previously GameDataDatabase.Initialize() ignored this
            // resolved path entirely and re-opened StreamingAssets directly,
            // so a freshly downloaded update was never actually loaded.
            GameDataDatabase.Initialize(DbPath);
            IsReady = GameDataDatabase.IsReady;
        }

        // ── Version check ─────────────────────────────────────────────────────

        private async Task<string> FetchRemoteHash()
        {
            try
            {
                string response = await Http.GetStringAsync(
                    $"{AuthServerUrl}/gamedata/version");

                const string key = "\"hash\":\"";
                int          idx = response.IndexOf(key);

                if (idx == -1) return null;

                int start = idx + key.Length;
                int end   = response.IndexOf('"', start);

                return end == -1 ? null : response.Substring(start, end - start);
            }
            catch
            {
                return null;
            }
        }

        // ── Download ──────────────────────────────────────────────────────────

        private async Task DownloadDatabase(string newHash)
        {
            // Bytes here are the ENCRYPTED file as served by GameDataRoute —
            // GameDataRoute itself needs no changes, it just streams whatever
            // is on disk at GameDatabasePath on the server, which should be
            // the output of encrypt_gamedata.py, not the raw plaintext db.
            byte[] data = await Http.GetByteArrayAsync(
                $"{AuthServerUrl}/gamedata/db");

            string tempPath = DbPath + ".tmp";

            // Use synchronous file writes — Unity's runtime doesn't have
            // WriteAllBytesAsync / WriteAllTextAsync
            File.WriteAllBytes(tempPath, data);

            if (File.Exists(DbPath))
                File.Delete(DbPath);

            File.Move(tempPath, DbPath);

            File.WriteAllText(HashPath, newHash);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void CopyBundledToLocal()
        {
            if (!File.Exists(BundledDbPath))
            {
                Debug.LogWarning(
                    "[GameDataBootstrap] No bundled gamedata.db in StreamingAssets/GameData/");
                return;
            }

            File.Copy(BundledDbPath, DbPath);
            Debug.Log("[GameDataBootstrap] Copied bundled gamedata.db to persistent storage.");
        }

        private string ReadLocalHash()
        {
            if (!File.Exists(HashPath))
                return null;

            return File.ReadAllText(HashPath).Trim();
        }

        private void TryOpenBundled()
        {
            if (!File.Exists(DbPath))
                CopyBundledToLocal();

            GameDataDatabase.Initialize(DbPath);
            IsReady = GameDataDatabase.IsReady;
        }

        private void OnApplicationQuit()
        {
            GameDataDatabase.Close();
        }
    }
}