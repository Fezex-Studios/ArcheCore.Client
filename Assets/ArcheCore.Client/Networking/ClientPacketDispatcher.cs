using System.Collections.Generic;
using ArcheCore.Library.Net.Worldserver;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking
{
    public class PacketDispatcher
    {
        private readonly Dictionary<
                Opcodes,
                IClientPacketHandler>
            handlers = new();

        public void Register(
            Opcodes packet,
            IClientPacketHandler handler)
        {
            handlers[packet] =
                handler;
        }

        public void Handle(
            Opcodes packet,
            NetPacketReader reader)
        {
            if(handlers.TryGetValue(
                   packet,
                   out var handler))
            {
                handler.Handle(
                    reader);
            }
        }
    }
}