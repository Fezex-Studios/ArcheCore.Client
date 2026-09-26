using ArchCore.Client;
using ArcheCore.Client.Movement;
using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2WSenders;
using ArcheCore.Client.UI;
using ArcheCore.Client.UI.State;
using ArcheCore.Client.Gameplay.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace ArcheCore.Client.Gameplay.Combat
{
    /// <summary>
    /// Combat input. Creates itself - no scene setup.
    ///
    ///   Left-click an enemy  - target it (the same click that interacts)
    ///   Skill keys (1-5)     - use that skill-bar skill: an enemy skill on
    ///                          the enemy under the cursor, or your current
    ///                          target; a self skill (Mend, Battle Shout) on you
    ///   C                    - the character window
    ///
    /// Ignored while typing in chat, while on cooldown (the server would drop
    /// it anyway), and before entering the world. Also creates the target
    /// frame the first time you pick a target.
    /// </summary>
    public class CombatController : MonoBehaviour
    {
        // Skill keys are GameHotkeys.SkillSlot(1..5) (rebindable, default 1-5).

        public static CombatController Instance { get; private set; }

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

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

            // Target whatever you click on: enemies, friendly NPCs, and other
            // players (PvP). The frame shows all three; the server decides
            // what may actually be attacked.
            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                int hovered = HoveredTarget();
                if (hovered != 0)
                    CombatClient.SetTarget(hovered);
            }

            // Target walked out of view or died - drop it.
            if (CombatClient.TargetId != 0 && !IsLiveTarget(CombatClient.TargetId))
                CombatClient.SetTarget(0);

            if (CombatClient.TargetId != 0)
                TargetFrameUI.EnsureInstance();

            // Phase 3 HUD: the skill bar (with mana) and your buffs.
            SkillBarUI.EnsureInstance();
            PlayerStatusRowUI.EnsureInstance();

            if (WorldUIManager.IsTypingInField)
                return;

            for (int slot = 1; slot <= GameHotkeys.SkillSlotCount; slot++)
            {
                if (GameHotkeys.Pressed(GameHotkeys.SkillSlot(slot)))
                {
                    UseSkillSlot(slot);
                    break;
                }
            }

            if (GameHotkeys.Pressed(GameHotkeys.ToggleCharacterWindow))
                CharacterWindowUI.Toggle();
        }

        /// <summary>Key 1-5 or a click on the skill bar.</summary>
        public void UseSkillSlot(int slot)
        {
            var skill = SkillBook.BySlot(slot);

            // Before the catalogue arrives (or from an older server), key 1
            // is still Strike.
            int skillId = skill?.Id ?? (slot == 1 ? CombatClient.DefaultSkillId : 0);
            if (skillId == 0)
                return;

            if (skill != null && skill.TargetRule == 1)
            {
                UseOnSelf(skill);
                return;
            }

            TryAttack(skillId, skill);
        }

        private void UseOnSelf(ArcheCore.Network.Shared.Packets.W2C.SkillData skill)
        {
            if (SkillBook.IsOnCooldown(skill.Id))
                return;

            if (skill.ManaCost > LocalCharacterState.Mana && LocalCharacterState.MaxMana > 0)
            {
                HudMessageDisplay.QueueOrShow("Not enough mana.");
                return;
            }

            var peer = ClientNetwork.Instance?.ServerPeer;
            if (peer != null)
                C2WAttackPacketSender.Send(peer, 0, skill.Id);
        }

        private void TryAttack(int skillId, ArcheCore.Network.Shared.Packets.W2C.SkillData skill)
        {
            int target = HoveredAttackableNpc();
            if (target == 0 && IsOtherPlayer(HoveredTarget()))
                target = HoveredTarget();
            if (target == 0)
                target = CombatClient.TargetId;

            if (target == 0)
            {
                HudMessageDisplay.QueueOrShow("You have no target.");
                return;
            }

            CombatClient.SetTarget(target);

            // Players are attackable as far as the client is concerned - PvP
            // rules and safe zones are the server's call, and it answers with
            // a message if the answer is no.
            if (!IsAttackableNpc(target) && !IsOtherPlayer(target))
            {
                HudMessageDisplay.QueueOrShow("You can't attack that.");
                return;
            }

            if (SkillBook.IsOnCooldown(skillId) || (skillId == CombatClient.DefaultSkillId && CombatClient.IsOnCooldown))
                return;

            if (skill != null && skill.ManaCost > LocalCharacterState.Mana && LocalCharacterState.MaxMana > 0)
            {
                HudMessageDisplay.QueueOrShow("Not enough mana.");
                return;
            }

            var peer = ClientNetwork.Instance?.ServerPeer;
            if (peer == null)
                return;

            C2WAttackPacketSender.Send(peer, target, skillId);
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

        private int HoveredTarget()
        {
            var focus = _interaction != null ? _interaction.CurrentFocus : null;
            if (focus != null && (IsLiveNpc(focus.NetworkId) || IsOtherPlayer(focus.NetworkId)))
                return focus.NetworkId;

            // Players carry no InteractableIdentity and aren't on the
            // interact layer, so PlayerInteraction never sees them - they
            // need a raycast of their own.
            return HoveredPlayer();
        }

        private static int HoveredPlayer()
        {
            var cam = Camera.main;
            var mouse = Mouse.current;
            if (cam == null || mouse == null) return 0;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return 0;

            if (!Physics.Raycast(cam.ScreenPointToRay(mouse.position.ReadValue()), out var hit, 100f))
                return 0;

            var pc = hit.collider.GetComponentInParent<ArchCore.Client.PlayerController>();
            return pc != null && pc.networkId != CombatClient.LocalPlayerId ? pc.networkId : 0;
        }

        private static bool IsOtherPlayer(int networkId) =>
            networkId != 0 &&
            networkId != CombatClient.LocalPlayerId &&
            PlayerRegistry.Instance != null &&
            PlayerRegistry.Instance.TryGetPlayer(networkId, out var pc) && pc != null;

        private static bool IsLiveTarget(int networkId) => IsLiveNpc(networkId) || IsOtherPlayer(networkId);

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
