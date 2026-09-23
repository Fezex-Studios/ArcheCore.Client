using System.Collections.Generic;
using ArchCore.Client;
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
    ///   another player           white name + health bar (PvP)
    ///   your current target      gold border on its bar, brighter name
    ///
    /// Your own character never gets one - you know where you are.
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
        /// <summary>One thing that gets a plate - an NPC or another player.</summary>
        private readonly struct Subject
        {
            public readonly Transform Transform;
            public readonly int NetworkId;
            public readonly string Name;
            public readonly string Title;
            public readonly int Health;
            public readonly int MaxHealth;
            public readonly bool Hostile;

            public Subject(Transform t, int id, string name, string title, int health, int maxHealth, bool hostile)
            {
                Transform = t; NetworkId = id; Name = name; Title = title;
                Health = health; MaxHealth = maxHealth; Hostile = hostile;
            }
        }

        private readonly Dictionary<Transform, Plate> _plates = new();
        private readonly Stack<Plate> _pool = new();
        private readonly List<Subject> _subjects = new();
        private readonly List<Transform> _gone = new();
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
                _subjects.Clear();

                foreach (var npc in FindObjectsByType<NpcIdentity>(FindObjectsSortMode.None))
                {
                    if (npc == null) continue;
                    var id = npc.GetComponent<ArcheCore.Client.World.InteractableIdentity>();
                    _subjects.Add(new Subject(npc.transform, npc.NetworkId, npc.NpcName,
                                              id != null ? id.Title : null,
                                              npc.Health, npc.MaxHealth, npc.MaxHealth > 0));
                }

                // Other players (PvP). Never the local one.
                int localId = CombatClient.LocalPlayerId;
                foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                {
                    if (pc == null || pc.isLocalPlayer || pc.networkId == localId) continue;
                    _subjects.Add(new Subject(pc.transform, pc.networkId,
                                              string.IsNullOrEmpty(pc.playerName) ? "Player" : pc.playerName,
                                              null, pc.health, pc.maxHealth, false));
                }
            }

            var cam = Camera.main;
            if (cam == null) return;
            Vector3 camPos = cam.transform.position;

            // Retire plates whose subject is gone.
            _gone.Clear();
            foreach (var kv in _plates) if (kv.Key == null) _gone.Add(kv.Key);
            foreach (var dead in _gone) Release(dead);

            foreach (var subject in _subjects)
            {
                if (subject.Transform == null) continue;

                Vector3 head = subject.Transform.position + Vector3.up * heightAboveFeet;
                Vector3 screen = cam.WorldToScreenPoint(head);
                bool visible = screen.z > 0f && Vector3.Distance(camPos, head) <= maxDistance;

                if (!visible) { if (_plates.ContainsKey(subject.Transform)) Release(subject.Transform); continue; }

                if (!_plates.TryGetValue(subject.Transform, out var plate))
                {
                    plate = Acquire();
                    _plates[subject.Transform] = plate;
                }

                Draw(plate, subject);

                if (RuntimeUI.ScreenToLocal(_layer, screen, out var local))
                    plate.Root.anchoredPosition = local;
            }
        }

        private void Draw(Plate p, Subject subject)
        {
            bool selected = CombatClient.TargetId == subject.NetworkId;
            bool showBar = subject.MaxHealth > 0;

            Color c = subject.Hostile ? RuntimeUI.Hostile
                    : subject.Title != null || !showBar ? RuntimeUI.Friendly
                    : RuntimeUI.Text;   // another player
            if (selected) c = Color.Lerp(c, Color.white, 0.35f);
            p.Name.color = c;

            p.Name.text = string.IsNullOrEmpty(subject.Title)
                ? subject.Name
                : $"<size=80%>&lt;{subject.Title}&gt;</size>\n{subject.Name}";

            p.BarBack.gameObject.SetActive(showBar);
            if (showBar)
            {
                float pct = Mathf.Clamp01((float)subject.Health / subject.MaxHealth);
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

        private void Release(Transform subject)
        {
            if (!_plates.TryGetValue(subject, out var plate)) return;
            _plates.Remove(subject);
            plate.Root.gameObject.SetActive(false);
            _pool.Push(plate);
        }
    }
}
