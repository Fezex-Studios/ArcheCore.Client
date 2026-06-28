using ArcheCore.Client.Networking.C2W;
using ArcheCore.Client.Networking.W2C;
using Client.Scripts;
using LiteNetLib;
using Shared;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcheCore.Client.Networking
{
    public class ClientNetwork :
        MonoBehaviour,
        INetEventListener
    {
        public static ClientNetwork Instance;

        public int     LocalNetworkId { get; set; }
        public NetPeer ServerPeer     { get; private set; }

        private NetManager      client;
        private readonly PacketDispatcher dispatcher = new();

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            ReadCommandLineToken();
        }

        private void ReadCommandLineToken()
        {
            string[] args = System.Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length; i++)
            {
                Debug.Log($"ARG: {args[i]}");

                if (args[i] == "-token" && i + 1 < args.Length)
                {
                    SessionManager.Token = args[i + 1];
                    Debug.Log($"[ClientNetwork] Token loaded from args: {SessionManager.Token}");
                }
            }
        }

        private void Start()
        {
            RegisterHandlers();
        }

        public void Connect(string ip)
        {
            client = new NetManager(this);
            client.Start();
            client.Connect(ip, 7777, "MMO");
        }

        private void Update()
        {
            client?.PollEvents();
        }

        private void RegisterHandlers()
        {
            dispatcher.Register(Opcode.MOTD,           new W2CMOTDHandler());
            dispatcher.Register(Opcode.SpawnPlayer,    new W2CSpawnPlayerHandler());
            dispatcher.Register(Opcode.PlayerPosition, new W2CPlayerPositionHandler());
            dispatcher.Register(Opcode.PlayerLeave,    new W2CPlayerLeaveHandler());
            dispatcher.Register(Opcode.Announcement, new W2CAnnouncementHandler());
            dispatcher.Register(Opcode.SpawnNpc, new W2CSpawnNpcHandler());
        }

        public void OnPeerConnected(NetPeer peer)
        {
            ServerPeer = peer;

            Debug.Log($"Token = {SessionManager.Token}");

            C2WAuthenticatePacket.Send(peer, SessionManager.Token);

            Debug.Log("Authenticate Sent");
        }

        public void OnNetworkReceive(
            NetPeer          peer,
            NetPacketReader  reader,
            byte             channel,
            DeliveryMethod   delivery)
        {
            Opcode packet = (Opcode)reader.GetUShort();
            dispatcher.Handle(packet, reader);
            reader.Recycle();
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info) { }
        public void OnConnectionRequest(ConnectionRequest request) { }
        public void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError error) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    }
}