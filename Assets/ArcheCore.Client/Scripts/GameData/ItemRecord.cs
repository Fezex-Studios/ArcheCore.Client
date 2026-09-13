namespace ArcheCore.Client.GameData
{
    /// <summary>
    /// A single row of client-facing item data. Plain data class — no
    /// SQLite attributes needed now that gamedata is loaded from the
    /// custom binary format (see GameDataBinaryFormat.cs) straight into
    /// memory rather than queried from a SQLite connection.
    /// </summary>
    public class ItemRecord
    {
        public int ItemId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Category { get; set; }
        public string IconName { get; set; }
    }
}