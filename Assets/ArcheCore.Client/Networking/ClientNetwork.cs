using ArcheCore.Client.Networking.C2W;
using ArcheCore.Client.Networking.W2C;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Client;
using Client.Scripts;
using LiteNetLib;

using UnityEngine;


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
        private readonly ClientPacketDispatcher dispatcher = new();

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
            dispatcher.Register(Opcodes.MOTD,           new W2CMOTDHandler());
            dispatcher.Register(Opcodes.SpawnPlayer,    new W2CSpawnPlayerHandler());
            dispatcher.Register(Opcodes.PlayerPosition, new W2CPlayerPositionHandler());
            dispatcher.Register(Opcodes.PlayerLeave,    new W2CPlayerLeaveHandler());
            dispatcher.Register(Opcodes.Announcement, new W2CAnnouncementHandler());
            dispatcher.Register(Opcodes.SpawnNpc, new W2CSpawnNpcHandler());
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
            Opcodes packet = (Opcodes)reader.GetUShort();
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