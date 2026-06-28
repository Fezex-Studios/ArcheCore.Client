using System;

namespace ArcheCore.WorldServer.ServerConfig
{
    [Serializable]
    public class ServerConfig
    {
        public string ServerName = "ArcheCore";

        public int Port = 7777;

        public int MaxPlayers = 1000;

        public string PersistenceHost = "127.0.0.1";

        public int PersistencePort = 7778;

        public string MOTD = "HELLO WELCOME TO THE SERVER!!!";

        public string AuthServerUrl = "http://127.0.0.1:3000";

        // Must match INTERNAL_SECRET in the Auth Server's .env file.
        // Generate a strong random string and keep it out of source control.
        public string InternalSecret = "replace_this_with_a_real_secret";
    }
}