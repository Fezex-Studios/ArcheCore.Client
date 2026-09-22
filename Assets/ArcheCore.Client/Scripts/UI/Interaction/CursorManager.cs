using System.Collections.Generic;
using ArchCore.Client;
using ArcheCore.Client.Gameplay;
using ArcheCore.Client.World;
using UnityEngine;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// Changes the mouse cursor with what's under it. Creates itself.
    ///
    ///   an enemy you can attack        -> "attack" (sword)
    ///   anything with actions          -> its F action's CursorName (data:
    ///                                     "interact" cog, "loot" bag, "talk")
    ///   nothing                        -> the normal cursor
    ///
    /// Cursors are PNGs in Resources/Cursors/, named like the CursorName. Any
    /// import settings work: the PNG is copied into a readable RGBA texture at
    /// runtime, which is what the hardware cursor needs. Missing file = normal
    /// cursor, once-per-name warning.
    /// </summary>
    public class CursorManager : MonoBehaviour
    {
        [SerializeField] private string attackCursor = "attack";
        [SerializeField] private Vector2 hotspot = new Vector2(1f, 1f);

        private readonly Dictionary<string, Texture2D> _cache = new();
        private PlayerInteraction _pointer;
        private float _nextSearch;
        private string _current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<CursorManager>() != null)
                return;

            var go = new GameObject("CursorManager");
            DontDestroyOnLoad(go);
            go.AddComponent<CursorManager>();
        }

        private void Update()
        {
            if (_pointer == null && Time.unscaledTime >= _nextSearch)
            {
                _nextSearch = Time.unscaledTime + 1f;
                _pointer = FindFirstObjectByType<PlayerInteraction>();
            }

            Apply(Choose(_pointer != null ? _pointer.CurrentFocus : null));
        }

        private void OnDisable() => Apply(null);

        private string Choose(InteractableIdentity hovered)
        {
            if (hovered == null)
                return null;

            if (hovered.Kind == InteractableKind.Npc &&
                NpcRegistry.Instance != null &&
                NpcRegistry.Instance.TryGetNpc(hovered.NetworkId, out var npc) &&
                npc != null && npc.MaxHealth > 0 && npc.Health > 0)
                return attackCursor;

            if (hovered.HasActions && hovered.IsAvailable)
                return hovered.Actions[0].CursorName;

            return null;
        }

        private void Apply(string name)
        {
            if (name == _current)
                return;

            _current = name;
            var tex = string.IsNullOrEmpty(name) ? null : Load(name);
            Cursor.SetCursor(tex, tex != null ? hotspot : Vector2.zero, CursorMode.Auto);
        }

        private Texture2D Load(string name)
        {
            if (_cache.TryGetValue(name, out var cached))
                return cached;

            Texture2D readable = null;
            var source = Resources.Load<Texture2D>("Cursors/" + name);

            if (source == null)
            {
                Debug.LogWarning($"[CursorManager] No cursor at Resources/Cursors/{name} - using the normal cursor.");
            }
            else
            {
                // Copy through the GPU so import settings don't matter (the
                // hardware cursor needs an uncompressed, readable RGBA32).
                var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(source, rt);
                var previous = RenderTexture.active;
                RenderTexture.active = rt;

                readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                readable.Apply();

                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }

            _cache[name] = readable;
            return readable;
        }
    }
}
