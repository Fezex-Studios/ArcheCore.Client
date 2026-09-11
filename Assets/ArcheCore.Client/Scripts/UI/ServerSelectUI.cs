using ArcheCore.Client.Networking;
using ArcheCore.Client.UI.Interfaces;
using TMPro;
using UnityEngine;

namespace ArcheCore.Client.UI
{
    public class ServerSelectUI : MonoBehaviour, IUIPanel
    {
        [SerializeField] private TMP_InputField ipInput;

        public bool IsVisible => gameObject.activeSelf;

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);

        public void Connect()
        {
            ClientNetwork.Instance.Connect(
                string.IsNullOrWhiteSpace(ipInput.text) ? "127.0.0.1" : ipInput.text);
        }
    }
}