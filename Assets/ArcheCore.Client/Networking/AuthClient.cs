using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ArcheCore.Client.ClientConfig;
using UnityEngine;

namespace ArcheCore.Client.Networking
{
    /// <summary>
    /// Talks to the Auth Server's POST /login so the client can get a fresh
    /// one-shot token itself (needed to reconnect after a disconnect without
    /// going back through the launcher).
    ///
    /// Uses HttpClient like GameDataBootstrap. Awaiting from a MonoBehaviour
    /// resumes on the Unity main thread.
    /// </summary>
    public static class AuthClient
    {
        public readonly struct LoginResult
        {
            public readonly bool   Success;
            public readonly string Token;
            public readonly string Error;

            public LoginResult(bool success, string token, string error)
            {
                Success = success;
                Token   = token;
                Error   = error;
            }
        }

        [Serializable]
        private class LoginRequest
        {
            public string Username;
            public string Password;
        }

        [Serializable]
        private class LoginResponse
        {
            public bool   Success;
            public string Token;
            public string Message;
        }

        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private static bool _configLoaded;

        private static string LoginUrl
        {
            get
            {
                if (!_configLoaded)
                {
                    ClientConfigService.Load();   // StreamingAssets/ClientConfig.json, if present
                    _configLoaded = true;
                }

                return ClientConfigService.Config.AuthServerUrl.TrimEnd('/') + "/login";
            }
        }

        public static async Task<LoginResult> LoginAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                return new LoginResult(false, null, "Enter your username and password.");

            string json = JsonUtility.ToJson(new LoginRequest
            {
                Username = username.Trim(),
                Password = password
            });

            HttpResponseMessage response;
            string body;

            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                response = await Http.PostAsync(LoginUrl, content);
                body = await response.Content.ReadAsStringAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AuthClient] Login request failed: {e.Message}");
                return new LoginResult(false, null, "Can't reach the login server.");
            }

            if ((int)response.StatusCode == 429)
                return new LoginResult(false, null, "Too many login attempts. Wait a minute and try again.");

            if (!response.IsSuccessStatusCode)
                return new LoginResult(false, null, $"Login server error ({(int)response.StatusCode}).");

            LoginResponse parsed;
            try
            {
                parsed = JsonUtility.FromJson<LoginResponse>(body);
            }
            catch (Exception)
            {
                return new LoginResult(false, null, "Unexpected response from the login server.");
            }

            if (parsed == null || !parsed.Success || string.IsNullOrEmpty(parsed.Token))
                return new LoginResult(false, null, string.IsNullOrEmpty(parsed?.Message) ? "Login failed." : parsed.Message);

            return new LoginResult(true, parsed.Token, null);
        }
    }
}
