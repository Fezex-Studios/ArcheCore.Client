// TileSceneIndex.cs
// Assets/Editor/DevTools/
//
// Where the tile scenes are. Shared by the World Tiles and Zones tabs so
// both agree - including once tiles have been sorted into zone
// subfolders (Tiles/solzreed/tile_-1_-2.unity). The streamer loads tiles
// by NAME, so which folder a tile sits in is purely for people; this
// class is what lets the tools stop caring too.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ArcheCore.Movement.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace ArcheCore.Editor.DevTools
{
    public static class TileSceneIndex
    {
        public const string FolderPref = "ArcheCore.WorldTiles.SceneFolder";
        public const string DefaultFolder = "Assets/ArcheCore.Client/World/Tiles";

        /// <summary>Root folder of all tile scenes (searched recursively).</summary>
        public static string Folder
        {
            get => EditorPrefs.GetString(FolderPref, DefaultFolder);
            set => EditorPrefs.SetString(FolderPref, value);
        }

        /// <summary>Every tile scene under the folder, any depth, by tile.</summary>
        public static Dictionary<TileCoord, string> FindAll(string folder = null)
        {
            folder ??= Folder;
            var result = new Dictionary<TileCoord, string>();

            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                return result;

            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (WorldGrid.TryParseSceneName(path, out var tile) && !result.ContainsKey(tile))
                    result[tile] = path;
            }

            return result;
        }

        /// <summary>
        /// The tile's existing scene wherever it lives under the folder, or
        /// where a new one should go (the folder root) if it doesn't exist.
        /// </summary>
        public static string PathFor(TileCoord tile, string folder = null)
        {
            folder ??= Folder;
            var all = FindAll(folder);
            return all.TryGetValue(tile, out var existing)
                ? existing
                : $"{folder.TrimEnd('/')}/{tile.SceneName}.unity";
        }

        /// <summary>Opens a scene additively unless it's already open. Returns it.</summary>
        public static Scene OpenAdditive(string path, out bool openedHere)
        {
            openedHere = false;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.path == path) return s;
            }

            openedHere = true;
            return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }

        /// <summary>Currently open scenes that are tile scenes.</summary>
        public static List<Scene> OpenTileScenes()
        {
            var list = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && WorldGrid.TryParseSceneName(s.name, out _)) list.Add(s);
            }
            return list;
        }

        /// <summary>
        /// Rewrites Build Settings entries after tile scenes were moved, so
        /// the list never points at a path that no longer exists.
        /// </summary>
        public static void RemapBuildSettings(IDictionary<string, string> oldToNew)
        {
            if (oldToNew.Count == 0) return;

            EditorBuildSettings.scenes = EditorBuildSettings.scenes
                .Select(s => oldToNew.TryGetValue(s.path, out var moved)
                    ? new EditorBuildSettingsScene(moved, s.enabled)
                    : s)
                .ToArray();
        }

        /// <summary>
        /// Ground height at a world point from whichever loaded terrain covers
        /// it, or <paramref name="fallback"/> where no loaded terrain does.
        /// Scene overlays use this so they lie ON the land instead of floating
        /// at the camera's pivot height.
        /// </summary>
        public static float GroundHeight(UnityEngine.Terrain[] terrains, float x, float z, float fallback = 0f)
        {
            foreach (var t in terrains)
            {
                if (t == null || t.terrainData == null) continue;
                var p = t.transform.position;
                var size = t.terrainData.size;
                if (x < p.x || x > p.x + size.x || z < p.z || z > p.z + size.z) continue;
                return t.SampleHeight(new UnityEngine.Vector3(x, 0f, z)) + p.y;
            }
            return fallback;
        }

        /// <summary>Creates an Assets/... folder path one level at a time. False if it isn't under Assets.</summary>
        public static bool EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (!folder.StartsWith("Assets", StringComparison.Ordinal)) return false;
            if (AssetDatabase.IsValidFolder(folder)) return true;

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) || !EnsureFolder(parent)) return false;

            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            return true;
        }
    }
}
