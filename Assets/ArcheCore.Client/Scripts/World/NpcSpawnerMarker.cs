using UnityEngine;

public class NpcSpawnerMarker : MonoBehaviour
{
    [Header("NPC Data")]
    public int    TemplateId = 1;
    public string NpcName    = "Orc Grunt";   // just for your reference in editor
    public int    Count      = 1;
    public float  Radius     = 3.0f;

    private void OnDrawGizmos()
    {
        // Draw a sphere showing the spawn radius in the scene view
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawSphere(transform.position, Radius);

        // Solid center point
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 1f);
        Gizmos.DrawSphere(transform.position, 0.3f);

        // Label
#if UNITY_EDITOR
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * (Radius + 0.5f),
            $"{NpcName} x{Count}");
#endif
    }
}