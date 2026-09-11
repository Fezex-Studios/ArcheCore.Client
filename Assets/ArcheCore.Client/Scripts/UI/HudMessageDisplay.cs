using TMPro;
using UnityEngine;

namespace ArcheCore.Client.UI
{
    public class HudMessageDisplay : MonoBehaviour
    {
        public static HudMessageDisplay Instance;

        [SerializeField] private TMP_Text motdText;
        [SerializeField] private float displayDuration = 6f;

        private static string pendingMessage;

        private void Awake()
        {
            Instance = this;

            if (pendingMessage != null)
            {
                ShowMessage(pendingMessage);
                pendingMessage = null;
            }
        }

        public static void QueueOrShow(string message)
        {
            if (Instance != null)
                Instance.ShowMessage(message);
            else
                pendingMessage = message;
        }

        public void ShowMessage(string message)
        {
            motdText.text = message;
            motdText.gameObject.SetActive(true);
            CancelInvoke(nameof(Hide));
            Invoke(nameof(Hide), displayDuration);
        }

        private void Hide()
        {
            motdText.gameObject.SetActive(false);
        }
    }
}