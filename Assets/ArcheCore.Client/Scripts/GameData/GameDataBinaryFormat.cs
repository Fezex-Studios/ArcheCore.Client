using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ArcheCore.Client.GameData
{
    /// <summary>
    /// Shared binary layout for the client's gamedata content package.
    ///
    /// Both the write side (Editor/ArcheCoreDevTools.cs, "Export Binary" step
    /// on the Game Data tab) and the read side (GameDataDatabase.cs at
    /// runtime) call into this class, so the two can never drift out of
    /// sync — there is exactly one place that knows the byte layout.
    ///
    /// This is the format that gets AES-encrypted (see GameDataCrypto.cs)
    /// before it's written to StreamingAssets / served by the Authserver.
    /// This class itself has no idea encryption exists — it only ever reads
    /// or writes plaintext bytes.
    ///
    /// Layout (BinaryWriter/BinaryReader handle endianness identically on
    /// every platform Unity targets, so we don't need to think about it):
    ///
    ///   [4 bytes ]  magic         = "AGDB" (ASCII)
    ///   [1 byte  ]  format version
    ///   ── Items section ──
    ///   [int32   ]  item count
    ///   per item:
    ///     [int32 ]  item_id
    ///     [string]  name          .NET BinaryWriter/Reader string: a 7-bit
    ///     [string]  description   encoded length prefix followed by UTF8
    ///     [int32 ]  category      bytes. Handled automatically by
    ///     [string]  icon_name     w.Write(string) / r.ReadString().
    ///
    /// Adding a new table later (e.g. quests, dialogue):
    ///   1. Bump CurrentVersion.
    ///   2. Add a WriteXxx/ReadXxx pair below, appended after the items
    ///      section (own count + loop, same pattern as items).
    ///   3. In ReadXxx, gate on the version byte you already read, so a
    ///      file written by an older exporter (which won't have that
    ///      section) doesn't blow up trying to read bytes that aren't
    ///      there — just skip the section and return an empty result.
    /// </summary>
    public static class GameDataBinaryFormat
    {
        public const string Magic = "AGDB";
        public const byte CurrentVersion = 1;

        // ── Write (Editor only, but no UnityEditor dependency here so this
        //    class can also be compiled into player builds without pulling
        //    in the editor assembly) ──────────────────────────────────────
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
            }
        }

        // ── Read (runtime) ───────────────────────────────────────────────
        /// <summary>
        /// Parses a decrypted gamedata blob into an in-memory item lookup.
        /// Throws InvalidDataException on a bad magic number or an
        /// unsupported version — callers should treat that the same way
        /// GameDataDatabase previously treated "malformed or wrong key".
        /// </summary>
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
            if (version != CurrentVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported gamedata format version {version} " +
                    $"(this build expects {CurrentVersion}).");
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

                result[item.ItemId] = item;
            }

            return result;
        }
    }
}
