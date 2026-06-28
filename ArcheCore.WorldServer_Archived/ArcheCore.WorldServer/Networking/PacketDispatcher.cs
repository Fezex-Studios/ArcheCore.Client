using System.Collections.Generic;
using LiteNetLib;
using Shared;

namespace ArcheCore.WorldServer.Networking
{
    public class PacketDispatcher
    {
        private readonly Dictionary<
            Opcode,
            IPacketHandler> handlers =
            new();

        public void Register(
            Opcode packet,
            IPacketHandler handler)
        {
            handlers[packet] =
                handler;
        }

        public void Handle(
            Opcode packet,
            NetPeer peer,
            NetPacketReader reader)
        {
            if (handlers.TryGetValue(
                    packet,
                    out var handler))
            {
                handler.Handle(
                    peer,
                    reader);
            }
            else
            {
                WorldLogger.Warning(
                    $"Unhandled packet: {packet}");
            }
        }
    }
}