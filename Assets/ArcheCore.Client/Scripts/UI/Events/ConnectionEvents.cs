using System;

namespace ArcheCore.Client.UI.Events
{
    /// <summary>
    /// World-server connection lifecycle. Raised by ClientNetwork on the Unity
    /// main thread. Subscribed to by LoginScreenManager / ServerSelectUI.
    /// </summary>
    public static class ConnectionEvents
    {
        /// <summary>Connection to the world server was lost or refused. Argument is a player-facing message.</summary>
        public static event Action<string> OnDisconnected;

        public static void RaiseDisconnected(string message) => OnDisconnected?.Invoke(message);
    }
}