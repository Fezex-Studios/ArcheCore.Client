using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ArcheCore.Client.GameData
{
    /// <summary>
    /// gamedata.bin layout (before encryption):
    ///
    ///   "AGDB"  version:byte  count:int32
    ///   per item, v1: id:int32 name:string description:string category:int32 icon:string
    ///   per item, v2 adds: categoryName:string rarityId:int32 rarityName:string
    ///                      rarityColor:string requiredLevel:int32 isUsable:bool consumeOnUse:bool
    ///
    /// Strings are BinaryWriter-style (7-bit length prefix + UTF-8). The
    /// writer is the server-side gamedata-export tool; this class must stay
    /// in step with it. Both versions are readable, so a client can always
    /// load an older file.
    /// </summary>
    public static class GameDataBinaryFormat
    {
        public const string Magic = "AGDB";
        public const byte CurrentVersion = 2;
        private const byte OldestReadableVersion = 1;

        public static void WriteItems(BinaryWriter w, IReadOnlyList<ItemRecord> items)
        {
            w.Write(Encoding.ASCII.GetBytes(Magic));
            w.Write(CurrentVersion);

            w.Write(items.Count);
            foreach (var item in items)
            {
                w.Write(item.ItemId);
                w.Write(item.Name ?? string.Empty);
                w.Write(item.Description ?? string.Empty);
                w.Write(item.Category);
                w.Write(item.IconName ?? string.Empty);

                w.Write(item.CategoryName ?? string.Empty);
                w.Write(item.RarityId);
                w.Write(item.RarityName ?? string.Empty);
                w.Write(item.RarityColor ?? string.Empty);
                w.Write(item.RequiredLevel);
                w.Write(item.IsUsable);
                w.Write(item.ConsumeOnUse);
            }
        }

        public static Dictionary<int, ItemRecord> ReadItems(BinaryReader r)
        {
            byte[] magicBytes = r.ReadBytes(Magic.Length);
            string magic      = Encoding.ASCII.GetString(magicBytes);

            if (magic != Magic)
            {
                throw new InvalidDataException(
                    $"Not a valid ArcheCore gamedata file (bad magic '{magic}').");
            }

            byte version = r.ReadByte();
            if (version < OldestReadableVersion || version > CurrentVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported gamedata format version {version} " +
                    $"(this build reads {OldestReadableVersion}-{CurrentVersion}). " +
                    (version > CurrentVersion ? "The client is older than the data - update the client." : ""));
            }

            int count  = r.ReadInt32();
            var result = new Dictionary<int, ItemRecord>(count);

            for (int i = 0; i < count; i++)
            {
                var item = new ItemRecord
                {
                    ItemId      = r.ReadInt32(),
                    Name        = r.ReadString(),
                    Description = r.ReadString(),
                    Category    = r.ReadInt32(),
                    IconName    = r.ReadString()
                };

                if (version >= 2)
                {
                    item.CategoryName  = r.ReadString();
                    item.RarityId      = r.ReadInt32();
                    item.RarityName    = r.ReadString();
                    item.RarityColor   = r.ReadString();
                    item.RequiredLevel = r.ReadInt32();
                    item.IsUsable      = r.ReadBoolean();
                    item.ConsumeOnUse  = r.ReadBoolean();
                }

                result[item.ItemId] = item;
            }

            return result;
        }
    }
}
