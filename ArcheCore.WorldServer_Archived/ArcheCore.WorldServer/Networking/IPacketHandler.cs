using LiteNetLib;
using LiteNetLib.Utils;

namespace ArcheCore.WorldServer.Networking
{
    public interface IPacketHandler
    {
        void Handle(
            NetPeer peer,
            NetPacketReader reader);
    }
}