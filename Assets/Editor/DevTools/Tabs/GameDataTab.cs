// GameDataTab.cs
// Assets/Editor/DevTools/Tabs/
//
// The client content pipeline: _oggamedata.db (sqlite, authored by hand)
// → packed binary via GameDataBinaryFormat → AES-256 encrypted →
// StreamingAssets + a local output folder.
//
// Ported verbatim from the legacy ArcheCoreDevTools GameData tab. Logic is
// unchanged — only the plumbing moved (shared settings/log/styles now come
// in through DevToolsContext instead of being window fields).
//
// Getting gamedata.bin onto the AuthServer stays a MANUAL copy. That was a
// deliberate call in the original and it still holds: a tool that reaches
// into the server's folder automatically will eventually overwrite a
// deployed build at exactly the wrong moment.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ArcheCore.Client.GameData;
using UnityEditor;
using UnityEngine;

namespace ArcheCore.Editor.DevTools.Tabs
{
    /// <summary>
    /// Typed read of the `items` table for the binary packer. Kept here
    /// rather than in a shared location because it exists purely to feed
    /// GameDataBinaryFormat.WriteItems — it is export-pipeline business
    /// logic tied to one known table, not a general-purpose data model.
    /// </summary>
    [Serializable]
    [SQLite.Table("items")]
    internal class ItemDataRow
    {
        [SQLite.PrimaryKey, SQLite.Column("item_id")] public int ItemId { get; set; }
        [SQLite.Column("name")]        public string Name        { get; set; } = string.Empty;
        [SQLite.Column("description")] public string Description { get; set; } = string.Empty;
        [SQLite.Column("category")]    public int    Category    { get; set; }
        [SQLite.Column("icon_name")]   public string IconName    { get; set; } = string.Empty;
    }

    public class GameDataTab : IDevToolsTab
    {
        public string Title => "📦  Game Data";
        public int    Order => 50;

        private const string Src = "GameData";

        /// <summary>Must match GameDataCrypto.cs on the client runtime side.</summary>
        private static readonly byte[] CryptoKey =
        {
            0x4B, 0x1C, 0x9E, 0x7A, 0x2D, 0x88, 0x3F, 0x61,
            0xA5, 0x0E, 0xD2, 0x77, 0x9B, 0x44, 0x1A, 0xC3,
            0x6F, 0x52, 0xE8, 0x09, 0xB1, 0x3D, 0x95, 0x2C,
            0x70, 0xF4, 0x18, 0x8A, 0x5C, 0xDB, 0x21, 0x67
        };

        public void OnEnable(DevToolsContext ctx) => ctx.Settings.AutoDetectPaths();
        public void OnDisable(DevToolsContext ctx) { }

        public void OnGUI(DevToolsContext ctx)
        {
            var s = ctx.Settings;

            // ── Paths ──
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("📁  Paths", ctx.Styles.SubHeader);
            EditorGUILayout.Space(3);

            bool changed = false;
            string p = s.plaintextDbPath;
            changed |= DevToolsContext.PathField("Plaintext DB (_oggamedata.db)", ref p, false);
            s.plaintextDbPath = p;

            string e = s.encryptedDbDir;
            changed |= DevToolsContext.PathField("Encrypted DB Output Dir", ref e, true);
            s.encryptedDbDir = e;

            string d = s.decryptedDbOutputDir;
            changed |= DevToolsContext.PathField("Decrypted DB Output Dir", ref d, true);
            s.decryptedDbOutputDir = d;

            if (changed) s.Save();
            EditorGUILayout.EndVertical();

            // ── Status ──
            string streamingDb = Path.Combine(Application.streamingAssetsPath, "GameData", "gamedata.bin");
            string devOutDb = string.IsNullOrEmpty(s.encryptedDbDir)
                ? "" : Path.Combine(s.encryptedDbDir, "gamedata.bin");

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("📊  Status", ctx.Styles.SubHeader);
            EditorGUILayout.Space(2);
            ctx.FileStatus("Plaintext DB (source)", s.plaintextDbPath);
            ctx.FileStatus("StreamingAssets .bin", streamingDb);
            ctx.FileStatus("Encrypted output .bin", devOutDb);
            EditorGUILayout.EndVertical();

            // ── Actions ──
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("⚡  Actions", ctx.Styles.SubHeader);
            EditorGUILayout.Space(3);

            bool hasPlaintext = File.Exists(s.plaintextDbPath);
            bool hasOutputDir = !string.IsNullOrEmpty(s.encryptedDbDir);

            EditorGUI.BeginDisabledGroup(!hasPlaintext || !hasOutputDir);
            if (DevToolsContext.ActionButton(
                "Export Binary + Encrypt → StreamingAssets + Output Dir",
                "Reads the items table from _oggamedata.db, packs it into the custom binary " +
                "format the client reads, AES-256 encrypts it, and writes the result to " +
                "StreamingAssets and the Encrypted DB Output Dir. Does NOT touch the AuthServer " +
                "— copy gamedata.bin over yourself when ready to deploy.",
                new Color(0.2f, 0.55f, 0.9f)))
            {
                ExportEncryptAndDeploy(ctx, s);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(2);

            EditorGUI.BeginDisabledGroup(
                !File.Exists(streamingDb) || string.IsNullOrEmpty(s.decryptedDbOutputDir));
            if (DevToolsContext.ActionButton(
                "Decrypt StreamingAssets → Inspect (.bin)",
                "Decrypts the current StreamingAssets .bin into the Decrypted DB Output Dir for " +
                "debugging. StreamingAssets is left untouched — it should only ever hold the " +
                "encrypted copy. This is the binary format, NOT sqlite: it can't be opened in " +
                "DB Browser and won't overwrite _oggamedata.db, which remains the editable source.",
                new Color(0.55f, 0.35f, 0.75f)))
            {
                DecryptFromStreaming(ctx, s, streamingDb);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(2);

            EditorGUI.BeginDisabledGroup(!hasPlaintext);
            if (DevToolsContext.ActionButton("Open Plaintext DB in Explorer",
                "Opens the folder containing _oggamedata.db.", new Color(0.3f, 0.3f, 0.3f)))
            {
                if (File.Exists(s.plaintextDbPath)) EditorUtility.RevealInFinder(s.plaintextDbPath);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(2);

            EditorGUI.BeginDisabledGroup(!hasOutputDir || !Directory.Exists(s.encryptedDbDir));
            if (DevToolsContext.ActionButton("Open Encrypted Output Folder",
                "Opens the Encrypted DB Output Dir so you can copy gamedata.bin to the AuthServer.",
                new Color(0.3f, 0.3f, 0.3f)))
            {
                EditorUtility.RevealInFinder(s.encryptedDbDir);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────────────────────────
        private void ExportEncryptAndDeploy(DevToolsContext ctx, ArcheCoreDevToolsSettings s)
        {
            try
            {
                byte[] binary    = ExportItemsToBinary(ctx, s.plaintextDbPath);
                byte[] encrypted = EncryptAes(binary);

                string streamingDir = Path.Combine(Application.streamingAssetsPath, "GameData");
                Directory.CreateDirectory(streamingDir);
                File.WriteAllBytes(Path.Combine(streamingDir, "gamedata.bin"), encrypted);
                ctx.Success($"Written to StreamingAssets/GameData/gamedata.bin ({encrypted.Length:N0} bytes)", Src);

                string devOut = Path.Combine(s.encryptedDbDir, "gamedata.bin");
                File.WriteAllBytes(devOut, encrypted);
                ctx.Success($"Written to Encrypted DB Output Dir: {devOut}", Src);
                ctx.Info($"Reminder: NOT copied to the AuthServer. Copy {devOut} over manually " +
                         "when you're ready to deploy.", Src);

                AssetDatabase.Refresh();
                ctx.Success("Export + Encrypt complete.", Src);
            }
            catch (Exception ex)
            {
                ctx.Error($"Export/encryption failed: {ex.Message}", Src);
            }
        }

        /// <summary>
        /// Reads `items` straight from the sqlite file on disk — never from
        /// an in-editor cache — so the export always reflects what's actually
        /// saved, not what some other tab happens to have loaded.
        /// </summary>
        private byte[] ExportItemsToBinary(DevToolsContext ctx, string plaintextDbPath)
        {
            List<ItemDataRow> rows;
            using (var conn = new SQLite.SQLiteConnection(plaintextDbPath))
                rows = conn.Table<ItemDataRow>().OrderBy(r => r.ItemId).ToList();

            var records = rows.Select(r => new ItemRecord
            {
                ItemId      = r.ItemId,
                Name        = r.Name,
                Description = r.Description,
                Category    = r.Category,
                IconName    = r.IconName
            }).ToList();

            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms))
                GameDataBinaryFormat.WriteItems(writer, records);

            ctx.Info($"Packed {records.Count} item(s) into binary format " +
                     $"v{GameDataBinaryFormat.CurrentVersion}.", Src);

            return ms.ToArray();
        }

        private void DecryptFromStreaming(DevToolsContext ctx, ArcheCoreDevToolsSettings s, string streamingDb)
        {
            try
            {
                byte[] decrypted = DecryptAes(File.ReadAllBytes(streamingDb));

                // Inspection output only — deliberately NOT back into
                // StreamingAssets (which holds the encrypted copy) and NOT to
                // plaintextDbPath, since this is packed binary, not valid
                // sqlite, and overwriting the editable source would break it.
                Directory.CreateDirectory(s.decryptedDbOutputDir);
                string outputPath = Path.Combine(s.decryptedDbOutputDir, "gamedata.decrypted.bin");
                File.WriteAllBytes(outputPath, decrypted);

                ctx.Success($"Decrypted to {outputPath} ({decrypted.Length:N0} bytes). " +
                            "_oggamedata.db is unaffected.", Src);
            }
            catch (Exception ex)
            {
                ctx.Error($"Decryption failed: {ex.Message}", Src);
            }
        }

        // ── Crypto ────────────────────────────────────────────────────────
        private static byte[] EncryptAes(byte[] plaintext)
        {
            using var aes = Aes.Create();
            aes.Key = CryptoKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();

            using var ms = new MemoryStream();
            using var encryptor = aes.CreateEncryptor();

            ms.Write(aes.IV, 0, aes.IV.Length);
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                cs.Write(plaintext, 0, plaintext.Length);

            return ms.ToArray();
        }

        private static byte[] DecryptAes(byte[] data)
        {
            const int ivLen = 16;
            if (data.Length <= ivLen)
                throw new InvalidDataException("Data too short to contain IV.");

            byte[] iv = new byte[ivLen];
            byte[] cipherText = new byte[data.Length - ivLen];
            Buffer.BlockCopy(data, 0, iv, 0, ivLen);
            Buffer.BlockCopy(data, ivLen, cipherText, 0, cipherText.Length);

            using var aes = Aes.Create();
            aes.Key = CryptoKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(new MemoryStream(cipherText), decryptor, CryptoStreamMode.Read))
                cs.CopyTo(ms);

            return ms.ToArray();
        }
    }
}