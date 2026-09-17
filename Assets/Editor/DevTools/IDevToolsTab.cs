// IDevToolsTab.cs
// Assets/Editor/DevTools/
//
// The contract every Dev Tools tab implements.
//
// WHY THIS EXISTS
//
// The original ArcheCoreDevTools was one 1,890-line class holding four
// tabs' worth of state as sibling private fields — _npcPatchName next to
// _customDbPath next to _rowSearch, all in one scope, all alive whether or
// not their tab is open. That works at four tabs. It does not work at
// eight: every new tab widens the same class, adding a field means
// scrolling past three unrelated features, and two tabs can accidentally
// share a scroll position or a stale flag because nothing stops them.
//
// Splitting on an interface means a tab owns its own state, its own file,
// and its own lifecycle. Adding one is dropping in a file — the shell
// finds it by reflection, no registration list to update and forget.

using UnityEditor;

namespace ArcheCore.Editor.DevTools
{
    public interface IDevToolsTab
    {
        /// <summary>Label on the tab button. Emoji prefix is the existing house style.</summary>
        string Title { get; }

        /// <summary>
        /// Sort order in the tab bar. Gaps of 10 are deliberate — inserting a
        /// tab between two existing ones shouldn't mean renumbering everything
        /// after it.
        /// </summary>
        int Order { get; }

        /// <summary>
        /// Called once when the window opens, before any draw. Use for cached
        /// loads and event subscriptions, never for anything slow enough to
        /// stall opening the window.
        /// </summary>
        void OnEnable(DevToolsContext ctx);

        /// <summary>Called when the window closes. Unsubscribe here.</summary>
        void OnDisable(DevToolsContext ctx);

        /// <summary>Draws the tab body. Shell handles header, tab bar, and log.</summary>
        void OnGUI(DevToolsContext ctx);

        /// <summary>
        /// Called for the ACTIVE tab only, from SceneView.duringSceneGui.
        /// Scene overlays belong to whichever tab is open — drawing a spawner
        /// overlay while the user is on the SQL Patches tab is noise.
        /// </summary>
        void OnSceneGUI(DevToolsContext ctx, SceneView sceneView) { }

        /// <summary>
        /// True if this tab has unsaved work. The shell uses it to warn on
        /// window close and to put a dot on the tab button, so a tab can't
        /// quietly lose edits just because you clicked away from it.
        /// </summary>
        bool HasUnsavedChanges => false;
    }
}