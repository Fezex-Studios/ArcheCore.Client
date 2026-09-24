using System.Collections.Generic;
using ArchCore.Client;
using ArcheCore.Client.Gameplay;
using ArcheCore.Client.Movement;
using ArcheCore.Client.World;
using UnityEngine;

namespace ArcheCore.Client.Gameplay.Mounts
{
    /// <summary>
    /// Draws mounts and makes the local rider move faster. Static, like the
    /// other registries - no scene object.
    ///
    /// A mount is a model parented to the rider, not a separate entity: the
    /// player keeps moving itself, and the mount just goes where they go.
    /// That's the whole point of doing mounts this way (roadmap N) - no
    /// second entity, no vehicle physics, nothing new in the interest grid.
    ///
    /// Speed is applied to the LOCAL player's movement profile only. Remote
    /// riders don't need it: their positions come from the server, so they
    /// already move at whatever speed they're actually going.
    /// </summary>
    public static class MountVisuals
    {
        private static readonly Dictionary<int, GameObject> Models = new();

        // The profile's own numbers, kept so dismounting restores exactly
        // what was there rather than a guess.
        private static float _baseRun, _baseSprint, _baseWalk;
        private static bool _baseCaptured;

        public static void Apply(int playerNetworkId, string modelType, float speedMultiplier)
        {
            if (PlayerRegistry.Instance == null ||
                !PlayerRegistry.Instance.TryGetPlayer(playerNetworkId, out var player) || player == null)
                return;

            Remove(playerNetworkId);

            bool mounting = !string.IsNullOrEmpty(modelType);

            if (mounting)
            {
                var prefabs = WorldObjectPrefabRegistry.Instance;
                var prefab = prefabs != null ? prefabs.GetPrefab(modelType) : null;

                if (prefab != null)
                {
                    var model = Object.Instantiate(prefab, player.transform);
                    model.transform.localPosition = Vector3.zero;
                    model.transform.localRotation = Quaternion.identity;
                    model.name = $"Mount_{playerNetworkId}";

                    // A mount is scenery: it must never block the interact
                    // raycast or get in the way of clicking the rider.
                    foreach (var collider in model.GetComponentsInChildren<Collider>())
                        collider.enabled = false;

                    Models[playerNetworkId] = model;
                }
                // No prefab is only a missing model - the ride still works,
                // and GetPrefab has already said which ModelType is missing.
            }

            if (player.isLocalPlayer)
                ApplySpeed(player, mounting ? speedMultiplier : 1f);
        }

        public static void Remove(int playerNetworkId)
        {
            if (!Models.TryGetValue(playerNetworkId, out var model))
                return;

            Models.Remove(playerNetworkId);
            if (model != null)
                Object.Destroy(model);
        }

        /// <summary>
        /// Multiplies the local player's movement speeds. The server allows
        /// exactly this much extra, so the two agree; going faster than this
        /// is what the speed check exists to catch.
        /// </summary>
        private static void ApplySpeed(PlayerController player, float multiplier)
        {
            var motor = player.GetComponent<LocalCharacterMotor>();
            var profile = motor != null ? motor.Profile : null;
            if (profile == null)
                return;

            if (!_baseCaptured)
            {
                _baseWalk = profile.WalkSpeed;
                _baseRun = profile.RunSpeed;
                _baseSprint = profile.SprintSpeed;
                _baseCaptured = true;
            }

            profile.WalkSpeed = _baseWalk * multiplier;
            profile.RunSpeed = _baseRun * multiplier;
            profile.SprintSpeed = _baseSprint * multiplier;
        }
    }
}
