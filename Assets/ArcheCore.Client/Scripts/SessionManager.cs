namespace ArcheCore.Client
{
    /// <summary>
    /// Holds session state for the current client.
    ///
    /// Auth Server tokens are ONE-SHOT: the World Server burns the token the
    /// moment it validates it. So once a token has been sent to a world
    /// server it is marked used, and reconnecting requires a new login.
    /// </summary>
    public static class SessionManager
    {
        private static string _token;

        /// <summary>Setting a new token clears the "used" flag.</summary>
        public static string Token
        {
            get => _token;
            set
            {
                _token = value;
                TokenUsed = false;
            }
        }

        /// <summary>True once Token has been sent to a world server.</summary>
        public static bool TokenUsed { get; private set; }

        /// <summary>A token exists and hasn't been spent yet.</summary>
        public static bool HasUsableToken => !string.IsNullOrEmpty(_token) && !TokenUsed;

        public static int AccountId { get; set; }

        public static void MarkTokenUsed() => TokenUsed = true;

        public static void ClearToken()
        {
            _token = null;
            TokenUsed = false;
        }
    }
}
