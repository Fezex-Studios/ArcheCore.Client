using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.W2C;
using ArcheCore.Client.UI.Interfaces;
using TMPro;
using UnityEngine;

namespace Shared
{
    public class ServerSelectUI : MonoBehaviour,IUIScreen
    {
        [SerializeField] private TMP_InputField ipInput;

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
        
        public void Connect()
        {
            ClientNetwork.Instance.Connect("127.0.0.1");
        }
    }
}