// DevToolsContext.cs
// Assets/Editor/DevTools/
//
// Everything a tab needs from the shell, passed in rather than reached for.
//
// Tabs get this as a parameter instead of calling
// ArcheCoreDevToolsSettings.instance directly or newing up their own
// GUIStyles. Two reasons: the shared log stays shared (a tab logging into
// its own private list means the user has to hunt for which panel the
// error went to), and style objects get built once for the whole window
// rather than once per tab per repaint.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ArcheCore.Editor.DevTools
{
    public enum LogLevel { Info, Success, Warning, Error }

    public sealed class LogEntry
    {
        public LogLevel Level;
        public string   Message;
        public string   Timestamp;
        public string   Source;   // which tab logged it — matters once several can

        public LogEntry(LogLevel level, string msg, string source)
        {
            Level     = level;
            Message   = msg;
            Source    = source;
            Timestamp = DateTime.Now.ToString("HH:mm:ss");
        }
    }

    public sealed class DevToolsContext
    {
        private readonly List<LogEntry> _log;
        private readonly EditorWindow   _window;

        public DevToolsContext(List<LogEntry> log, EditorWindow window, DevToolsStyles styles)
        {
            _log    = log;
            _window = window;
            Styles  = styles;
        }

        public DevToolsStyles Styles { get; }

        /// <summary>
        /// The one persisted settings object. Shared deliberately: the world
        /// DB path configured on one tab must be the same path another tab
        /// reads, or you end up editing one database while testing against
        /// another — a genuinely confusing class of bug.
        /// </summary>
        public ArcheCoreDevToolsSettings Settings => ArcheCoreDevToolsSettings.instance;

        public string WorldDbPath  => Settings.worldServerDbPath;
        public string ClientDbPath => Settings.plaintextDbPath;
        public string PatchDir     => Settings.serverPatchDir;

        public bool HasWorldDb => !string.IsNullOrEmpty(WorldDbPath) && File.Exists(WorldDbPath);

        public void Log(LogLevel level, string msg, string source = "")
        {
            _log.Add(new LogEntry(level, msg, source));

            // Cap so a chatty validation run over thousands of rows doesn't
            // grow unbounded across a long editor session.
            if (_log.Count > 500) _log.RemoveRange(0, _log.Count - 500);

            _window.Repaint();
        }

        public void Info(string m, string src = "")    => Log(LogLevel.Info, m, src);
        public void Success(string m, string src = "") => Log(LogLevel.Success, m, src);
        public void Warn(string m, string src = "")    => Log(LogLevel.Warning, m, src);
        public void Error(string m, string src = "")   => Log(LogLevel.Error, m, src);

        public void Repaint() => _window.Repaint();

        // ── Shared widgets ────────────────────────────────────────────────
        // Lifted out of the original window so every tab draws a path field
        // or a status line the same way. Copy-pasting these into each tab is
        // exactly how a tool ends up with four slightly different file
        // pickers that behave subtly differently.

        public static bool PathField(string label, ref string value, bool isDirectory,
                                     string extension = "db", int labelWidth = 190)
        {
            EditorGUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(label))
                EditorGUILayout.LabelField(label, GUILayout.Width(labelWidth));

            EditorGUI.BeginChangeCheck();
            value = EditorGUILayout.TextField(value);
            bool changed = EditorGUI.EndChangeCheck();

            if (GUILayout.Button("…", EditorStyles.miniButton, GUILayout.Width(24)))
            {
                string start = "";
                try
                {
                    if (!string.IsNullOrEmpty(value))
                        start = isDirectory
                            ? (Directory.Exists(value) ? value : "")
                            : (File.Exists(value) ? Path.GetDirectoryName(value) : "");
                }
                catch { start = ""; }

                string picked = isDirectory
                    ? EditorUtility.OpenFolderPanel("Select Folder", start, "")
                    : EditorUtility.OpenFilePanel("Select File", start, extension);

                if (!string.IsNullOrEmpty(picked)) { value = picked; changed = true; }
            }
            EditorGUILayout.EndHorizontal();
            return changed;
        }

        public void FileStatus(string label, string path, int labelWidth = 160)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel, GUILayout.Width(labelWidth));

            bool exists = !string.IsNullOrEmpty(path) && File.Exists(path);
            string text = exists
                ? $"✓  {Path.GetFileName(path)}  ({new FileInfo(path).Length / 1024:N0} KB)"
                : "✗  Not found";

            EditorGUILayout.LabelField(text, exists ? Styles.Ok : Styles.Err);
            EditorGUILayout.EndHorizontal();
        }

        public static bool ActionButton(string label, string tooltip, Color accent, float height = 30)
        {
            var rect = EditorGUILayout.GetControlRect(false, height);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3, rect.height), accent);
            return GUI.Button(new Rect(rect.x + 6, rect.y, rect.width - 6, rect.height),
                new GUIContent(label, tooltip));
        }

        public static void Section(string title, GUIStyle headerStyle, Action body)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(title, headerStyle);
            EditorGUILayout.Space(3);
            body();
            EditorGUILayout.EndVertical();
        }
    }

    /// <summary>
    /// Built once per window. GUIStyle allocation inside OnGUI is a classic
    /// editor-tool performance mistake — OnGUI runs on every repaint, many
    /// times a second while the mouse moves.
    /// </summary>
    public sealed class DevToolsStyles
    {
        public GUIStyle Header      { get; private set; }
        public GUIStyle SubHeader   { get; private set; }
        public GUIStyle Mini        { get; private set; }
        public GUIStyle Ok          { get; private set; }
        public GUIStyle Warn        { get; private set; }
        public GUIStyle Err         { get; private set; }
        public GUIStyle TabActive   { get; private set; }
        public GUIStyle TabInactive { get; private set; }
        public GUIStyle SectionBox  { get; private set; }

        public void Build()
        {
            if (Header != null) return;

            Header = new GUIStyle(EditorStyles.boldLabel)
            { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            Header.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

            SubHeader = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            SubHeader.normal.textColor = new Color(0.7f, 0.7f, 0.7f);

            Mini = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, richText = true, fontSize = 10 };

            var tabBase = new GUIStyle(EditorStyles.toolbarButton) { fontSize = 11, fixedHeight = 28 };
            TabActive   = new GUIStyle(tabBase);
            TabInactive = new GUIStyle(tabBase);
            TabActive.normal.textColor   = new Color(0.95f, 0.85f, 0.4f);
            TabInactive.normal.textColor = new Color(0.65f, 0.65f, 0.65f);

            SectionBox = new GUIStyle("box")
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin  = new RectOffset(0, 0, 4, 4)
            };

            Ok = new GUIStyle(EditorStyles.miniLabel);
            Ok.normal.textColor = new Color(0.4f, 0.9f, 0.4f);
            Warn = new GUIStyle(EditorStyles.miniLabel);
            Warn.normal.textColor = new Color(0.95f, 0.75f, 0.2f);
            Err = new GUIStyle(EditorStyles.miniLabel);
            Err.normal.textColor = new Color(0.95f, 0.3f, 0.3f);
        }
    }
}