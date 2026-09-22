using ArcheCore.Client.UI.State;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>
    /// Writes the initial character picture into LocalCharacterState,
    /// which caches it and raises the same three events the old separate
    /// packets each raised.
    ///
    /// It goes through the cache rather than raising the events directly
    /// because this packet arrives in the same burst as W2CSpawnPlayer -
    /// i.e. while main_world is still loading, before any HUD object
    /// exists to have subscribed. Raising straight to the events here
    /// would drop the whole thing on the floor on every login.
    /// </summary>
    public class W2CEnterWorldHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CEnterWorldPacket>(reader.GetRemainingBytes());

            LocalCharacterState.ApplyEnterWorld(packet.Character, packet.Gold, packet.Inventory,
                                                packet.Health, packet.MaxHealth);
        }
    }
}