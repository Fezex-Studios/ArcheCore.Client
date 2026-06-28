namespace Client.Scripts
{
    /// <summary>
    /// Holds session state for the currently authenticated client.
    /// Populated by CommandLineBootstrap on startup (token from -token arg)
    /// and by AuthenticateHandler once the server confirms the session.
    /// </summary>
    public class SessionManager
    {
        public static string Token
        {
            get;
            set;
        }

        public static int AccountId
        {
            get;
            set;
        }
    }
}