// ArcheCoreDevToolsSettings.cs
// Assets/Editor/DevTools/
//
// EXTRACTED from ArcheCoreDevTools.cs so that file can be deleted without
// taking every path setting with it. Every tab reads this through
// DevToolsContext.Settings — it is the single source of truth for where
// the databases and patch directory live, deliberately shared rather than
// per-tab, because two tabs pointing at different copies of the same
// database is a genuinely confusing bug to chase.
//
// The field names are unchanged from the original, so the existing
// ProjectSettings/ArcheCoreDevTools.asset deserializes straight into this
// and your configured paths survive the migration.

using UnityEditor;

namespace ArcheCore.Editor
{
    [FilePath("ProjectSettings/ArcheCoreDevTools.asset",
              FilePathAttribute.Location.ProjectFolder)]
    public class ArcheCoreDevToolsSettings : ScriptableSingleton<ArcheCoreDevToolsSettings>
    {
        // ── Client game data pipeline ────────────────────────────────────
        public string plaintextDbPath      = "";  // _oggamedata.db — sqlite source, authored by hand
        public string encryptedDbDir       = "";  // where Export writes gamedata.bin; copy to AuthServer manually
        public string decryptedDbOutputDir = "";  // where "Decrypt → Inspect" writes, deliberately NOT StreamingAssets

        // ── Server ───────────────────────────────────────────────────────
        public string serverPatchDir    = "";  // ArcheCore.Server.World/SQL/patches
        public string worldServerDbPath = "";  // Data/worldserver.db — the LIVE runtime db,
                                               // a completely different file from plaintextDbPath

        public void Save() => Save(true);

        /// <summary>
        /// Best-effort defaults based on the usual repo layout next to the
        /// Unity project. Only fills in blanks — never overwrites a path the
        /// user has already set, since a wrong auto-detect that silently
        /// replaces a correct manual path is worse than no auto-detect.
        /// </summary>
        public void AutoDetectPaths()
        {
            string root = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(UnityEngine.Application.dataPath, "../../.."));

            if (string.IsNullOrEmpty(serverPatchDir))
            {
                string candidate = System.IO.Path.Combine(
                    root, "ArcheCore", "src", "ArcheCore.Server.World", "SQL", "patches");
                if (System.IO.Directory.Exists(candidate)) serverPatchDir = candidate;
            }

            if (string.IsNullOrEmpty(plaintextDbPath))
            {
                string devTools = System.IO.Path.Combine(root, "ArcheCore.DevTools", "ArcheCore.DevTools");
                if (System.IO.Directory.Exists(devTools))
                {
                    plaintextDbPath = System.IO.Path.Combine(devTools, "_oggamedata.db");
                    encryptedDbDir  = devTools;
                }
            }

            if (string.IsNullOrEmpty(decryptedDbOutputDir) && !string.IsNullOrEmpty(plaintextDbPath))
            {
                string folder = System.IO.Path.GetDirectoryName(plaintextDbPath);
                if (!string.IsNullOrEmpty(folder))
                    decryptedDbOutputDir = System.IO.Path.Combine(folder, "decrypted_db_output");
            }

            if (string.IsNullOrEmpty(worldServerDbPath))
            {
                // Matches appsettings.json → Database:WorldDb = "Data/worldserver.db"
                string candidate = System.IO.Path.Combine(
                    root, "ArcheCore", "src", "ArcheCore.Server.World", "Data", "worldserver.db");
                if (System.IO.File.Exists(candidate)) worldServerDbPath = candidate;
            }

            Save();
        }
    }
}