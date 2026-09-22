using System.Collections.Generic;
using UnityEngine;

namespace ArcheCore.Client.GameData
{
    /// <summary>
    /// Item id -> icon sprite, data-driven by the item's icon_name.
    ///
    /// Drop a PNG into any Resources/Icons/ folder, named exactly like the
    /// item's icon_name on the server (e.g. Resources/Icons/ore_iron.png),
    /// and every slot, shop row and tooltip picks it up. No prefab or code
    /// change per item.
    ///
    /// Works whatever the PNG's import type is: a "Sprite (2D and UI)"
    /// import is used directly, and a plain "Default" texture is wrapped in
    /// a sprite at runtime - so a freshly dropped-in PNG works without
    /// touching its import settings.
    ///
    /// Returns null for no icon; callers show their text fallback instead.
    /// Results (including misses) are cached, so a missing icon costs one
    /// lookup per session, not one per frame.
    /// </summary>
    public static class ItemIcons
    {
        private const string Folder = "Icons/";

        private static readonly Dictionary<string, Sprite> Cache = new();

        public static Sprite Get(int itemTemplateId)
        {
            if (itemTemplateId <= 0 ||
                !GameDataDatabase.IsReady ||
                GameDataDatabase.Items == null ||
                !GameDataDatabase.Items.TryGetValue(itemTemplateId, out var record))
                return null;

            return GetByName(record.IconName);
        }

        public static Sprite GetByName(string iconName)
        {
            if (string.IsNullOrWhiteSpace(iconName))
                return null;

            if (Cache.TryGetValue(iconName, out var cached))
                return cached;

            var sprite = Resources.Load<Sprite>(Folder + iconName);

            if (sprite == null)
            {
                var tex = Resources.Load<Texture2D>(Folder + iconName);
                if (tex != null)
                {
                    sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    sprite.name = iconName;
                }
            }

            if (sprite == null)
                Debug.Log($"[ItemIcons] No icon at Resources/{Folder}{iconName} - using the text fallback.");

            Cache[iconName] = sprite;
            return sprite;
        }
    }
}
