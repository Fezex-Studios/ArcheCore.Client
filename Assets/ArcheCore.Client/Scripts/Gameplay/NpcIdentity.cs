using ArcheCore.Client.Gameplay;
using UnityEngine;

/// <summary>
/// CHANGED: used to run its own copy of the exponential
/// lerp-toward-target that PlayerController.HandleRemoteMovement had, with
/// the same three problems (never arrives, framerate dependent, no idea
/// when the next update is due). Both now share
/// RemoteEntityInterpolator, which does real constant-speed interpolation
/// across the measured update interval and extrapolates through dropped
/// packets.
///
/// This matters more for NPCs than for players, not less. The server's
/// wander AI steps every 300ms while snapshots go out at tick rate, so the
/// gap an observer has to interpolate across is roughly six times what it
/// is for a nearby player, and the old decay-toward-target spent most of
/// that gap visibly easing into each waypoint.
/// </summary>
public class NpcIdentity : MonoBehaviour
{
    public int    NetworkId;
    public int    TemplateId;
    public string NpcName;
    public int    Level;

    private RemoteEntityInterpolator _interpolator;

    private void Awake()
    {
        _interpolator = GetComponent<RemoteEntityInterpolator>();
        if (_interpolator == null)
            _interpolator = gameObject.AddComponent<RemoteEntityInterpolator>();

        // NpcAiManager sends a real heading now (atan2 of the wander
        // direction), so take it off the wire rather than deriving it from
        // the motion vector. Deriving was a workaround for the AI reporting
        // yaw as zero, and it has a blind spot the real value doesn't: at
        // the moment an NPC arrives at a waypoint there is no motion to
        // derive a facing from, so it would hold whatever heading it had
        // last and then swing round only once the next leg started.
        _interpolator.DeriveYawFromMotion = false;
    }

    private void Start()
    {
        _interpolator.Initialize(transform.position, transform.eulerAngles.y);
    }

    /// <summary>
    /// Legacy W2CNpcPosition path - position only, no velocity or facing.
    /// Dead on the movement path now that NPCs ride the world snapshot, but
    /// kept so the opcode still works if anything else uses it.
    /// </summary>
    public void SetTargetPosition(Vector3 position)
    {
        _interpolator.ApplyUpdate(position, Vector3.zero, transform.eulerAngles.y);
    }

    /// <param name="yawDegrees">Facing in degrees (the wire carries radians).</param>
    public void ApplyNetworkState(
        Vector3 position, Vector3 velocity,
        float yawDegrees, float pitchDegrees, float rollDegrees,
        ArcheCore.Network.Shared.MovementState state)
    {
        _interpolator.ApplyUpdate(position, velocity, yawDegrees, pitchDegrees, rollDegrees, state);
    }
}