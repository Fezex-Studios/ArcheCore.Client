// DevToolsWindow.cs
// Assets/Editor/DevTools/
// Open via: ArcheCore → Dev Tools  (Shift+Alt+D)
//
// The shell. Owns the window chrome (header, tab bar, shared log) and
// nothing else — every actual feature lives in an IDevToolsTab.
//
// TABS ARE DISCOVERED BY REFLECTION, NOT REGISTERED IN A LIST.
//
// A hardcoded `new[] { new GameDataTab(), new DatabaseTab(), ... }` is one
// more place to forget when adding a feature, and the failure mode is
// silent — you write the tab, it compiles, and it just never appears.
// Scanning for implementors means dropping in a file is the whole job.
// The cost is one assembly scan at window open, which is imperceptible
// next to the domain reload that just happened anyway.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ArcheCore.Editor.DevTools
{
    public class DevToolsWindow : EditorWindow
    {
        private readonly List<LogEntry> _log = new();
        private readonly DevToolsStyles _styles = new();

        private List<IDevToolsTab> _tabs = new();
        private int _activeIndex;
        private DevToolsContext _ctx;

        private Vector2 _logScroll;
        private bool _logExpanded = true;
        private LogLevel _minLogLevel = LogLevel.Info;

        [MenuItem("ArcheCore/Dev Tools #&d")]
        public static void Open()
        {
            var w = GetWindow<DevToolsWindow>("ArcheCore Dev Tools");
            w.minSize = new Vector2(780, 680);
        }

        private void OnEnable()
        {
            // NOTE: no _styles.Build() here. EditorStyles.* is only valid
            // inside OnGUI — accessed during OnEnable it returns null and
            // throws, which would abort this method before DiscoverTabs()
            // runs and leave the window permanently showing "No tabs found".
            // OnGUI builds the styles on its first call instead.
            _ctx = new DevToolsContext(_log, this, _styles);

            DiscoverTabs();

            foreach (var tab in _tabs)
            {
                try { tab.OnEnable(_ctx); }
                catch (Exception e)
                {
                    // One broken tab must not stop the window from opening —
                    // otherwise a typo in a new tab locks you out of all the
                    // working ones, including the ones you'd use to fix it.
                    Debug.LogError($"[DevTools] Tab '{tab.Title}' failed OnEnable: {e}");
                }
            }

            SceneView.duringSceneGui += HandleSceneGUI;
            RestoreActiveTab();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= HandleSceneGUI;

            foreach (var tab in _tabs)
            {
                try { tab.OnDisable(_ctx); }
                catch (Exception e) { Debug.LogError($"[DevTools] Tab '{tab.Title}' failed OnDisable: {e}"); }
            }

            if (_tabs.Count > 0 && _activeIndex < _tabs.Count)
                EditorPrefs.SetString(ActiveTabPrefKey, _tabs[_activeIndex].Title);
        }

        private const string ActiveTabPrefKey = "ArcheCore.DevTools.ActiveTab";

        private void RestoreActiveTab()
        {
            string last = EditorPrefs.GetString(ActiveTabPrefKey, "");
            int idx = _tabs.FindIndex(t => t.Title == last);
            _activeIndex = idx >= 0 ? idx : 0;
        }

        private void DiscoverTabs()
        {
            _tabs = TypeCache.GetTypesDerivedFrom<IDevToolsTab>()
                .Where(t => !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t =>
                {
                    try { return (IDevToolsTab)Activator.CreateInstance(t); }
                    catch (Exception e)
                    {
                        Debug.LogError($"[DevTools] Couldn't construct tab {t.Name}: {e.Message}");
                        return null;
                    }
                })
                .Where(t => t != null)
                .OrderBy(t => t.Order)
                .ThenBy(t => t.Title)
                .ToList();
        }

        // ─────────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            _styles.Build();

            if (_tabs.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No tabs found. Every tab implements IDevToolsTab and needs a parameterless " +
                    "constructor — check the console for construction errors.", MessageType.Error);
                return;
            }

            DrawHeader();
            DrawTabBar();

            EditorGUILayout.Space(4);

            var active = _tabs[Mathf.Clamp(_activeIndex, 0, _tabs.Count - 1)];
            try
            {
                active.OnGUI(_ctx);
            }
            catch (ExitGUIException)
            {
                throw; // Unity's own control flow for things like file dialogs — must propagate.
            }
            catch (Exception e)
            {
                // Contained per-tab so an exception mid-layout doesn't leave
                // the whole window in the broken "Invalid GUILayout state"
                // condition where nothing renders until you reopen it.
                EditorGUILayout.HelpBox($"Tab '{active.Title}' threw:\n{e.Message}", MessageType.Error);
                if (GUILayout.Button("Copy full exception to clipboard"))
                    EditorGUIUtility.systemCopyBuffer = e.ToString();
            }

            EditorGUILayout.Space(4);
            DrawLog();
        }

        private void DrawHeader()
        {
            var rect = EditorGUILayout.GetControlRect(false, 36);
            EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.16f));

            EditorGUI.LabelField(new Rect(rect.x + 12, rect.y + 6, rect.width - 200, rect.height),
                "⚔  ArcheCore Dev Tools", _styles.Header);

            // Global DB status in the chrome: which database you're pointed at
            // is relevant on most tabs, and burying it inside one tab means
            // you can be three tabs deep editing the wrong file.
            string dbLabel = _ctx.HasWorldDb
                ? $"world: {System.IO.Path.GetFileName(_ctx.WorldDbPath)}"
                : "world db: not set";
            GUI.color = _ctx.HasWorldDb ? new Color(0.45f, 0.8f, 0.45f) : new Color(0.9f, 0.5f, 0.3f);
            EditorGUI.LabelField(new Rect(rect.xMax - 210, rect.y + 11, 140, rect.height),
                dbLabel, EditorStyles.miniLabel);
            GUI.color = new Color(0.5f, 0.5f, 0.5f);
            EditorGUI.LabelField(new Rect(rect.xMax - 62, rect.y + 11, 56, rect.height),
                "v2.0.0", EditorStyles.miniLabel);
            GUI.color = Color.white;
        }

        private void DrawTabBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            for (int i = 0; i < _tabs.Count; i++)
            {
                var tab = _tabs[i];
                bool active = i == _activeIndex;

                // Dot marks unsaved work so switching away from a tab with
                // pending edits is a visible choice rather than a silent one.
                string label = tab.HasUnsavedChanges ? tab.Title + "  •" : tab.Title;

                if (GUILayout.Button(label, active ? _styles.TabActive : _styles.TabInactive,
                        GUILayout.Width(150)))
                {
                    _activeIndex = i;
                    GUI.FocusControl(null); // stop a focused text field carrying across tabs
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void HandleSceneGUI(SceneView sv)
        {
            if (_tabs.Count == 0) return;
            var active = _tabs[Mathf.Clamp(_activeIndex, 0, _tabs.Count - 1)];

            try { active.OnSceneGUI(_ctx, sv); }
            catch (Exception e) { Debug.LogError($"[DevTools] {active.Title} OnSceneGUI: {e.Message}"); }
        }

        // ─────────────────────────────────────────────────────────────────
        private void DrawLog()
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();
            _logExpanded = EditorGUILayout.Foldout(_logExpanded, $"📋  Log ({_log.Count})", true);
            GUILayout.FlexibleSpace();

            _minLogLevel = (LogLevel)EditorGUILayout.EnumPopup(_minLogLevel, GUILayout.Width(80));

            if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(44)))
            {
                EditorGUIUtility.systemCopyBuffer = string.Join("\n",
                    _log.Select(e => $"[{e.Timestamp}] {e.Level} {e.Source}: {e.Message}"));
            }
            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(44)))
                _log.Clear();
            EditorGUILayout.EndHorizontal();

            if (!_logExpanded) { EditorGUILayout.EndVertical(); return; }

            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(110));

            foreach (var entry in _log)
            {
                if (entry.Level < _minLogLevel) continue;

                var style = entry.Level switch
                {
                    LogLevel.Success => _styles.Ok,
                    LogLevel.Warning => _styles.Warn,
                    LogLevel.Error   => _styles.Err,
                    _                => _styles.Mini
                };

                string src = string.IsNullOrEmpty(entry.Source) ? "" : $"<b>{entry.Source}</b>  ";
                EditorGUILayout.LabelField($"[{entry.Timestamp}]  {src}{entry.Message}", style);
            }

            if (_log.Count > 0) _logScroll.y = float.MaxValue;

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }
    }
}