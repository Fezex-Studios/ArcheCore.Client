using ArchCore.Client;
using ArcheCore.Client.Movement;
using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2WSenders;
using ArcheCore.Client.UI;
using ArcheCore.Client.UI.State;
using ArcheCore.Client.World;
using ArcheCore.Network.Shared.Packets.W2C;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcheCore.Client.Gameplay.Interaction
{
    /// <summary>
    /// Proximity interaction, ArcheAge-style. Creates itself - no scene setup.
    ///
    /// Each frame it picks ONE target - the thing F and G act on:
    ///   1. what you're hovering, if it's in range and has actions, else
    ///   2. the nearest in-range interactable with actions, preferring what
    ///      the camera is facing.
    /// So you never have to point at or face a vein to mine it - walk up.
    ///
    ///   F / G         - the target's first / second action
    ///   Right-click   - on a hovered, in-range object: its first action
    ///                   (corpses: Obtain loot, which opens the window)
    ///
    /// Every action is a request; the server re-checks range and that the
    /// object really offers it. Disabled actions (Climb) say so locally
    /// instead of sending.
    /// </summary>
    public class InteractionController : MonoBehaviour
    {
        public static InteractableIdentity CurrentTarget { get; private set; }

        // Slot hotkeys are GameHotkeys.InteractPrimary / InteractSecondary
        // (rebindable, default F / G).

        [Tooltip("How much being in front of the camera beats being slightly closer. 0 = nearest wins, always.")]
        [SerializeField] private float facingWeight = 1.5f;

        [Tooltip("A right-click that moves further than this (pixels) is a camera drag, not a click.")]
        [SerializeField] private float clickMaxDrag = 6f;

        private PlayerInteraction _pointer;
        private float _nextSearch;
        private Vector2 _rightDownAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<InteractionController>() != null)
                return;

            var go = new GameObject("InteractionController");
            DontDestroyOnLoad(go);
            go.AddComponent<InteractionController>();
        }

        private void Update()
        {
            if (!LocalCharacterState.HasEnteredWorld || !FindPointer())
            {
                CurrentTarget = null;
                return;
            }

            CurrentTarget = PickTarget();

            if (WorldUIManager.IsTypingInField)
                return;

            if (CurrentTarget != null)
            {
                for (int slot = 0; slot < GameHotkeys.InteractSlotCount; slot++)
                {
                    if (GameHotkeys.Pressed(GameHotkeys.InteractSlot(slot)) && slot < CurrentTarget.Actions.Length)
                    {
                        Perform(CurrentTarget, CurrentTarget.Actions[slot]);
                        break;
                    }
                }
            }

            HandleRightClick();
        }

        // ── Target selection ─────────────────────────────────────────

        private InteractableIdentity PickTarget()
        {
            Vector3 me = _pointer.transform.position;

            var hovered = _pointer.CurrentFocus;
            if (IsUsable(hovered) && InRange(hovered, me))
                return hovered;

            var cam = Camera.main;
            Vector3 facing = cam != null ? Flat(cam.transform.forward) : Vector3.forward;

            InteractableIdentity best = null;
            float bestScore = float.MaxValue;

            var all = InteractableIdentity.All;
            for (int i = 0; i < all.Count; i++)
            {
                var candidate = all[i];
                if (!IsUsable(candidate))
                    continue;

                float distance = Vector3.Distance(me, candidate.transform.position);
                if (distance > candidate.InteractRange)
                    continue;

                // -1 behind the camera .. +1 dead ahead. Being ahead is worth
                // `facingWeight` metres of extra closeness.
                float ahead = Vector3.Dot(facing, Flat(candidate.transform.position - me));
                float score = distance - ahead * facingWeight;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private static bool IsUsable(InteractableIdentity i) =>
            i != null && i.isActiveAndEnabled && i.IsAvailable && i.HasActions;

        public static bool InRange(InteractableIdentity i, Vector3 from) =>
            Vector3.Distance(from, i.transform.position) <= i.InteractRange;

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
        }

        // ── Input ────────────────────────────────────────────────────

        private void HandleRightClick()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;

            if (mouse.rightButton.wasPressedThisFrame)
                _rightDownAt = mouse.position.ReadValue();

            if (!mouse.rightButton.wasReleasedThisFrame)
                return;

            // Right-drag rotates the camera; only a short, still click counts.
            if (Vector2.Distance(_rightDownAt, mouse.position.ReadValue()) > clickMaxDrag)
                return;

            var hovered = _pointer.CurrentFocus;
            if (!IsUsable(hovered))
                return;

            if (!InRange(hovered, _pointer.transform.position))
            {
                HudMessageDisplay.QueueOrShow("Too far away.");
                return;
            }

            // Corpses open the window on right-click, like ArcheAge and WoW.
            var action = hovered.Kind == InteractableKind.Corpse
                ? Find(hovered, InteractionActionType.OpenLoot) ?? hovered.Actions[0]
                : hovered.Actions[0];

            Perform(hovered, action);
        }

        private static InteractionActionData Find(InteractableIdentity target, InteractionActionType type)
        {
            foreach (var a in target.Actions)
                if (a.ActionType == (int)type) return a;
            return null;
        }

        /// <summary>Do one action on one target. Also used by the clickable F/G icons.</summary>
        public static void Perform(InteractableIdentity target, InteractionActionData action)
        {
            if (target == null || action == null)
                return;

            if (!action.IsEnabled)
            {
                HudMessageDisplay.QueueOrShow($"{action.Label} isn't available yet.");
                return;
            }

            var peer = ClientNetwork.Instance?.ServerPeer;
            if (peer == null)
                return;

            C2WInteractPacketSender.Send(peer, target.NetworkId, action.ActionType);
        }

        private bool FindPointer()
        {
            if (_pointer != null)
                return true;

            if (Time.unscaledTime < _nextSearch)
                return false;

            _nextSearch = Time.unscaledTime + 1f;
            _pointer = FindFirstObjectByType<PlayerInteraction>();   // only the local player has one
            return _pointer != null;
        }
    }
}
