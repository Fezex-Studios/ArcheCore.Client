using System.Collections.Generic;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking
{
    public class PacketDispatcher
    {
        private readonly Dictionary<
                Opcode,
                IClientPacketHandler>
            handlers = new();

        public void Register(
            Opcode packet,
            IClientPacketHandler handler)
        {
            handlers[packet] =
                handler;
        }

        public void Handle(
            Opcode packet,
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