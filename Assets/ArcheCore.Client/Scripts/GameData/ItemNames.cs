namespace ArcheCore.Client.GameData
{
    /// <summary>
    /// Item id -> display name, from the client's gamedata.bin.
    ///
    /// Never throws and never returns null: an id missing from gamedata (or
    /// gamedata not loaded yet) comes back as "#12". That fallback is the
    /// visible symptom of the client's gamedata being behind the server's -
    /// if you see a "#" name in game, rebuild gamedata.bin.
    ///
    /// Anything the SERVER already names for us (harvest results, shop
    /// rows) uses the server's name instead, so those are always right.
    /// </summary>
    public static class ItemNames
    {
        public static string Get(int itemTemplateId)
        {
            if (itemTemplateId <= 0)
                return string.Empty;

            // Checked directly rather than through ItemRepository, which
            // logs a warning every call while gamedata is loading - this is
            // called from every slot on every refresh.
            if (GameDataDatabase.IsReady &&
                GameDataDatabase.Items != null &&
                GameDataDatabase.Items.TryGetValue(itemTemplateId, out var record) &&
                !string.IsNullOrEmpty(record.Name))
            {
                return record.Name;
            }

            return $"#{itemTemplateId}";
        }
    }
}
