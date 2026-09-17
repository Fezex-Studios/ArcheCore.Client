// PlayerSpawnPointMarker.cs
// Assets/ArcheCore.Client/Scripts/World/
//
// Scene representation of a row in SpawnPointTables — where players
// actually enter the world. Previously these only existed as DB rows, so
// the only way to know where "Starting Meadow" was involved reading
// coordinates out of a grid and mentally mapping them onto the terrain.
//
// Global namespace for the same reason as NpcSpawnerMarker: so scene
// references survive if this file ever moves.

using UnityEngine;

public class PlayerSpawnPointMarker : MonoBehaviour
{
    /// <summary>Primary key in SpawnPointTables, or 0 if not yet saved.</summary>
    [HideInInspector] public int DbId;

    [Header("Spawn Point")]
    public string SpawnName = "New Spawn Point";

    /// <summary>
    /// Mirrors SpawnPointTables.IsDefault. Exactly one row should have
    /// this set — the World Map window enforces that on write rather than
    /// trusting the scene, since nothing stops you ticking the box on two
    /// markers here.
    /// </summary>
    public bool IsDefault;

    [HideInInspector] public Vector3 SyncedPosition;
    [HideInInspector] public string  SyncedName;
    [HideInInspector] public bool    SyncedIsDefault;

    public bool IsInDatabase => DbId != 0;

    public bool HasLocalChanges =>
        IsInDatabase && (
            SyncedPosition  != transform.position ||
            SyncedName      != SpawnName ||
            SyncedIsDefault != IsDefault);

    public void MarkSynced()
    {
        SyncedPosition  = transform.position;
        SyncedName      = SpawnName;
        SyncedIsDefault = IsDefault;
    }

    private void OnDrawGizmos()
    {
        Color c = IsDefault
            ? new Color(0.3f, 0.9f, 1f)     // default spawn — cyan, stands out
            : new Color(0.4f, 0.7f, 0.9f);

        if (!IsInDatabase)     c = new Color(0.6f, 0.6f, 0.6f);
        else if (HasLocalChanges) c = new Color(1f, 0.75f, 0.2f);

        Gizmos.color = c;

        // A vertical pillar reads far better than a sphere for something
        // you need to spot from across a large terrain at a shallow camera
        // angle — spheres vanish into the ground plane at distance.
        Vector3 top = transform.position + Vector3.up * 6f;
        Gizmos.DrawLine(transform.position, top);
        Gizmos.DrawWireSphere(transform.position, 1.5f);
        Gizmos.DrawSphere(top, 0.5f);

        // Ground ring so the exact landing spot is readable from overhead.
        const int segments = 24;
        Vector3 prev = transform.position + new Vector3(1.5f, 0, 0);
        for (int i = 1; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            Vector3 next = transform.position + new Vector3(Mathf.Cos(a) * 1.5f, 0, Mathf.Sin(a) * 1.5f);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

#if UNITY_EDITOR
        string status =
            !IsInDatabase   ? "  [NEW]" :
            HasLocalChanges ? "  [EDITED]" : "";

        UnityEditor.Handles.Label(
            top + Vector3.up * 1f,
            $"⚑ {SpawnName}{(IsDefault ? "  (DEFAULT)" : "")}{status}");
#endif
    }
}