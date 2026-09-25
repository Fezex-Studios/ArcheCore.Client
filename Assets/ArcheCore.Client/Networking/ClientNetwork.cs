using System;
using ArchCore.Client;
using ArcheCore.Client.Networking.C2W;
using ArcheCore.Client.Networking.W2C;
using ArcheCore.Client.UI.Events;
using ArcheCore.Client.UI.State;
using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Client;
using LiteNetLib;
using MessagePack;
using UnityEngine;

namespace ArcheCore.Client.Networking
{
    public class ClientNetwork :
        MonoBehaviour,
        INetEventListener
    {
        public static ClientNetwork Instance;

        /// <summary>Scene the client returns to after a disconnect.</summary>
        public const string ServerSelectSceneName = "server_select";

        private const int WorldServerPort = 7777;
        private const string ConnectionKey = "MMO";

#if UNITY_EDITOR
        [Header("Editor Testing Only - not used in builds")]
        [SerializeField] private string editorToken = "";
#endif

        public int              LocalNetworkId { get; set; }
        public NetPeer          ServerPeer     { get; private set; }
        public PlayerController LocalPlayer    { get; set; }

        /// <summary>Why the last connection ended. Shown by ServerSelectUI; cleared on a successful connect.</summary>
        public static string LastDisconnectMessage { get; private set; }

        private NetManager client;
        private readonly ClientPacketDispatcher dispatcher = new();
        private bool _quitting;

        private void Awake()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 120;

            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            LocalCharacterState.Init();

            ReadCommandLineToken();
        }

        private void ReadCommandLineToken()
        {
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(editorToken))
            {
                SessionManager.Token = editorToken;
                Debug.Log("[ClientNetwork] Using Editor token override.");
                return;
            }
#endif

            string[] args = Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-token" && i + 1 < args.Length)
                {
                    SessionManager.Token = args[i + 1];
                    Debug.Log("[ClientNetwork] Token loaded from command line.");
                }
            }
        }

        private void Start()
        {
            RegisterHandlers();
        }

        public void Connect(string ip)
        {
            // Tear down any previous client so we don't bind a second socket.
            if (client != null)
            {
                client.Stop();
                client = null;
            }

            ServerPeer = null;
            LocalNetworkId = 0;
            LocalPlayer = null;

            if (!SessionManager.HasUsableToken)
            {
                FailConnection("You need to log in again.");
                return;
            }

            client = new NetManager(this);

            if (!client.Start())
            {
                Debug.LogError("[ClientNetwork] Failed to start NetManager — local port may already be in use.");
                FailConnection("Couldn't open a network socket.");
                return;
            }

            // Server tick numbers restart with each connection, and nothing
            // queued for a previous world session may run in this one.
            W2CWorldSnapshotHandler.Reset();
            WorldLoader.ClearPending();
            LocalCharacterState.Reset();

            client.Connect(ip, WorldServerPort, ConnectionKey);
        }

        private void OnDestroy()
        {
            client?.Stop();
        }

        private void OnApplicationQuit()
        {
            _quitting = true;
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
            dispatcher.Register(Opcodes.Announcement,   new W2CAnnouncementHandler());
            dispatcher.Register(Opcodes.SpawnNpc,       new W2CSpawnNpcHandler());
            dispatcher.Register(Opcodes.NpcPosition,    new W2CNpcPositionHandler());
            dispatcher.Register(Opcodes.NpcDespawn,     new W2CNpcDespawnHandler());

            dispatcher.Register(Opcodes.W2CTestPacket,       new W2CTestPacketHandler());
            dispatcher.Register(Opcodes.PlayerLevelResponse, new W2CPlayerlevelResponseHandler());

            // Character roster after authentication
            dispatcher.Register(Opcodes.W2CCharacterList, new W2CCharacterListHandler());

            // --- Interaction system ---
            dispatcher.Register(Opcodes.W2CInteractDialogue, new W2CInteractDialogueHandler());
            dispatcher.Register(Opcodes.W2CInteractLoot,     new W2CInteractLootHandler());
            dispatcher.Register(Opcodes.W2CInteractDenied,   new W2CInteractDeniedHandler());
            dispatcher.Register(Opcodes.ChatMessage,         new W2CChatMessageHandler());

            dispatcher.Register(Opcodes.ItemDataResponse, new W2CItemDataResponseHandler());
            dispatcher.Register(Opcodes.W2CEnterWorld,    new W2CEnterWorldHandler());

            // Batched movement snapshots (opcode 30) - the only way the server
            // sends other players' movement.
            dispatcher.Register(Opcodes.W2CWorldSnapshot, new W2CWorldSnapshotHandler());
            
            
            dispatcher.Register(Opcodes.W2CPositionCorrection, new W2CPositionCorrectionHandler());
            dispatcher.Register(Opcodes.W2CJumpEvent,          new W2CJumpEventHandler());
            dispatcher.Register(Opcodes.W2CGoldUpdate,new W2CGoldUpdatehandler());
            dispatcher.Register(Opcodes.W2CInventorySnapshot, new W2CInventorySnapshotHandler());
            dispatcher.Register(Opcodes.W2CInventorySlotChanged, new W2CInventorySlotChangedHandler());
            dispatcher.Register(Opcodes.W2CItemCooldown, new W2CItemCooldownHandler());

            // Harvesting (roadmap E)
            dispatcher.Register(Opcodes.W2CSpawnHarvestNode, new W2CSpawnHarvestNodeHandler());
            dispatcher.Register(Opcodes.W2CHarvestNodeState, new W2CHarvestNodeStateHandler());
            dispatcher.Register(Opcodes.W2CHarvestStarted,   new W2CHarvestStartedHandler());
            dispatcher.Register(Opcodes.W2CHarvestCompleted, new W2CHarvestCompletedHandler());
            dispatcher.Register(Opcodes.W2CHarvestCancelled, new W2CHarvestCancelledHandler());

            // NPC shops (roadmap F)
            dispatcher.Register(Opcodes.W2CShopOpen,   new W2CShopOpenHandler());
            dispatcher.Register(Opcodes.W2CShopResult, new W2CShopResultHandler());

            // Combat and loot (roadmap G/H/I)
            dispatcher.Register(Opcodes.W2CCombatEvent,  new W2CCombatEventHandler());
            dispatcher.Register(Opcodes.W2CHealthUpdate, new W2CHealthUpdateHandler());
            dispatcher.Register(Opcodes.W2CSpawnCorpse,  new W2CSpawnCorpseHandler());

            // Interaction rework
            dispatcher.Register(Opcodes.W2CLootWindow,   new W2CLootWindowHandler());

            // Death and respawn (roadmap J)
            dispatcher.Register(Opcodes.W2CPlayerDeath,  new W2CPlayerDeathHandler());
            dispatcher.Register(Opcodes.W2CRespawn,      new W2CRespawnHandler());
            dispatcher.Register(Opcodes.W2CNpcHealth,    new W2CNpcHealthHandler());

            // Quests (roadmap K/L/M)
            dispatcher.Register(Opcodes.W2CQuestCatalog, new W2CQuestCatalogHandler());
            dispatcher.Register(Opcodes.W2CQuestLog,     new W2CQuestLogHandler());
            dispatcher.Register(Opcodes.W2CQuestUpdate,  new W2CQuestUpdateHandler());
            dispatcher.Register(Opcodes.W2CQuestOffers,  new W2CQuestOffersHandler());

            // Mounts (roadmap N)
            dispatcher.Register(Opcodes.W2CMountState,   new W2CMountStateHandler());

            // Market: auction house, cash shop, mailboxes (roadmap P)
            dispatcher.Register(Opcodes.W2CMailList,     new W2CMailListHandler());
            dispatcher.Register(Opcodes.W2CAuctionList,  new W2CAuctionListHandler());
            dispatcher.Register(Opcodes.W2CCashShopList, new W2CCashShopListHandler());
            dispatcher.Register(Opcodes.W2CMarketResult, new W2CMarketResultHandler());
        }

        public void OnPeerConnected(NetPeer peer)
        {
            ServerPeer = peer;
            LastDisconnectMessage = null;

            C2WAuthenticatePacket.Send(peer, SessionManager.Token);

            // The world server burns the token when it validates it.
            SessionManager.MarkTokenUsed();

            Debug.Log("[ClientNetwork] Connected - authentication sent.");
        }

        public void OnNetworkReceive(
            NetPeer          peer,
            NetPacketReader  reader,
            byte             channel,
            DeliveryMethod   delivery)
        {
            Opcodes packet = 0;

            try
            {
                packet = (Opcodes)reader.GetUShort();
                dispatcher.Handle(packet, reader);
            }
            catch (MessagePackSerializationException e)
            {
                // Usually means client and server were built from different
                // ArcheCore.Network versions.
                Debug.LogError($"[ClientNetwork] Could not read {packet} packet (ArcheCore.Network.dll out of date?): {e.Message}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClientNetwork] Handler for {packet} threw:");
                Debug.LogException(e);
            }
            finally
            {
                reader.Recycle();
            }
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            if (_quitting)
                return;

            // Ignore a stale peer from an older connection.
            if (ServerPeer != null && ServerPeer != peer)
                return;

            string message = DescribeDisconnect(info);
            Debug.LogWarning($"[ClientNetwork] Disconnected ({info.Reason}): {message}");

            FailConnection(message);
        }

        /// <summary>
        /// Common "connection is over" path: reset state, go back to the
        /// server select screen and tell the UI why.
        /// </summary>
        private void FailConnection(string message)
        {
            ServerPeer = null;
            LocalNetworkId = 0;
            LocalPlayer = null;

            // Token is spent (or was never valid) - a fresh login is required.
            SessionManager.ClearToken();

            W2CWorldSnapshotHandler.Reset();
            LocalCharacterState.Reset();

            LastDisconnectMessage = message;

            // Loads server_select (or queues it if main_world is mid-load).
            WorldLoader.ReturnToScene(ServerSelectSceneName);

            ConnectionEvents.RaiseDisconnected(message);
        }

        private static string DescribeDisconnect(DisconnectInfo info)
        {
            switch (info.Reason)
            {
                case DisconnectReason.ConnectionFailed:
                    return "Could not reach the world server.";
                case DisconnectReason.Timeout:
                    return "Connection to the world server timed out.";
                case DisconnectReason.HostUnreachable:
                case DisconnectReason.NetworkUnreachable:
                    return "Network unreachable. Check your connection.";
                case DisconnectReason.ConnectionRejected:
                    return "The world server rejected the connection (client may be out of date).";
                case DisconnectReason.RemoteConnectionClose:
                    return "Disconnected by the server. Your session may have expired or you logged in elsewhere. Please log in again.";
                case DisconnectReason.DisconnectPeerCalled:
                    return "Disconnected.";
                default:
                    return $"Disconnected ({info.Reason}). Please log in again.";
            }
        }

        public void OnConnectionRequest(ConnectionRequest request) { }

        public void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError error)
        {
            Debug.LogWarning($"[ClientNetwork] Network error: {error}");
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    }
}