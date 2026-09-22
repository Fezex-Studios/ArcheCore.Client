using ArchCore.Client;
using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2WSenders;
using ArcheCore.Client.UI;
using ArcheCore.Client.UI.State;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcheCore.Client.Gameplay.Combat
{
    /// <summary>
    /// Combat input. Creates itself - no scene setup.
    ///
    ///   Left-click an enemy  - target it (the same click that interacts)
    ///   Attack key (1)       - attack the enemy under the cursor, or your
    ///                          current target if you're not hovering one
    ///
    /// Ignored while typing in chat, while on cooldown (the server would drop
    /// it anyway), and before entering the world. Also creates the target
    /// frame the first time you pick a target.
    /// </summary>
    public class CombatController : MonoBehaviour
    {
        [SerializeField] private Key attackKey = Key.Digit1;

        private PlayerInteraction _interaction;
        private float _nextSearch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<CombatController>() != null)
                return;

            var go = new GameObject("CombatController");
            DontDestroyOnLoad(go);
            go.AddComponent<CombatController>();
        }

        private void Update()
        {
            if (!LocalCharacterState.HasEnteredWorld)
                return;

            FindInteraction();

            // Target whatever NPC you click on - enemies and friendly NPCs alike
            // (the frame shows both; only enemies can be attacked).
            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                int hovered = HoveredNpc();
                if (hovered != 0)
                    CombatClient.SetTarget(hovered);
            }

            // Target walked out of view or died - drop it.
            if (CombatClient.TargetId != 0 && !IsLiveNpc(CombatClient.TargetId))
                CombatClient.SetTarget(0);

            if (CombatClient.TargetId != 0)
                TargetFrameUI.EnsureInstance();

            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[attackKey].wasPressedThisFrame || WorldUIManager.IsTypingInField)
                return;

            TryAttack();
        }

        private void TryAttack()
        {
            int target = HoveredAttackableNpc();
            if (target == 0)
                target = CombatClient.TargetId;

            if (target == 0)
            {
                HudMessageDisplay.QueueOrShow("You have no target.");
                return;
            }

            CombatClient.SetTarget(target);

            if (!IsAttackableNpc(target))
            {
                HudMessageDisplay.QueueOrShow("You can't attack that.");
                return;
            }

            if (CombatClient.IsOnCooldown)
                return;

            var peer = ClientNetwork.Instance?.ServerPeer;
            if (peer == null)
                return;

            C2WAttackPacketSender.Send(peer, target, CombatClient.DefaultSkillId);
        }

        private void FindInteraction()
        {
            if (_interaction != null || Time.unscaledTime < _nextSearch)
                return;

            _nextSearch = Time.unscaledTime + 1f;
            _interaction = FindFirstObjectByType<PlayerInteraction>();   // only the local player has one
        }

        private int HoveredAttackableNpc()
        {
            var focus = _interaction != null ? _interaction.CurrentFocus : null;
            return focus != null && IsAttackableNpc(focus.NetworkId) ? focus.NetworkId : 0;
        }

        private int HoveredNpc()
        {
            var focus = _interaction != null ? _interaction.CurrentFocus : null;
            return focus != null && IsLiveNpc(focus.NetworkId) ? focus.NetworkId : 0;
        }

        private static bool IsLiveNpc(int networkId) =>
            NpcRegistry.Instance != null &&
            NpcRegistry.Instance.TryGetNpc(networkId, out var npc) &&
            npc != null && (npc.MaxHealth == 0 || npc.Health > 0);

        private static bool IsAttackableNpc(int networkId) =>
            NpcRegistry.Instance != null &&
            NpcRegistry.Instance.TryGetNpc(networkId, out var npc) &&
            npc != null && npc.MaxHealth > 0 && npc.Health > 0;
    }
}
