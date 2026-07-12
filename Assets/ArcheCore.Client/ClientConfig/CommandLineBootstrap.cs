using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.W2C;
using Client.Scripts;

using UnityEngine;

public class CommandLineBootstrap : MonoBehaviour
{
    [Tooltip("IP used when no -ip argument is passed.")]
    [SerializeField] private string fallbackIp = "127.0.0.1";

#if UNITY_EDITOR
    [Header("Editor Testing Only - not used in builds")]
    [SerializeField] private string editorToken = "";
    [SerializeField] private string editorIp    = "127.0.0.1";
#endif

    private string _connectIp;
    private bool   _shouldAutoConnect;

    private void Awake()
    {
#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(editorToken))
        {
            SessionManager.Token = editorToken;
            _connectIp         = string.IsNullOrEmpty(editorIp) ? fallbackIp : editorIp;
            _shouldAutoConnect = true;
            Debug.Log($"[Bootstrap] Using Editor token override: {editorToken}");
            return;
        }
#endif

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