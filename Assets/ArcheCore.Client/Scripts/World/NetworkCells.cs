using UnityEngine;

namespace ArcheCore.Client.World
{
    /// <summary>
    /// Editor-only visualisation of the server's interest radius.
    ///
    /// This used to draw a flat green 150x150 square, because that was
    /// genuinely what the server used: interest was the 3x3 block of cells
    /// around the one you stood in, anchored to the grid rather than to
    /// you. The square jumping as you crossed a boundary was not a
    /// rendering artifact, it was the real AOI being recomputed.
    ///
    /// The server now filters by actual distance, so this draws what it
    /// actually does: two circles centred on the player.
    ///
    ///   GREEN  - SpawnRadius. Something crossing inward becomes visible.
    ///   AMBER  - DespawnRadius. Something crossing outward disappears.
    ///
    /// The gap between them is deliberate. Without it, standing exactly on
    /// the boundary would spawn and despawn entities on alternating ticks.
    ///
    /// KEEP THESE IN SYNC WITH THE SERVER. They're duplicated constants,
    /// not anything the server sends, so if you retune InterestManager and
    /// forget this file, the gizmo quietly starts lying to you. Sending the
    /// real values down at spawn time would be the proper fix.
    ///
    /// Gizmos are Editor-only and never run in a build. They're also off by
    /// default in the Game view - toggle "Gizmos" in its toolbar if you
    /// want to see this while playing rather than only in the Scene view.
    /// </summary>
    public class NetworkCells : MonoBehaviour
    {
        [Header("Must match InterestManager on the server")]
        [SerializeField] private float spawnRadius = 75f;
        [SerializeField] private float despawnRadius = 85f;

        [Header("Broad phase (debug only - not view distance)")]
        [SerializeField] private bool showCellGrid = true;
        [SerializeField] private float cellSize = 50f;
        [SerializeField] private float gridExtent = 500f;

        private const int CircleSegments = 64;

        private void OnDrawGizmos()
        {
            Vector3 pos = transform.position;

            // The two interest radii. Drawn on the ground plane rather than
            // as wire spheres because interest is decided in 2D - the
            // server's grid indexes X and Z and ignores Y entirely, so a
            // sphere would imply a vertical limit that doesn't exist.
            Gizmos.color = new Color(0f, 1f, 0.4f, 0.9f);
            DrawGroundCircle(pos, spawnRadius);

            Gizmos.color = new Color(1f, 0.7f, 0f, 0.5f);
            DrawGroundCircle(pos, despawnRadius);

            if (!showCellGrid)
                return;

            // The broad-phase grid, for debugging the spatial index itself.
            // These lines no longer bound anything you can see - they're
            // just where the server's dictionary buckets happen to fall.
            Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.25f);

            float originX = Mathf.Floor(pos.x / cellSize) * cellSize;
            float originZ = Mathf.Floor(pos.z / cellSize) * cellSize;

            for (float x = originX - gridExtent; x <= originX + gridExtent; x += cellSize)
            {
                Gizmos.DrawLine(
                    new Vector3(x, pos.y, originZ - gridExtent),
                    new Vector3(x, pos.y, originZ + gridExtent));
            }

            for (float z = originZ - gridExtent; z <= originZ + gridExtent; z += cellSize)
            {
                Gizmos.DrawLine(
                    new Vector3(originX - gridExtent, pos.y, z),
                    new Vector3(originX + gridExtent, pos.y, z));
            }
        }

        private static void DrawGroundCircle(Vector3 center, float radius)
        {
            Vector3 previous = center + new Vector3(radius, 0f, 0f);

            for (int i = 1; i <= CircleSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / CircleSegments;

                Vector3 next = center + new Vector3(
                    Mathf.Cos(angle) * radius,
                    0f,
                    Mathf.Sin(angle) * radius);

                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}