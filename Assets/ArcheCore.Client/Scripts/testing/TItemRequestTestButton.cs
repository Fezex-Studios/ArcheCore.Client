using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2W;
using ArcheCore.Client.Networking.C2WSenders;
using UnityEngine;

namespace ArcheCore.Client.testing
{
    public class TItemRequestTestButton : MonoBehaviour
    {
        [SerializeField] private int testItemId = 1;


        public void OnRequestItemButtonClicked()
        {
            if (ClientNetwork.Instance == null || ClientNetwork.Instance.ServerPeer == null)
            {
                Debug.LogWarning("Not Connected to WorldServer");
                return;
            }

            C2WItemRequestDataPacketSender.Send(ClientNetwork.Instance.ServerPeer, testItemId);
        }

        public void OnLevelUpButtonClicked()
        {
            if (ClientNetwork.Instance?.ServerPeer == null) return;
            C2WLevelUpPacketSender.Send(ClientNetwork.Instance.ServerPeer);
        }
        
    }
}