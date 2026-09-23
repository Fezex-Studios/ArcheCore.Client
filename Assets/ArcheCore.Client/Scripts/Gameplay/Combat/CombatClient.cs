using System;
using ArchCore.Client;
using ArcheCore.Client.UI;
using ArcheCore.Network.Shared.Packets.W2C;
using UnityEngine;

namespace ArcheCore.Client.Gameplay.Combat
{
    /// <summary>
    /// Client-side combat state: who you're targeting, whether your attack is
    /// cooling down, and what to do with incoming W2CCombatEvents. Static,
    /// like LocalCharacterState - no scene object needed.
    ///
    /// Nothing here decides a hit. The server rolls every point of damage;
    /// this only DISPLAYS what it was told and remembers the cooldown the
    /// server reported, so the attack key doesn't send presses the server
    /// would drop.
    /// </summary>
    public static class CombatClient
    {
        /// <summary>The one skill (Skills.Id 1, "Strike"). Roadmap H.</summary>
        public const int DefaultSkillId = 1;

        public static int TargetId { get; private set; }

        /// <summary>Target changed (0 = no target).</summary>
        public static event Action<int> OnTargetChanged;

        /// <summary>Any hit on anything you can see.</summary>
        public static event Action<W2CCombatEventPacket> OnCombatEvent;

        private static double _readyAt;
        private static double _cooldownLength;

        private static PlayerController _localPlayer;

        public static bool IsOnCooldown => Time.realtimeSinceStartupAsDouble < _readyAt;

        /// <summary>1 -> 0 over the cooldown, for a sweep on the attack key.</summary>
        public static float CooldownRemaining01 =>
            IsOnCooldown && _cooldownLength > 0
                ? (float)((_readyAt - Time.realtimeSinceStartupAsDouble) / _cooldownLength)
                : 0f;

        /// <summary>The local player's network id, or 0 before they've spawned.</summary>
        public static int LocalPlayerId
        {
            get
            {
                if (_localPlayer == null || !_localPlayer.isLocalPlayer)
                {
                    _localPlayer = null;
                    foreach (var pc in UnityEngine.Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                    {
                        if (pc.isLocalPlayer) { _localPlayer = pc; break; }
                    }
                }

                return _localPlayer != null ? _localPlayer.networkId : 0;
            }
        }

        public static void SetTarget(int networkId)
        {
            if (TargetId == networkId)
                return;

            TargetId = networkId;
            OnTargetChanged?.Invoke(networkId);
        }

        /// <summary>Drop the target if it's this entity (it died or left view).</summary>
        public static void ClearTargetIf(int networkId)
        {
            if (TargetId == networkId)
                SetTarget(0);
        }

        /// <summary>W2CCombatEvent - already on the main thread with the world loaded.</summary>
        public static void HandleCombatEvent(W2CCombatEventPacket p)
        {
            Vector3? where = null;
            int localId = LocalPlayerId;
            bool hitMe = p.TargetId == localId;

            if (NpcRegistry.Instance != null && NpcRegistry.Instance.TryGetNpc(p.TargetId, out var npc) && npc != null)
            {
                npc.Health = p.TargetHealth;
                npc.MaxHealth = p.TargetMaxHealth;
                where = npc.transform.position;
            }
            else if (PlayerRegistry.Instance != null &&
                     PlayerRegistry.Instance.TryGetPlayer(p.TargetId, out var hitPlayer) && hitPlayer != null)
            {
                // A player took the hit - an NPC fighting back, or another
                // player in PvP.
                where = hitPlayer.transform.position;
                hitPlayer.health = p.TargetHealth;
                hitPlayer.maxHealth = p.TargetMaxHealth;

                // W2CHealthUpdate carries this too, but applying it here keeps
                // the HUD bar exactly in step with the damage number.
                if (hitMe)
                    ArcheCore.Client.UI.State.LocalCharacterState.ApplyHealth(p.TargetHealth, p.TargetMaxHealth);
            }

            bool mine = p.AttackerId == localId;
            if (mine && p.CooldownMs > 0)
            {
                _cooldownLength = p.CooldownMs / 1000.0;
                _readyAt = Time.realtimeSinceStartupAsDouble + _cooldownLength;
            }

            if (where.HasValue)
                DamageNumbersUI.Spawn(where.Value, p.Damage, mine, hitMe);

            OnCombatEvent?.Invoke(p);

            if (p.Killed)
                ClearTargetIf(p.TargetId);
        }

        /// <summary>On disconnect - nothing from the last session should carry over.</summary>
        public static void Reset()
        {
            SetTarget(0);
            _readyAt = 0;
            _localPlayer = null;
        }
    }
}
