using ArcheCore.Client.UI;
using ArcheCore.Network.Client;
using LiteNetLib;

namespace ArcheCore.Client.Networking.W2C
{
    public class W2CCharacterNotFoundHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            UnityEngine.Debug.Log("[W2CCharacterNotFound] Handler fired!");
            PlayerUIEvents.RaiseCharacterNotFound();
        }
    }
}