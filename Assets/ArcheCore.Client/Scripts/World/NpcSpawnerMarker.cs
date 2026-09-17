// NpcSpawnerMarker.cs
// Assets/ArcheCore.Client/Scripts/World/
//
// DELIBERATELY LEFT IN THE GLOBAL NAMESPACE. The original version had no
// namespace, and Unity serializes component references by type name —
// moving this into ArcheCore.Client.World would silently break the
// component link on every NpcSpawnerMarker already placed in a saved
// scene, turning them into "Missing (Mono Script)". Not worth it for
// tidiness.
//
// WHAT CHANGED: added DbId. The original marker was write-only — you
// placed it, exported SQL, and the scene object had no idea whether the
// row it created still existed, had moved, or had been edited elsewhere.
// DbId is what makes the round trip possible: markers loaded from
// worldserver.db carry their row id, so the World Map window can tell
// "this is row 42, moved 3 units" apart from "this is a brand new
// spawner". DbId == 0 means "not in the database yet".

using UnityEngine;

public class NpcSpawnerMarker : MonoBehaviour
{
    /// <summary>
    /// Primary key of the matching NpcSpawners row, or 0 if this marker
    /// has never been written to the database. Set automatically on load
    /// and stamped in after a successful insert — you should not normally
    /// edit this by hand, which is why the custom inspector shows it
    /// read-only.
    /// </summary>
    [HideInInspector] public int DbId;

    [Header("NPC Data")]
    public int    TemplateId = 1;
    public string NpcName    = "Orc Grunt";   // display only; resolved from NpcTemplates on load
    public int    Count      = 1;
    public float  Radius     = 3.0f;

    /// <summary>
    /// Position/values as last synced with the database. Used to detect
    /// drift without a DB round trip every repaint — comparing against
    /// live DB state on every OnGUI would hammer SQLite from the editor
    /// loop. Meaningless when DbId == 0.
    /// </summary>
    [HideInInspector] public Vector3 SyncedPosition;
    [HideInInspector] public int     SyncedTemplateId;
    [HideInInspector] public int     SyncedCount;
    [HideInInspector] public float   SyncedRadius;

    public bool IsInDatabase => DbId != 0;

    public bool HasLocalChanges =>
        IsInDatabase && (
            SyncedPosition   != transform.position ||
            SyncedTemplateId != TemplateId ||
            SyncedCount      != Count ||
            !Mathf.Approximately(SyncedRadius, Radius));

    public void MarkSynced()
    {
        SyncedPosition   = transform.position;
        SyncedTemplateId = TemplateId;
        SyncedCount      = Count;
        SyncedRadius     = Radius;
    }

    private void OnDrawGizmos()
    {
        // Colour encodes sync state, so a glance at the scene tells you
        // what's actually committed: grey = new/unsaved, amber = edited
        // since load, red = matches the database.
        Color baseColor =
            !IsInDatabase    ? new Color(0.6f, 0.6f, 0.6f) :
            HasLocalChanges  ? new Color(1f, 0.75f, 0.2f)  :
                               new Color(1f, 0.3f, 0.3f);

        Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.25f);
        Gizmos.DrawSphere(transform.position, Radius);

        Gizmos.color = baseColor;
        Gizmos.DrawSphere(transform.position, 0.3f);
        Gizmos.DrawWireSphere(transform.position, Radius);

#if UNITY_EDITOR
        string status =
            !IsInDatabase   ? "  [NEW]" :
            HasLocalChanges ? "  [EDITED]" : "";

        UnityEditor.Handles.Label(
            transform.position + Vector3.up * (Radius + 0.5f),
            $"{NpcName} x{Count}{status}");
#endif
    }
}