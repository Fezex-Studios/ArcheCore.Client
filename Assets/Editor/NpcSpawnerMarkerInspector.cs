// MarkerInspectors.cs
// Assets/Editor/World/
//
// Custom inspectors for the two marker components. Without these, selecting
// a spawner in the scene shows a raw TemplateId int field — you'd have to
// remember that 7 means "Orc Grunt", and a typo silently produces a spawner
// the server skips at load with no visible error. These resolve the id
// against real NpcTemplates rows and surface sync state where you're
// already looking.
//
// Template list is cached per-inspector and refreshed on enable rather than
// queried every OnInspectorGUI — that method runs on every repaint, and
// hitting SQLite from it would put a file read in the editor's paint loop.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ArcheCore.Editor.World
{
    [CustomEditor(typeof(NpcSpawnerMarker))]
    [CanEditMultipleObjects]
    public class NpcSpawnerMarkerInspector : UnityEditor.Editor
    {
        private static List<NpcTemplateRow> _templates = new();
        private static string[] _labels = Array.Empty<string>();
        private static int[]    _ids    = Array.Empty<int>();
        private static double   _lastRefresh;

        private void OnEnable() => RefreshTemplatesIfStale();

        /// <summary>
        /// Cached across inspector instances and refreshed at most every 30s,
        /// because selecting ten markers in a row shouldn't mean ten database
        /// round trips. Manual refresh button below covers the case where you
        /// just added a template and want it immediately.
        /// </summary>
        private static void RefreshTemplatesIfStale(bool force = false)
        {
            if (!force && EditorApplication.timeSinceStartup - _lastRefresh < 30) return;

            string db = ArcheCoreDevToolsSettings.instance.worldServerDbPath;
            if (string.IsNullOrEmpty(db) || !File.Exists(db)) return;

            try
            {
                _templates = WorldDbAccess.LoadTemplates(db);
                _labels = _templates.Select(t => $"[{t.Id}] {t.Name}  (lv{t.Level})").ToArray();
                _ids    = _templates.Select(t => t.Id).ToArray();
                _lastRefresh = EditorApplication.timeSinceStartup;
            }
            catch
            {
                // Silent: a locked or missing DB shouldn't spam the console
                // every time you click a marker. The inspector falls back to a
                // plain int field and says why.
            }
        }

        public override void OnInspectorGUI()
        {
            var marker = (NpcSpawnerMarker)target;

            // ── Sync status ──
            EditorGUILayout.BeginVertical("box");
            if (!marker.IsInDatabase)
            {
                EditorGUILayout.HelpBox("Not in the database yet. Use World Map → Scene → DB to write it.",
                    MessageType.Info);
            }
            else if (marker.HasLocalChanges)
            {
                EditorGUILayout.HelpBox($"Row #{marker.DbId} — edited since load. Sync from the World Map window.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField($"✓ Row #{marker.DbId} — matches database", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            // ── Template ──
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("NPC Template", GUILayout.Width(100));

            if (_ids.Length == 0)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("TemplateId"), GUIContent.none);
                if (GUILayout.Button("↻", EditorStyles.miniButton, GUILayout.Width(24)))
                    RefreshTemplatesIfStale(force: true);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.HelpBox(
                    "Couldn't read NpcTemplates — set the World DB path in Dev Tools, or stop the " +
                    "server if it's holding the file lock. Falling back to a raw id field.",
                    MessageType.Warning);
            }
            else
            {
                int idx = Array.IndexOf(_ids, marker.TemplateId);
                bool unknown = idx < 0;
                if (unknown) idx = 0;

                int newIdx = EditorGUILayout.Popup(idx, _labels);
                if (GUILayout.Button("↻", EditorStyles.miniButton, GUILayout.Width(24)))
                    RefreshTemplatesIfStale(force: true);
                EditorGUILayout.EndHorizontal();

                if (unknown)
                    EditorGUILayout.HelpBox(
                        $"TemplateId {marker.TemplateId} has no row in NpcTemplates. The server will " +
                        "skip this spawner. Pick a valid template above.", MessageType.Error);

                if (newIdx != idx || unknown && newIdx == 0)
                {
                    Undo.RecordObject(marker, "Change NPC Template");
                    marker.TemplateId = _ids[newIdx];
                    marker.NpcName    = _templates[newIdx].Name;
                    EditorUtility.SetDirty(marker);
                }
            }

            // ── Editable fields ──
            EditorGUI.BeginChangeCheck();
            int count    = EditorGUILayout.IntField("Count", marker.Count);
            float radius = EditorGUILayout.FloatField("Radius", marker.Radius);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(marker, "Edit Spawner");
                marker.Count  = Mathf.Max(1, count);
                marker.Radius = Mathf.Max(0f, radius);
                EditorUtility.SetDirty(marker);
            }

            EditorGUILayout.Space(4);

            // ── Utilities ──
            if (GUILayout.Button("Snap to Ground"))
            {
                Undo.RecordObject(marker.transform, "Snap Spawner to Ground");
                if (Physics.Raycast(marker.transform.position + Vector3.up * 500f,
                                    Vector3.down, out var hit, 2000f))
                {
                    marker.transform.position = hit.point;
                    EditorUtility.SetDirty(marker.transform);
                }
                else
                {
                    Debug.LogWarning($"[{marker.name}] No collider found below — nothing to snap to.");
                }
            }

            if (marker.IsInDatabase && marker.HasLocalChanges)
            {
                EditorGUILayout.Space(2);
                if (GUILayout.Button("Revert to Database Values"))
                {
                    Undo.RecordObject(marker, "Revert Spawner");
                    Undo.RecordObject(marker.transform, "Revert Spawner");
                    marker.transform.position = marker.SyncedPosition;
                    marker.TemplateId = marker.SyncedTemplateId;
                    marker.Count      = marker.SyncedCount;
                    marker.Radius     = marker.SyncedRadius;
                    EditorUtility.SetDirty(marker);
                }
            }
        }
    }

    [CustomEditor(typeof(PlayerSpawnPointMarker))]
    public class PlayerSpawnPointMarkerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var marker = (PlayerSpawnPointMarker)target;

            EditorGUILayout.BeginVertical("box");
            if (!marker.IsInDatabase)
                EditorGUILayout.HelpBox("Not in the database yet.", MessageType.Info);
            else if (marker.HasLocalChanges)
                EditorGUILayout.HelpBox($"Row #{marker.DbId} — edited since load.", MessageType.Warning);
            else
                EditorGUILayout.LabelField($"✓ Row #{marker.DbId} — matches database", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.TextField("Spawn Name", marker.SpawnName);
            bool isDefault = EditorGUILayout.Toggle("Is Default", marker.IsDefault);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(marker, "Edit Spawn Point");
                marker.SpawnName = name;

                // Clearing the flag on every other marker here keeps the scene
                // honest about the DB's single-default rule, instead of letting
                // two look default until someone reads the table.
                if (isDefault && !marker.IsDefault)
                {
                    foreach (var other in FindObjectsByType<PlayerSpawnPointMarker>(FindObjectsSortMode.None))
                    {
                        if (other == marker || !other.IsDefault) continue;
                        Undo.RecordObject(other, "Clear Default Spawn");
                        other.IsDefault = false;
                        EditorUtility.SetDirty(other);
                    }
                }

                marker.IsDefault = isDefault;
                EditorUtility.SetDirty(marker);
            }

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Snap to Ground"))
            {
                Undo.RecordObject(marker.transform, "Snap Spawn Point to Ground");
                if (Physics.Raycast(marker.transform.position + Vector3.up * 500f,
                                    Vector3.down, out var hit, 2000f))
                {
                    marker.transform.position = hit.point;
                    EditorUtility.SetDirty(marker.transform);
                }
                else
                {
                    Debug.LogWarning($"[{marker.name}] No collider below — players would fall in from here.");
                }
            }
        }
    }
}