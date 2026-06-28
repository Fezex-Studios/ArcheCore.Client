using MessagePack;
using UnityEngine;

namespace ArcheCore.WorldServer.PersistenceServer.Networking.P2W
{
    public class P2WConnectResponseHandler
        : IPersistencePacketHandler
    {
        public void Handle(PersistencePacket persistencePacket)
        {
            var response =
                MessagePackSerializer
                    .Deserialize<P2WConnectResponse>(
                        persistencePacket.Payload);

            Debug.Log(
                response.Message);
        }
    }
}