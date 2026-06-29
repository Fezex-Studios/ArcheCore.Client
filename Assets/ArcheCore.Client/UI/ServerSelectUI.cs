using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.W2C;
using TMPro;
using UnityEngine;

namespace Shared
{
    public class ServerSelectUI : MonoBehaviour
    {
        [SerializeField] private TMP_InputField ipInput;

        public void Connect()
        {
            ClientNetwork.Instance.Connect(ipInput.text);
        }
    }
}