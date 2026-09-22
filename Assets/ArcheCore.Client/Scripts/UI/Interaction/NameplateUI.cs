using System.Collections.Generic;
using ArcheCore.Client.Gameplay;
using ArcheCore.Client.Gameplay.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// Names and health bars floating over NPCs. Creates itself.
    ///
    ///   enemy (can be attacked)  red name + health bar
    ///   friendly (MaxHealth 0)   green name, "&lt;Merchant&gt;" title, no bar
    ///   your current target      gold border on its bar, brighter name
    ///
    /// Only for NPCs within maxDistance, and hidden behind the camera. Plates
    /// are pooled: one per visible NPC, reused as NPCs come and go.
    /// </summary>
    public class NameplateUI : MonoBehaviour
    {
        [SerializeField] private float maxDistance = 45f;
        [SerializeField] private float heightAboveFeet = 2.4f;
        [SerializeField] private Vector2 barSize = new Vector2(80f, 7f);
        [SerializeField] private float rescanInterval = 0.5f;

        private sealed class Plate
        {
            public RectTransform Root;
            public TMP_Text Name;
            public RectTransform BarBack, BarFill;
            public Outline Border;
        }

        private RectTransform _layer;
        private readonly Dictionary<NpcIdentity, Plate> _plates = new();
        private readonly Stack<Plate> _pool = new();
        private readonly List<NpcIdentity> _npcs = new();
        private readonly List<NpcIdentity> _gone = new();
        private float _nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<NameplateUI>() != null) return;
            var go = new GameObject("NameplateUI");
            DontDestroyOnLoad(go);
            go.AddComponent<NameplateUI>();
        }

        private void LateUpdate()
        {
            if (_layer == null)
            {
                _layer = RuntimeUI.CreateLayer("Nameplates", 2, clickable: false);
                if (_layer == null) return;
            }

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + rescanInterval;
                _npcs.Clear();
                _npcs.AddRange(FindObjectsByType<NpcIdentity>(FindObjectsSortMode.None));
            }

            var cam = Camera.main;
            if (cam == null) return;
            Vector3 camPos = cam.transform.position;

            // Retire plates whose NPC is gone.
            _gone.Clear();
            foreach (var kv in _plates) if (kv.Key == null) _gone.Add(kv.Key);
            foreach (var dead in _gone) Release(dead);

            foreach (var npc in _npcs)
            {
                if (npc == null) continue;

                Vector3 head = npc.transform.position + Vector3.up * heightAboveFeet;
                Vector3 screen = cam.WorldToScreenPoint(head);
                bool visible = screen.z > 0f && Vector3.Distance(camPos, head) <= maxDistance;

                if (!visible) { if (_plates.ContainsKey(npc)) Release(npc); continue; }

                if (!_plates.TryGetValue(npc, out var plate))
                {
                    plate = Acquire();
                    _plates[npc] = plate;
                }

                Draw(plate, npc);

                if (RuntimeUI.ScreenToLocal(_layer, screen, out var local))
                    plate.Root.anchoredPosition = local;
            }
        }

        private void Draw(Plate p, NpcIdentity npc)
        {
            bool hostile = npc.MaxHealth > 0;
            bool selected = CombatClient.TargetId == npc.NetworkId;

            Color c = hostile ? RuntimeUI.Hostile : RuntimeUI.Friendly;
            if (selected) c = Color.Lerp(c, Color.white, 0.35f);
            p.Name.color = c;

            var id = npc.GetComponent<ArcheCore.Client.World.InteractableIdentity>();
            string title = id != null ? id.Title : null;
            p.Name.text = string.IsNullOrEmpty(title) ? npc.NpcName : $"<size=80%>&lt;{title}&gt;</size>\n{npc.NpcName}";

            p.BarBack.gameObject.SetActive(hostile);
            if (hostile)
            {
                float pct = Mathf.Clamp01(npc.MaxHealth > 0 ? (float)npc.Health / npc.MaxHealth : 0f);
                p.BarFill.anchorMax = new Vector2(pct, 1f);
                p.Border.effectColor = selected ? RuntimeUI.Gold : new Color(0f, 0f, 0f, 0.8f);
                p.Border.effectDistance = selected ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
            }
        }

        private Plate Acquire()
        {
            if (_pool.Count > 0)
            {
                var reused = _pool.Pop();
                reused.Root.gameObject.SetActive(true);
                return reused;
            }

            var root = RuntimeUI.NewRect("Plate", _layer);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(160f, 40f);

            var back = RuntimeUI.NewImage("Bar", root, RuntimeUI.Inset);
            var br = back.rectTransform;
            br.anchorMin = br.anchorMax = new Vector2(0.5f, 0f); br.pivot = new Vector2(0.5f, 0f);
            br.sizeDelta = barSize;
            var border = back.gameObject.AddComponent<Outline>();

            var fill = RuntimeUI.NewImage("Fill", br, RuntimeUI.Health);
            RuntimeUI.Stretch(fill.rectTransform);

            var name = RuntimeUI.NewText("Name", root, 12f, RuntimeUI.Text, FontStyles.Bold, TextAlignmentOptions.Bottom);
            var nr = name.rectTransform;
            nr.anchorMin = new Vector2(0f, 0f); nr.anchorMax = new Vector2(1f, 1f);
            nr.offsetMin = new Vector2(0f, barSize.y + 2f); nr.offsetMax = new Vector2(0f, 20f);

            return new Plate { Root = root, Name = name, BarBack = br, BarFill = fill.rectTransform, Border = border };
        }

        private void Release(NpcIdentity npc)
        {
            if (!_plates.TryGetValue(npc, out var plate)) return;
            _plates.Remove(npc);
            plate.Root.gameObject.SetActive(false);
            _pool.Push(plate);
        }
    }
}
