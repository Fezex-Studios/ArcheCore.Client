// SqlPatchesTab.cs
// Assets/Editor/DevTools/Tabs/
//
// Browse and author .sql patch files in the server's SQL/patches directory.
//
// Ported from the legacy ArcheCoreDevTools Patches tab. Logic unchanged;
// only the plumbing moved. One addition: HasUnsavedChanges reports a draft
// in the SQL box, so the tab bar marks it and you can't lose typed SQL by
// clicking away without noticing.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ArcheCore.Editor.DevTools.Tabs
{
    public class SqlPatchesTab : IDevToolsTab
    {
        public string Title => "🧩  SQL Patches";
        public int    Order => 60;

        private const string Src = "Patches";

        private string  _patchName = "";
        private string  _patchSql  = "";
        private Vector2 _patchScroll;
        private List<string> _existingPatches = new();

        /// <summary>
        /// Typed-but-unwritten SQL counts as unsaved. The original window had
        /// no notion of this, so switching tabs mid-draft silently kept it
        /// around with no indication — fine with four tabs you rarely leave,
        /// worth flagging once there are six.
        /// </summary>
        public bool HasUnsavedChanges => !string.IsNullOrWhiteSpace(_patchSql);

        public void OnEnable(DevToolsContext ctx)
        {
            ctx.Settings.AutoDetectPaths();
            RefreshPatches(ctx);
        }

        public void OnDisable(DevToolsContext ctx) { }

        public void OnGUI(DevToolsContext ctx)
        {
            var s = ctx.Settings;

            // ── Patch directory ──
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("📁  Server Patch Directory", ctx.Styles.SubHeader);
            EditorGUILayout.Space(3);

            string dir = s.serverPatchDir;
            if (DevToolsContext.PathField("", ref dir, true))
            {
                s.serverPatchDir = dir;
                s.Save();
                RefreshPatches(ctx);
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("↻ Refresh", EditorStyles.miniButton, GUILayout.Width(70)))
                RefreshPatches(ctx);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            // ── Existing patches ──
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"📋  Existing Patches  ({_existingPatches.Count})",
                ctx.Styles.SubHeader);
            EditorGUILayout.Space(3);

            if (_existingPatches.Count == 0)
            {
                EditorGUILayout.HelpBox("No .sql patches found in the patch directory.",
                    MessageType.Info);
            }
            else
            {
                _patchScroll = EditorGUILayout.BeginScrollView(_patchScroll, GUILayout.MaxHeight(150));
                foreach (var patch in _existingPatches)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(Path.GetFileName(patch), EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(42)))
                    {
                        // UseShellExecute is required on .NET Core/5+ to open a
                        // file with its registered application; without it this
                        // throws Win32Exception instead of opening the editor.
                        try
                        {
                            System.Diagnostics.Process.Start(
                                new System.Diagnostics.ProcessStartInfo(patch) { UseShellExecute = true });
                        }
                        catch (Exception ex) { ctx.Error($"Couldn't open {Path.GetFileName(patch)}: {ex.Message}", Src); }
                    }

                    if (GUILayout.Button("📂", EditorStyles.miniButton, GUILayout.Width(24)))
                        EditorUtility.RevealInFinder(patch);

                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();

            // ── Write new patch ──
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("✏️  Write New Patch", ctx.Styles.SubHeader);
            EditorGUILayout.Space(3);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Patch Name", GUILayout.Width(80));
            _patchName = EditorGUILayout.TextField(_patchName);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("SQL", EditorStyles.miniLabel);
            _patchSql = EditorGUILayout.TextArea(_patchSql, GUILayout.Height(100));

            EditorGUILayout.Space(3);

            bool canWrite = !string.IsNullOrEmpty(_patchName)
                            && !string.IsNullOrEmpty(_patchSql)
                            && !string.IsNullOrEmpty(s.serverPatchDir);

            EditorGUI.BeginDisabledGroup(!canWrite);
            if (DevToolsContext.ActionButton("Write Patch File",
                "Saves the SQL above as a new .sql file in the patch directory.",
                new Color(0.2f, 0.55f, 0.9f)))
            {
                WritePatch(ctx, s.serverPatchDir);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();

            EditorGUI.BeginDisabledGroup(
                string.IsNullOrEmpty(s.serverPatchDir) || !Directory.Exists(s.serverPatchDir));
            if (DevToolsContext.ActionButton("Open Patch Folder in Explorer",
                "Opens the SQL patches directory in your file explorer.",
                new Color(0.3f, 0.3f, 0.3f)))
            {
                EditorUtility.RevealInFinder(s.serverPatchDir);
            }
            EditorGUI.EndDisabledGroup();
        }

        private void RefreshPatches(DevToolsContext ctx)
        {
            _existingPatches.Clear();
            string dir = ctx.PatchDir;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            _existingPatches = Directory.GetFiles(dir, "*.sql").OrderBy(f => f).ToList();
        }

        private void WritePatch(DevToolsContext ctx, string patchDir)
        {
            try
            {
                string fileName = _patchName.EndsWith(".sql") ? _patchName : $"{_patchName}.sql";

                Directory.CreateDirectory(patchDir);
                string fullPath = Path.Combine(patchDir, fileName);

                if (File.Exists(fullPath) && !EditorUtility.DisplayDialog("Overwrite Patch?",
                        $"{fileName} already exists. Overwrite it?", "Overwrite", "Cancel"))
                    return;

                var sb = new StringBuilder();
                sb.AppendLine("-- Written by ArcheCore Dev Tools");
                sb.AppendLine($"-- Date: {DateTime.Now:yyyy-MM-dd HH:mm}");
                sb.AppendLine();
                sb.AppendLine(_patchSql);

                File.WriteAllText(fullPath, sb.ToString());
                ctx.Success($"Patch written: {fileName}", Src);

                _patchName = "";
                _patchSql  = "";
                RefreshPatches(ctx);
                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                ctx.Error($"Write failed: {ex.Message}", Src);
            }
        }
    }
}