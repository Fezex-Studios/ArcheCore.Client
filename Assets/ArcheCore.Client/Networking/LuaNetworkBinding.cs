using MoonSharp.Interpreter;

namespace ArcheCore.Client.Networking
{
    [MoonSharpUserData]
    public class LuaNetworkBinding
    {
        public void RequestPlayerLevel()
        {
            if (ClientNetwork.Instance.ServerPeer == null)
            {
                UnityEngine.Debug.LogWarning("[LuaNetworkBinding] Not connected to world server.");
                return;
            }

            C2W.C2WRequestPlayerLevelPacketSender.Send(ClientNetwork.Instance.ServerPeer);
        }
    }
}