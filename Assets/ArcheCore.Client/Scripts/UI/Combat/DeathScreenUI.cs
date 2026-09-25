using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2WSenders;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// "You have died" with a Respawn button. Builds itself the first time
    /// you die - no scene setup.
    ///
    /// Deliberately NOT an IUIPanel: Escape must not close it, and it isn't
    /// part of the window stack. It covers the screen and eats clicks, so
    /// you can't shop or loot while dead.
    /// </summary>
    public class DeathScreenUI : MonoBehaviour
    {
        public static DeathScreenUI Instance { get; private set; }

        private RectTransform _layer, _panel;
        private TMP_Text _killer;

        public static void Show(string killerName)
        {
            if (Instance == null)
            {
                var go = new GameObject("DeathScreenUI");
                DontDestroyOnLoad(go);
                go.AddComponent<DeathScreenUI>();
            }

            Instance.Present(killerName);
        }

        public static void Hide()
        {
            if (Instance != null && Instance._layer != null)
                Instance._layer.gameObject.SetActive(false);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Present(string killerName)
        {
            if (_layer == null && !Build())
                return;

            _killer.text = string.IsNullOrEmpty(killerName) ? "" : $"Killed by {RichText.Safe(killerName)}";
            _layer.gameObject.SetActive(true);
        }

        private bool Build()
        {
            _layer = RuntimeUI.CreateLayer("DeathScreen", 400, clickable: true);   // above everything
            if (_layer == null) return false;

            var dim = RuntimeUI.NewImage("Dim", _layer, new Color(0.25f, 0f, 0f, 0.45f), raycast: true);
            RuntimeUI.Stretch(dim.rectTransform);

            var panel = RuntimeUI.NewPanel("Panel", _layer, raycast: true);
            _panel = panel.rectTransform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(320f, 160f);

            var title = RuntimeUI.NewText("Title", _panel, 26f, RuntimeUI.Hostile, FontStyles.Bold);
            var tr = title.rectTransform;
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f); tr.pivot = new Vector2(0.5f, 1f);
            tr.offsetMin = new Vector2(10f, -58f); tr.offsetMax = new Vector2(-10f, -18f);
            title.text = "You have died";

            _killer = RuntimeUI.NewText("Killer", _panel, 14f, RuntimeUI.Muted);
            var kr = _killer.rectTransform;
            kr.anchorMin = new Vector2(0f, 1f); kr.anchorMax = new Vector2(1f, 1f); kr.pivot = new Vector2(0.5f, 1f);
            kr.offsetMin = new Vector2(10f, -88f); kr.offsetMax = new Vector2(-10f, -62f);

            var button = RuntimeUI.NewPanel("Respawn", _panel, raycast: true);
            var br = button.rectTransform;
            br.anchorMin = br.anchorMax = new Vector2(0.5f, 0f); br.pivot = new Vector2(0.5f, 0f);
            br.anchoredPosition = new Vector2(0f, 20f);
            br.sizeDelta = new Vector2(140f, 34f);
            button.color = new Color(0.42f, 0.30f, 0.14f, 1f);

            var label = RuntimeUI.NewText("Label", br, 15f, RuntimeUI.Gold, FontStyles.Bold);
            RuntimeUI.Stretch(label.rectTransform);
            label.text = "Respawn";

            var b = button.gameObject.AddComponent<Button>();
            b.targetGraphic = button;
            b.onClick.AddListener(Respawn);

            return true;
        }

        private static void Respawn()
        {
            var peer = ClientNetwork.Instance?.ServerPeer;
            if (peer != null)
                C2WRespawnPacketSender.Send(peer);
        }
    }
}
