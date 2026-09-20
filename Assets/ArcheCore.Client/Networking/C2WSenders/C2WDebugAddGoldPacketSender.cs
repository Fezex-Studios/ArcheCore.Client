using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking.C2WSenders
{
    /// <summary>
    /// DEV ONLY - mirrors C2WLevelUpPacketSender. Only does anything on a
    /// server with AllowDebugCommands true; delete alongside
    /// C2WDebugAddGoldHandler once a real gold source (shop, loot, quest
    /// reward) exists server-side.
    /// </summary>
    public static class C2WDebugAddGoldPacketSender
    {
        public static void Send(NetPeer peer, int amount)
        {
            if (peer == null)
                return;

            ClientPacketSender.SendPacket(
                peer,
                Opcodes.C2WDebugAddGold,
                new C2WDebugAddGoldPacket { Amount = amount });
        }
    }
}