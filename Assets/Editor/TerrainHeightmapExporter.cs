using System.IO;
using ArcheCore.Movement.Terrain;
using UnityEditor;
using UnityEngine;

namespace ArcheCore.Client.Editor
{
    /// <summary>
    /// Exports a Unity Terrain into the .achtmap format HeightmapData reads,
    /// for the world server to load with HeightmapCollisionWorld.
    ///
    /// THIS IS THE STEP THAT MAKES "SAME GROUND, BOTH SIDES" TRUE RATHER
    /// THAN ASPIRATIONAL. Hand-authoring a server-side heightmap separately
    /// from the terrain artists actually sculpt guarantees the two drift -
    /// see HeightmapData's remarks on why consistency matters more than
    /// fidelity. Re-run this export as the LAST step of any terrain
    /// sculpting pass, and treat a stale export as a bug, not a formality -
    /// it silently reintroduces exactly the "server thinks you're
    /// underground" problem this whole system exists to remove.
    ///
    /// RESOLUTION. Exports at the terrain's own heightmap resolution
    /// (terrainData.heightmapResolution) rather than downsampling. A world
    /// server loads this once at zone startup, not per-tick, so there is no
    /// runtime cost to keeping full fidelity, and downsampling is exactly
    /// the kind of "close enough" that produces the disagreement this
    /// format exists to avoid.
    /// </summary>
    public static class TerrainHeightmapExporter
    {
        [MenuItem("ArcheCore/Terrain/Export Heightmap For Server...")]
        public static void ExportSelectedTerrain()
        {
            Terrain terrain = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<Terrain>()
                : null;

            if (terrain == null)
                terrain = Terrain.activeTerrain;

            if (terrain == null)
            {
                EditorUtility.DisplayDialog(
                    "Export Heightmap",
                    "Select a GameObject with a Terrain component (or have an active Terrain in the scene) first.",
                    "OK");
                return;
            }

            string defaultName = terrain.name + ".achtmap";
            string path = EditorUtility.SaveFilePanel(
                "Export Heightmap For Server", "", defaultName, "achtmap");

            if (string.IsNullOrEmpty(path))
                return;

            Export(terrain, path);

            EditorUtility.DisplayDialog(
                "Export Heightmap",
                $"Exported '{terrain.name}' to:\n{path}\n\n" +
                "Copy this file to the world server's terrain data folder " +
                "and point WorldServerConfig at it for the zone this " +
                "terrain belongs to.",
                "OK");
        }

        public static void Export(Terrain terrain, string outputPath)
        {
            TerrainData data = terrain.terrainData;
            int resolution = data.heightmapResolution;

            // GetHeights returns [0,1] normalized samples indexed [y, x]
            // (row-major by Unity's own convention, y is the Z axis here -
            // terrain heightmaps are historically 2D-image-shaped). Convert
            // to world-space Y and HeightmapData's [z * resX + x] layout in
            // the same pass, so nothing downstream has to remember either
            // convention.
            float[,] raw = data.GetHeights(0, 0, resolution, resolution);
            var heights = new float[resolution * resolution];

            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    heights[z * resolution + x] = raw[z, x] * data.size.y;
                }
            }

            Vector3 origin = terrain.transform.position;

            var heightmap = new HeightmapData(
                originX: origin.x,
                originZ: origin.z,
                sizeX: data.size.x,
                sizeZ: data.size.z,
                resolutionX: resolution,
                resolutionZ: resolution,
                heights: heights);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            heightmap.Save(outputPath);

            Debug.Log($"[TerrainHeightmapExporter] Wrote {outputPath} " +
                      $"({resolution}x{resolution}, origin ({origin.x:F1}, {origin.z:F1}), " +
                      $"size ({data.size.x:F1} x {data.size.z:F1}))");
        }
    }
}
