using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking.C2WSenders
{
    /// <summary>
    /// Take off one equipment slot (roadmap 3.2). Putting gear ON is the
    /// normal right-click use on a bag slot - the server equips anything
    /// that has an equip slot.
    /// </summary>
    public static class C2WUnequipPacketSender
    {
        public static void Send(NetPeer peer, int equipSlot)
        {
            if (peer == null) return;
            ClientPacketSender.SendPacket(peer, Opcodes.C2WUnequip, new C2WUnequipPacket { Slot = equipSlot });
        }
    }
}
