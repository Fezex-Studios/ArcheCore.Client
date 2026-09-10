using UnityEngine;

// Same lerp-toward-target pattern as PlayerController.HandleRemoteMovement -
// NPCs now move server-side (NpcAiManager's wander AI), sending unreliable
// position updates every 300ms, so this smooths between updates instead of
// snapping.
public class NpcIdentity : MonoBehaviour
{
    public int    NetworkId;
    public int    TemplateId;
    public string NpcName;
    public int    Level;

    private Vector3 _targetPosition;

    private void Start()
    {
        _targetPosition = transform.position;
    }

    private void Update()
    {
        transform.position = Vector3.Lerp(
            transform.position,
            _targetPosition,
            Time.deltaTime * 10f);
    }

    public void SetTargetPosition(Vector3 position)
    {
        _targetPosition = position;
    }
}