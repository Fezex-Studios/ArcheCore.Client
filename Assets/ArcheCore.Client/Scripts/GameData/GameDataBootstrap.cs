using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;

namespace ArcheCore.Client.GameData
{
    /// <summary>
    /// Attach to a GameObject in the server_select scene.
    /// Checks for a new gamedata.bin on the auth server, downloads if
    /// outdated, then loads it before the player connects to the world.
    ///
    /// Note: gamedata.bin is stored encrypted on disk (both the bundled
    /// StreamingAssets copy and whatever the Authserver serves) — see
    /// GameDataCrypto.cs / the Editor's "Export Binary + Encrypt" step.
    /// This class only ever moves the encrypted bytes around;
    /// GameDataDatabase.Initialize() is what decrypts and parses it right
    /// before use.
    ///
    /// Only the file extension changed from the SQLite-backed version
    /// (gamedata.db → gamedata.bin) — the version-check/download/fallback
    /// flow below is otherwise identical, since none of it ever looked at
    /// what was inside the file.
    /// </summary>
    public class GameDataBootstrap : MonoBehaviour
    {
        public static bool IsReady { get; private set; }

        private static readonly HttpClient Http = new HttpClient();

        private const string AuthServerUrl = "http://127.0.0.1:3000";

        private string GameDataDir => Path.Combine(
            Application.persistentDataPath, "GameData");

        private string DbPath   => Path.Combine(GameDataDir, "gamedata.bin");
        private string HashPath => Path.Combine(GameDataDir, "gamedata.hash");

        private string BundledDbPath => Path.Combine(
            Application.streamingAssetsPath, "GameData", "gamedata.bin");

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

            // A newer bundled file wins over the cached copy. Without this the
            // cache only ever filled once: a rebuilt gamedata.bin dropped into
            // StreamingAssets was ignored until someone deleted the cache by
            // hand, which is exactly why new items kept showing as "#7".
            if (!File.Exists(DbPath) || BundledIsNewerThanLocal())
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
            // GameDataRoute itself needs no format-aware changes, it just
            // streams whatever is on disk at GameDatabasePath on the
            // server, which should now be the output of the Editor's
            // Export Binary + Encrypt step, not a raw SQLite file.
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
                    "[GameDataBootstrap] No bundled gamedata.bin in StreamingAssets/GameData/");
                return;
            }

            File.Copy(BundledDbPath, DbPath, overwrite: true);

            // Record the bundled file's hash in the same form the auth server
            // reports (lowercase SHA-256 hex). If the auth server is serving
            // this same file, the version check below then sees "up to date"
            // instead of downloading an identical copy.
            File.WriteAllText(HashPath, Sha256Hex(DbPath));

            Debug.Log("[GameDataBootstrap] Copied bundled gamedata.bin to persistent storage.");
        }

        private bool BundledIsNewerThanLocal()
        {
            if (!File.Exists(BundledDbPath) || !File.Exists(DbPath))
                return false;

            // File.Copy keeps the source's timestamp, so after a copy the two
            // match and this stays false until the bundled file changes again.
            return File.GetLastWriteTimeUtc(BundledDbPath) > File.GetLastWriteTimeUtc(DbPath);
        }

        private static string Sha256Hex(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            byte[] hash = sha.ComputeHash(stream);

            var sb = new System.Text.StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
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