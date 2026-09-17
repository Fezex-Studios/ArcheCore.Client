using System;
using System.Collections;
using System.Collections.Generic;
using ArcheCore.Client.Gameplay;
using ArcheCore.Client.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcheCore.Client.Networking
{
    /// <summary>
    /// The ONE place that loads main_world, plus an ordered queue of
    /// "world" packet work that must wait until the scene is ready.
    ///
    /// Why this exists: the server sends SpawnPlayer (self), then SpawnPlayer
    /// for every nearby player, then SpawnNpc for nearby NPCs, all in the same
    /// burst. Before this class, every one of those handlers started its own
    /// coroutine, and each one saw "active scene != main_world" and called
    /// LoadSceneAsync again - several overlapping loads, with PlayerRegistry
    /// missing or replaced while spawns were being applied. Nearby players
    /// silently never appeared.
    ///
    /// Rules:
    ///  - Any handler that touches world objects (spawn/despawn of players or
    ///    NPCs) calls RunWhenReady instead of acting directly.
    ///  - Work runs in the exact order the packets arrived, so a despawn that
    ///    arrives after its spawn can never run first.
    ///  - Once the world is ready and the queue is empty, work runs immediately.
    /// </summary>
    public static class WorldLoader
    {
        public const string WorldSceneName = "main_world";

        // How long to wait for scene singletons before running queued work anyway.
        private const float RegistryWaitTimeoutSeconds = 10f;

        private static readonly Queue<Action> Pending = new();
        private static bool _loading;

        // Set when a disconnect happens while main_world is still loading: the
        // in-flight load can't be cancelled, so we switch scenes when it ends.
        private static string _returnScene;

        /// <summary>True when main_world is active, its registries exist, and nothing is waiting.</summary>
        public static bool IsReady =>
            !_loading &&
            SceneManager.GetActiveScene().name == WorldSceneName &&
            PlayerRegistry.Instance != null;

        /// <summary>
        /// Runs the action now if the world is ready and nothing is queued ahead of
        /// it; otherwise queues it (preserving arrival order) and makes sure the
        /// scene is loading.
        /// </summary>
        public static void RunWhenReady(Action action)
        {
            if (action == null)
                return;

            if (IsReady && Pending.Count == 0)
            {
                Run(action);
                return;
            }

            Pending.Enqueue(action);
            EnsureLoading();
        }

        /// <summary>
        /// Drops queued work from a previous connection. Call when starting a new
        /// connection. A load already in progress is left to finish.
        /// </summary>
        public static void ClearPending() => Pending.Clear();

        /// <summary>
        /// Leaves the world (e.g. after a disconnect): drops queued world work
        /// and loads <paramref name="sceneName"/>. If main_world is still
        /// loading, the switch happens as soon as that load finishes.
        /// </summary>
        public static void ReturnToScene(string sceneName)
        {
            Pending.Clear();

            if (_loading)
            {
                _returnScene = sceneName;
                return;
            }

            if (SceneManager.GetActiveScene().name != sceneName)
                SceneManager.LoadScene(sceneName);
        }

        private static void EnsureLoading()
        {
            if (_loading)
                return;

            if (ClientNetwork.Instance == null)
            {
                Debug.LogError("[WorldLoader] ClientNetwork.Instance is null - cannot start world load.");
                return;
            }

            _loading = true;
            _returnScene = null;
            ClientNetwork.Instance.StartCoroutine(LoadAndFlush());
        }

        private static IEnumerator LoadAndFlush()
        {
            if (SceneManager.GetActiveScene().name != WorldSceneName)
            {
                Debug.Log($"[WorldLoader] Loading '{WorldSceneName}'...");
                AsyncOperation op = SceneManager.LoadSceneAsync(WorldSceneName);

                if (op == null)
                {
                    Debug.LogError($"[WorldLoader] Could not load '{WorldSceneName}'. Is it in Build Settings?");
                    _loading = false;
                    yield break;
                }

                while (!op.isDone)
                    yield return null;
            }

            if (TryHandleReturn())
                yield break;

            // Scene singletons set Instance in Awake. Unity's == null is also true
            // for destroyed objects, so a stale Instance from an old scene won't pass.
            float waited = 0f;
            while ((PlayerRegistry.Instance == null || WorldObjectPrefabRegistry.Instance == null)
                   && waited < RegistryWaitTimeoutSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (TryHandleReturn())
                yield break;

            if (PlayerRegistry.Instance == null)
                Debug.LogError("[WorldLoader] PlayerRegistry not found in main_world - players cannot spawn.");
            if (WorldObjectPrefabRegistry.Instance == null)
                Debug.LogError("[WorldLoader] WorldObjectPrefabRegistry not found in main_world - NPCs cannot spawn.");

            _loading = false;

            Debug.Log($"[WorldLoader] World ready - applying {Pending.Count} queued action(s).");

            // Anything queued while we drain goes to the back and is drained here too.
            while (Pending.Count > 0)
                Run(Pending.Dequeue());
        }

        /// <summary>A disconnect arrived during the load - go there instead of flushing.</summary>
        private static bool TryHandleReturn()
        {
            if (_returnScene == null)
                return false;

            string scene = _returnScene;
            _returnScene = null;
            _loading = false;
            Pending.Clear();

            SceneManager.LoadScene(scene);
            return true;
        }

        private static void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
