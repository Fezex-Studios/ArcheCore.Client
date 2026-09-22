namespace ArcheCore.Client.GameData
{
    /// <summary>
    /// A single row of client-facing item data, loaded from gamedata.bin
    /// (see GameDataBinaryFormat). Generated from the SERVER's item tables by
    /// the gamedata-export tool - never edit it by hand.
    ///
    /// Fields below the line are format v2 (tooltip data). A v1 file leaves
    /// them at their defaults, so an old gamedata.bin still loads - it just
    /// shows less in the tooltip.
    /// </summary>
    public class ItemRecord
    {
        public int ItemId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Category { get; set; }
        public string IconName { get; set; }

        // ── v2 ──────────────────────────────────────────────
        public string CategoryName { get; set; } = string.Empty;
        public int RarityId { get; set; }
        public string RarityName { get; set; } = string.Empty;

        /// <summary>"#RRGGBB" from ItemRarities.ColorHex. Empty = no rarity colour.</summary>
        public string RarityColor { get; set; } = string.Empty;

        /// <summary>0 = no level requirement.</summary>
        public int RequiredLevel { get; set; }

        /// <summary>Has an ItemUses row on the server - right-click does something.</summary>
        public bool IsUsable { get; set; }
        public bool ConsumeOnUse { get; set; }
    }
}
