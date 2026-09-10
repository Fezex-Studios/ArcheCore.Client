using ArcheCore.Client.UI;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;
using UnityEngine;

namespace ArcheCore.Client.Networking.W2C
{
    // NOTE: routes through HudMessageDisplay for now since there's no
    // dedicated dialogue panel yet. Once one exists, swap the body of
    // Handle() to open it instead - the packet/handler wiring won't change.
    public class W2CInteractDialogueHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            Debug.Log("[W2CInteractDialogue] *** PACKET RECEIVED ***");

            var packet = MessagePackSerializer
                .Deserialize<W2CInteractDialoguePacket>(
                    reader.GetRemainingBytes());

            Debug.Log(
                $"[W2CInteractDialogue] Speaker={packet.SpeakerName}, Text={packet.Text}");

            HudMessageDisplay.QueueOrShow(
                $"{packet.SpeakerName}: {packet.Text}");
        }
    }
}