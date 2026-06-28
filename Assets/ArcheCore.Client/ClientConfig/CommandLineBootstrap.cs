using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.W2C;
using Client.Scripts;

using UnityEngine;

/// <summary>
/// Reads command-line arguments and auto-connects when a -token is present.
///
/// Why Awake + Start split:
///   Awake  — parse args and store token. Runs before any Start().
///   Start  — call Connect(). By this point ALL Awake() calls in the scene
///             have finished, so ClientNetwork.Instance is guaranteed non-null.
///
/// No Script Execution Order setting required.
/// </summary>
public class CommandLineBootstrap : MonoBehaviour
{
    [Tooltip("IP used when no -ip argument is passed.")]
    [SerializeField] private string fallbackIp = "127.0.0.1";

    private string _connectIp;
    private bool   _shouldAutoConnect;

    // ── Phase 1: parse only. No Connect() here. ──────────────────────────────
    private void Awake()
    {
        string[] args = System.Environment.GetCommandLineArgs();

        string token = null;
        string ip    = null;

        for (int i = 0; i < args.Length; i++)
        {
            Debug.Log($"ARG: {args[i]}");

            if (args[i] == "-token" && i + 1 < args.Length)
            {
                token = args[i + 1];
                SessionManager.Token = token;
                Debug.Log($"[Bootstrap] Token loaded: {token}");
            }

            if (args[i] == "-ip" && i + 1 < args.Length)
            {
                ip = args[i + 1];
                Debug.Log($"[Bootstrap] IP loaded: {ip}");
            }
        }

        if (!string.IsNullOrEmpty(token))
        {
            _connectIp         = !string.IsNullOrEmpty(ip) ? ip : fallbackIp;
            _shouldAutoConnect = true;
        }
    }

    // ── Phase 2: connect. All Awake() calls done — Instance is safe. ─────────
    private void Start()
    {
        if (!_shouldAutoConnect)
            return;

        if (ClientNetwork.Instance == null)
        {
            Debug.LogError(
                "[Bootstrap] ClientNetwork.Instance is null in Start(). " +
                "Make sure ClientNetwork is in the same scene as CommandLineBootstrap.");
            return;
        }

        Debug.Log($"[Bootstrap] Auto-connecting to {_connectIp}");
        ClientNetwork.Instance.Connect(_connectIp);
    }
}