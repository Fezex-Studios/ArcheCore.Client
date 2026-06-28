using SQLite;

namespace ArcheCore.Client.GameData
{
    [Table("items")]
    public class ItemRecord
    {
        [PrimaryKey, Column("item_id")]
        public int ItemId { get; set; }

        [Column("name")]
        public string Name { get; set; }

        [Column("description")]
        public string Description { get; set; }

        [Column("category")]
        public int Category { get; set; }

        [Column("icon_name")]
        public string IconName { get; set; }
    }
}