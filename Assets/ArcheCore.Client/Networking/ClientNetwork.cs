using ArchCore.Client;
using ArcheCore.Client.Networking.C2W;
using ArcheCore.Client.Networking.W2C;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Client;
using LiteNetLib;

using UnityEngine;


namespace ArcheCore.Client.Networking
{
    public class ClientNetwork :
        MonoBehaviour,
        INetEventListener
    {
        public static ClientNetwork Instance;

#if UNITY_EDITOR
        [Header("Editor Testing Only - not used in builds")]
        [SerializeField] private string editorToken = "";
#endif

        public int              LocalNetworkId { get; set; }
        public NetPeer          ServerPeer     { get; private set; }
        public PlayerController LocalPlayer    { get; set; }

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
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(editorToken))
            {
                SessionManager.Token = editorToken;
                Debug.Log($"[ClientNetwork] Using Editor token override: {editorToken}");
                return;
            }
#endif

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
            // If a previous session left a client running (e.g. Stop/Play again
            // in the Editor without a clean shutdown), tear it down first so we
            // don't try to bind a second socket on the same port.
            if (client != null)
            {
                client.Stop();
                client = null;
            }

            ServerPeer = null;

            client = new NetManager(this);

            if (!client.Start())
            {
                Debug.LogError("[ClientNetwork] Failed to start NetManager — local port may already be in use.");
                return;
            }

            client.Connect(ip, 7777, "MMO");
        }
        private void OnDestroy()
        {
            client?.Stop();
        }
        private void OnApplicationQuit()
        {
            client?.Stop();
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
            dispatcher.Register(Opcodes.NpcPosition, new W2CNpcPositionHandler());
            dispatcher.Register(Opcodes.NpcDespawn, new W2CNpcDespawnHandler());
            
            dispatcher.Register(Opcodes.W2CTestPacket, new W2CTestPacketHandler());
            dispatcher.Register(Opcodes.PlayerLevelResponse, new W2CPlayerlevelResponseHandler());

            // NEW — replaces W2CCharacterNotFound in the login flow
            dispatcher.Register(Opcodes.W2CCharacterList, new W2CCharacterListHandler());

            // --- Interaction system ---
            dispatcher.Register(Opcodes.W2CInteractDialogue, new W2CInteractDialogueHandler());
            dispatcher.Register(Opcodes.W2CInteractLoot,      new W2CInteractLootHandler());
            dispatcher.Register(Opcodes.W2CInteractDenied,    new  W2CInteractDeniedHandler());
            dispatcher.Register(Opcodes.ChatMessage, new W2CChatMessageHandler());
            
            dispatcher.Register(Opcodes.ItemDataResponse, new W2CItemDataResponseHandler());
            dispatcher.Register(Opcodes.PlayerSpawned, new W2CCharacterDataHandler());
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

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            if (ServerPeer == peer)
                ServerPeer = null;
        }
        public void OnConnectionRequest(ConnectionRequest request) { }
        public void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError error) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    }
}