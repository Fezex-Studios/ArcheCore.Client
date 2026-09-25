using ArcheCore.Movement.World;
using ArcheCore.Network.Shared.Packets.W2C;
using UnityEngine;

namespace ArcheCore.Client.World
{
    /// <summary>
    /// The shard's world layout as the SERVER described it on entering the
    /// world (W2CEnterWorldPacket.World). Replaces the hardcoded copies of
    /// server numbers the client used to carry - NetworkCells' interest
    /// radii had a "KEEP THESE IN SYNC" comment, which is another way of
    /// saying "will eventually be wrong".
    ///
    /// Defaults are what the server used before it started sending these,
    /// so an older server still produces sensible values.
    /// </summary>
    public static class WorldSettings
    {
        public static string ShardName { get; private set; } = "";
        public static float InterestSpawnRadius { get; private set; } = 75f;
        public static float InterestDespawnRadius { get; private set; } = 85f;

        /// <summary>
        /// Hash of the server's zone map, "" if it has none. ZoneTracker
        /// compares it with the copy this client shipped.
        /// </summary>
        public static string ServerZoneMapHash { get; private set; } = "";

        /// <summary>True once a server has told us its layout on this connection.</summary>
        public static bool ReceivedFromServer { get; private set; }

        /// <summary>
        /// False if the server cuts the world into different tiles than this
        /// client build does. WorldStreamer refuses to stream in that case -
        /// it would load the wrong scenery for where the player is - and says
        /// so loudly, because the fix is rebuilding ArcheCore.Movement.dll
        /// into the client, not anything a player can do.
        /// </summary>
        public static bool TileSizeMatches { get; private set; } = true;

        public static void Apply(WorldSettingsData data)
        {
            if (data == null)
                return; // older server - keep defaults

            ShardName = data.ShardName ?? "";
            ServerZoneMapHash = data.ZoneMapHash ?? "";

            if (data.InterestSpawnRadius > 0f) InterestSpawnRadius = data.InterestSpawnRadius;
            if (data.InterestDespawnRadius > 0f) InterestDespawnRadius = data.InterestDespawnRadius;

            TileSizeMatches = data.TileSize <= 0f || Mathf.Approximately(data.TileSize, WorldGrid.TileSize);

            if (!TileSizeMatches)
            {
                Debug.LogError(
                    $"[WorldSettings] Shard '{ShardName}' uses {data.TileSize}m world tiles but this client was " +
                    $"built with {WorldGrid.TileSize}m. World streaming is DISABLED. Rebuild ArcheCore.Movement " +
                    "and copy the DLL into Assets/Plugins so client and server share the same WorldGrid.");
            }

            ReceivedFromServer = true;

            Debug.Log($"[WorldSettings] Entered shard '{ShardName}' - tiles {WorldGrid.TileSize}m, " +
                      $"interest {InterestSpawnRadius}/{InterestDespawnRadius}m.");
        }

        public static void Reset()
        {
            ShardName = "";
            ServerZoneMapHash = "";
            InterestSpawnRadius = 75f;
            InterestDespawnRadius = 85f;
            TileSizeMatches = true;
            ReceivedFromServer = false;
        }
    }
}
