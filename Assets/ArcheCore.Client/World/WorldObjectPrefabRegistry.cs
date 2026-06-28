using System.Collections.Generic;
using UnityEngine;

namespace ArcheCore.Client.World
{
    public class WorldObjectPrefabRegistry : MonoBehaviour
    {
        public static WorldObjectPrefabRegistry Instance { get; private set; }

        // Assign these in the Inspector on a GameObject in main_world scene
        [SerializeField] private List<WorldObjectPrefabEntry> entries;

        private readonly Dictionary<string, GameObject> _map = new();

        private void Awake()
        {
            Instance = this;
            foreach (var entry in entries)
                _map[entry.Type] = entry.Prefab;
        }

        public GameObject GetPrefab(string type)
        {
            if (_map.TryGetValue(type, out var prefab))
                return prefab;

            Debug.LogWarning($"[WorldObjectPrefabRegistry] No prefab for type '{type}'");
            return null;
        }
    }

    [System.Serializable]
    public class WorldObjectPrefabEntry
    {
        public string     Type;    // must match server Type exactly e.g. "Cube"
        public GameObject Prefab;
    }
}