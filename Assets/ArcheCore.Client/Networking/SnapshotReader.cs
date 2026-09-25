using System;
using System.Buffers.Binary;
using ArcheCore.Network.Shared;
using UnityEngine;

namespace ArcheCore.Client.Networking
{
    /// <summary>
    /// Client side of W2CWorldSnapshot. Mirror of SnapshotWriter - if you
    /// change the layout on one side, change it here in the same commit.
    ///
    /// Snapshots are UNRELIABLE and may arrive out of order. The tick field
    /// exists so you can discard stale ones: keep the highest tick applied
    /// per entity and drop anything older. Applying an out-of-order
    /// snapshot is what produces the classic remote-player stutter.
    ///
    /// Entities not present in a snapshot have NOT despawned. They were
    /// either far enough away to be on a slower LOD tier, or past the
    /// per-packet cap this tick. Despawn only ever arrives on the reliable
    /// W2CPlayerLeave / W2CNpcDespawn channel. Treating absence as despawn
    /// will make distant players blink in and out constantly.
    ///
    /// ENTRIES ARE VARIABLE LENGTH. 13 bytes base, +3 if Velocity is set
    /// (near-tier entities only), +2 if Tilt is (non-upright entities
    /// only). The flags byte is the only thing that says which - so the
    /// advance of `offset` MUST be driven by the flags, not by a constant,
    /// and the optional blocks must be read in the same ORDER the writer
    /// wrote them (velocity, then tilt). Getting either wrong doesn't
    /// throw; it silently misreads every remaining entry in the packet as
    /// garbage positions, which looks like other players teleporting to the
    /// edge of the world.
    /// </summary>
    public static class SnapshotReader
    {
        private const float PositionScale = 64f;
        private const int HeaderSize = 20;

        [Flags]
        public enum EntryFlags : byte
        {
            None     = 0,
            Position = 1 << 0,
            Yaw      = 1 << 1,
            IsNpc    = 1 << 2,
            Velocity = 1 << 3,
            Tilt     = 1 << 4,
        }

        public readonly struct Entry
        {
            public readonly int     NetworkId;
            public readonly Vector3 Position;

            /// <summary>
            /// World units per second, or zero when the server didn't send
            /// it (mid/far LOD tiers). Zero is indistinguishable from
            /// "genuinely stationary" here, and that's fine: both mean
            /// "don't extrapolate this entity anywhere."
            /// </summary>
            public readonly Vector3 Velocity;

            /// <summary>Facing, in RADIANS (matches EntityStateCodec).</summary>
            public readonly float   Yaw;

            /// <summary>Nose up/down and bank, in RADIANS. Zero when the
            /// server judged the entity upright and skipped the bytes.</summary>
            public readonly float   Pitch;
            public readonly float   Roll;

            /// <summary>What the entity is doing. Always present.</summary>
            public readonly MovementState State;

            public readonly bool    IsNpc;

            public Entry(int id, Vector3 pos, Vector3 velocity, float yaw, float pitch, float roll, MovementState state, bool isNpc)
            {
                NetworkId = id;
                Position  = pos;
                Velocity  = velocity;
                Yaw       = yaw;
                Pitch     = pitch;
                Roll      = roll;
                State     = state;
                IsNpc     = isNpc;
            }
        }

        public delegate void EntryHandler(in Entry entry, uint tick);

        /// <summary>
        /// Decodes in place and invokes the handler per entity. No
        /// allocation, no intermediate list - this runs at tick rate with
        /// up to ~60 entries and lives on the Unity main thread.
        /// The opcode is assumed already consumed by the dispatcher.
        /// </summary>
        public static void Read(ReadOnlySpan<byte> payload, EntryHandler onEntry)
        {
            if (payload.Length < HeaderSize - 2)
                return;

            // Offsets are relative to the start of the packet including the
            // 2-byte opcode, matching SnapshotWriter. Caller passes the full
            // packet; we skip the opcode here.
            var tick    = BinaryPrimitives.ReadUInt32LittleEndian(payload[2..]);
            var originX = BinaryPrimitives.ReadInt32LittleEndian(payload[6..]);
            var originY = BinaryPrimitives.ReadInt32LittleEndian(payload[10..]);
            var originZ = BinaryPrimitives.ReadInt32LittleEndian(payload[14..]);
            var count   = BinaryPrimitives.ReadUInt16LittleEndian(payload[18..]);

            var offset = HeaderSize;

            for (int i = 0; i < count; i++)
            {
                if (offset + 5 > payload.Length) break;

                var span  = payload[offset..];
                var id    = BinaryPrimitives.ReadInt32LittleEndian(span);
                var flags = (EntryFlags)span[4];

                offset += 5;

                var position = Vector3.zero;
                var velocity = Vector3.zero;
                var yaw = 0f;
                var pitch = 0f;
                var roll = 0f;
                var state = MovementState.None;

                if ((flags & EntryFlags.Position) != 0)
                {
                    if (offset + 6 > payload.Length) break;

                    var p = payload[offset..];
                    position = new Vector3(
                        originX + BinaryPrimitives.ReadInt16LittleEndian(p)      / PositionScale,
                        originY + BinaryPrimitives.ReadInt16LittleEndian(p[2..]) / PositionScale,
                        originZ + BinaryPrimitives.ReadInt16LittleEndian(p[4..]) / PositionScale);

                    offset += 6;
                }

                if ((flags & EntryFlags.Yaw) != 0)
                {
                    if (offset + 1 > payload.Length) break;
                    yaw = payload[offset] / 256f * (Mathf.PI * 2f);
                    offset += 1;
                }

                // Always present - not flag-gated. See MovementState for
                // why it can't be sent only on change.
                if (offset + 1 > payload.Length) break;
                state = (MovementState)payload[offset];
                offset += 1;

                if ((flags & EntryFlags.Velocity) != 0)
                {
                    if (offset + 3 > payload.Length) break;

                    // Signed bytes. The cast from byte is unchecked by
                    // default in C#, which is what we want - 0xFF has to
                    // come back as -1, not 255.
                    // Square-root curve up to 100 u/s, shared with the
                    // server's writer (VelocityCodec, audit M8).
                    velocity = new Vector3(
                        ArcheCore.Network.Shared.VelocityCodec.Decode((sbyte)payload[offset]),
                        ArcheCore.Network.Shared.VelocityCodec.Decode((sbyte)payload[offset + 1]),
                        ArcheCore.Network.Shared.VelocityCodec.Decode((sbyte)payload[offset + 2]));

                    offset += 3;
                }

                // Tilt AFTER velocity - same order as SnapshotWriter.
                if ((flags & EntryFlags.Tilt) != 0)
                {
                    if (offset + 2 > payload.Length) break;
                    pitch = payload[offset]     / 256f * (Mathf.PI * 2f);
                    roll  = payload[offset + 1] / 256f * (Mathf.PI * 2f);
                    offset += 2;
                }

                var entry = new Entry(
                    id, position, velocity, yaw, pitch, roll, state,
                    (flags & EntryFlags.IsNpc) != 0);
                onEntry(in entry, tick);
            }
        }
    }
}