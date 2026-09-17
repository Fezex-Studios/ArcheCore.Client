using System;
using System.Collections.Generic;
using ArcheCore.Client.Gameplay;
using ArcheCore.Network.Client;
using LiteNetLib;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>
    /// Handles W2CWorldSnapshot (opcode 30) - the batched, quantized,
    /// UNRELIABLE movement packet built by the server's SnapshotWriter.
    ///
    /// Not MessagePack. SnapshotReader decodes the raw layout, and it expects
    /// the FULL packet including the 2-byte opcode. The dispatcher has already
    /// consumed the opcode from the reader, so we copy the remaining bytes
    /// into a reusable buffer behind 2 placeholder bytes.
    ///
    /// Rules (see SnapshotReader):
    ///  - Snapshots can arrive out of order: keep the newest tick per entity.
    ///  - An entity missing from a snapshot has NOT despawned. Despawns only
    ///    arrive on W2CPlayerLeave / W2CNpcDespawn.
    /// </summary>
    public class W2CWorldSnapshotHandler : IClientPacketHandler
    {
        private const int OpcodeSize = 2;

        // Newest tick applied per entity, for dropping stale snapshots.
        private static readonly Dictionary<int, uint> LastTick = new();

        private byte[] _buffer = new byte[1024];

        /// <summary>Call when starting a new connection (server ticks restart at 1).</summary>
        public static void Reset() => LastTick.Clear();

        public void Handle(NetPacketReader reader)
        {
            byte[] body = reader.GetRemainingBytes();
            int total = OpcodeSize + body.Length;

            if (_buffer.Length < total)
                _buffer = new byte[total];

            _buffer[0] = 0;
            _buffer[1] = 0;
            Buffer.BlockCopy(body, 0, _buffer, OpcodeSize, body.Length);

            SnapshotReader.Read(new ReadOnlySpan<byte>(_buffer, 0, total), Apply);
        }

        private static void Apply(in SnapshotReader.Entry entry, uint tick)
        {
            if (LastTick.TryGetValue(entry.NetworkId, out var last) && tick <= last)
                return; // older than what we already applied

            var network = ClientNetwork.Instance;
            if (network != null && entry.NetworkId == network.LocalNetworkId)
                return; // never move the local player from the server

            if (entry.IsNpc)
            {
                var npcs = NpcRegistry.Instance;
                if (npcs == null || !npcs.TryGetNpc(entry.NetworkId, out _))
                {
                    // Not spawned (yet or anymore) - don't keep state for it.
                    LastTick.Remove(entry.NetworkId);
                    return;
                }

                LastTick[entry.NetworkId] = tick;
                npcs.UpdatePosition(entry.NetworkId, entry.Position);
            }
            else
            {
                var players = PlayerRegistry.Instance;
                if (players == null || !players.TryGetPlayer(entry.NetworkId, out _))
                {
                    LastTick.Remove(entry.NetworkId);
                    return;
                }

                LastTick[entry.NetworkId] = tick;
                players.UpdatePosition(entry.NetworkId, entry.Position);
            }
        }
    }
}