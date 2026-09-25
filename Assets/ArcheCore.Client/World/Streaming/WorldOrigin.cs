using System;
using UnityEngine;

namespace ArcheCore.Client.World
{
    /// <summary>
    /// The ONE conversion between the two coordinate spaces the client has.
    ///
    ///   WORLD space - what the server, the database, every packet, every
    ///   spawn row and every heightmap use. A position in the shard, valid
    ///   anywhere, forever.
    ///
    ///   LOCAL space - what Unity's transforms hold. World space minus
    ///   Offset. Identical to world space until the floating origin first
    ///   shifts, which is why none of this was visible before.
    ///
    /// WHY TWO SPACES
    ///
    /// A float has 24 bits of mantissa. 8km from the origin, the smallest
    /// step a position can take is about 1mm; at 16km it's 2mm, at 32km
    /// 4mm. That doesn't sound like much until a skinned character, a
    /// shadow cascade and a camera all round differently every frame - the
    /// result is visible jitter on everything near the player, which no
    /// amount of smoothing removes. An ArcheAge-sized continent is tens of
    /// kilometres across. So the client keeps the player near Unity's
    /// origin by moving the WORLD instead (FloatingOrigin), and everything
    /// that crosses the network boundary converts here.
    ///
    /// THE RULE
    ///
    /// Every position that ARRIVES from the network goes through ToLocal
    /// before it touches a transform. Every position that LEAVES for the
    /// network comes from ToWorld. Nothing else in the client needs to
    /// know - distances between two transforms, raycasts, the camera, UI
    /// projection all work in local space unchanged, because a shift moves
    /// all of them together.
    ///
    /// CONVERT AT THE LAST MOMENT. A handler that queues work with
    /// WorldLoader.RunWhenReady must convert INSIDE the queued lambda, not
    /// when the packet arrives: a shift can happen in between, and a
    /// position converted early would be wrong by exactly the shift.
    ///
    /// Only X and Z ever shift. Y is height above sea level and never gets
    /// large enough to matter, and keeping it untouched means jump arcs,
    /// water levels and anything else reasoning about "up" never has to
    /// think about any of this.
    /// </summary>
    public static class WorldOrigin
    {
        /// <summary>
        /// World-space position of Unity's (0, 0, 0). Always a multiple of
        /// WorldGrid.TileSize on X and Z, so every shift is exact in float.
        /// </summary>
        public static Vector3 Offset { get; private set; }

        /// <summary>
        /// Raised after the world has been shifted, with the delta that was
        /// ADDED to every local position (i.e. minus the change in Offset).
        /// Anything caching a local position outside a transform - an
        /// interpolation buffer, a simulation state, a "last position" for a
        /// velocity estimate - must add this delta to its cache.
        /// </summary>
        public static event Action<Vector3> Shifted;

        public static bool IsShifted => Offset != Vector3.zero;

        /// <summary>World (network/server) position to Unity transform position.</summary>
        public static Vector3 ToLocal(Vector3 world) =>
            new Vector3(world.x - Offset.x, world.y, world.z - Offset.z);

        public static Vector3 ToLocal(float x, float y, float z) =>
            new Vector3(x - Offset.x, y, z - Offset.z);

        /// <summary>Unity transform position to world (network/server) position.</summary>
        public static Vector3 ToWorld(Vector3 local) =>
            new Vector3(local.x + Offset.x, local.y, local.z + Offset.z);

        /// <summary>
        /// Moves the origin. Only FloatingOrigin calls this, right after it
        /// has moved every root object by the returned delta - the offset
        /// and the transforms must change together, or every conversion is
        /// wrong until they agree again.
        /// </summary>
        internal static Vector3 MoveOrigin(Vector3 newOffset)
        {
            newOffset.y = 0f;
            Vector3 localDelta = Offset - newOffset;
            Offset = newOffset;
            return localDelta;
        }

        internal static void RaiseShifted(Vector3 localDelta)
        {
            var handlers = Shifted;
            if (handlers == null) return;

            // One bad subscriber must not stop the rest from rebasing -
            // a single cache left unshifted is a character teleporting by
            // the shift distance, which is far worse than a logged error.
            foreach (Action<Vector3> handler in handlers.GetInvocationList())
            {
                try { handler(localDelta); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>
        /// Back to no offset. Called when a connection starts or ends - the
        /// scene that held shifted objects is gone by then, so there is
        /// nothing to move, only the number to forget.
        /// </summary>
        public static void Reset() => Offset = Vector3.zero;
    }
}
