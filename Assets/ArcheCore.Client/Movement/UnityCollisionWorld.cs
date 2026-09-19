using ArcheCore.Movement;
using UnityEngine;
using SVector3 = System.Numerics.Vector3;

namespace ArcheCore.Client.Movement
{
    /// <summary>
    /// ICollisionWorld over Unity's PhysX. The only file in the client that
    /// knows both the movement core and the engine, which is where that
    /// knowledge should be concentrated.
    ///
    /// SELF-COLLISION IS FILTERED HERE, NOT BY CONVENTION.
    ///
    /// Every query ignores colliders parented under the owning character.
    /// An earlier version relied on the project simply not having any, which
    /// is not a rule that survives contact with a real game - weapons,
    /// mounts, hit volumes, attachment points and stray test objects all end
    /// up as children sooner or later. And the failure is baffling rather
    /// than obvious: the downward ground probe hits the character's OWN
    /// child collider, reports grounded, and since that "ground" moves with
    /// the character it never falls. What you see is a character that jumps
    /// once and hangs in the air, which looks like a gravity bug and is not.
    ///
    /// Filtering costs a NonAlloc query plus a nearest-hit scan instead of a
    /// single cast. Worth it - a layer-based scheme would work too, but it
    /// relies on whoever builds the next prefab knowing the convention.
    ///
    /// Note the two Vector3 types: the core uses System.Numerics.Vector3 (no
    /// engine dependency), Unity uses its own. Both are three floats,
    /// conversion is free, and the aliases keep them from being confused.
    /// </summary>
    public sealed class UnityCollisionWorld : ICollisionWorld
    {
        private const int MaxHits = 16;

        private readonly LayerMask _collisionMask;
        private readonly LayerMask _waterMask;
        private readonly Transform _owner;

        private readonly RaycastHit[] _hits = new RaycastHit[MaxHits];
        private readonly Collider[] _overlaps = new Collider[MaxHits];

        /// <param name="owner">
        /// The character's root. Colliders under it are ignored by every
        /// query. Pass null only for something with no colliders of its own.
        /// </param>
        public UnityCollisionWorld(LayerMask collisionMask, LayerMask waterMask, Transform owner)
        {
            _collisionMask = collisionMask;
            _waterMask = waterMask;
            _owner = owner;
        }

        private bool IsOwn(Collider c) =>
            c == null || (_owner != null && c.transform.IsChildOf(_owner));

        public bool CapsuleCast(
            SVector3 center, float radius, float segment,
            SVector3 direction, float distance, out CollisionHit hit)
        {
            hit = default;

            Vector3 c = ToUnity(center);
            Vector3 d = ToUnity(direction);
            Vector3 offset = Vector3.up * segment;

            int count = Physics.CapsuleCastNonAlloc(
                c - offset, c + offset, radius, d, _hits,
                distance, _collisionMask, QueryTriggerInteraction.Ignore);

            int best = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                if (IsOwn(_hits[i].collider)) continue;

                // A zero distance means the sweep STARTED inside this
                // collider. PhysX reports a zero normal for that case, which
                // would make the motor slide along a meaningless plane, so
                // leave it to ResolveOverlap instead.
                if (_hits[i].distance <= 0f) continue;

                if (_hits[i].distance < bestDistance)
                {
                    bestDistance = _hits[i].distance;
                    best = i;
                }
            }

            if (best < 0) return false;

            ref RaycastHit rh = ref _hits[best];

            hit.Point = ToNumerics(rh.point);
            hit.Normal = ToNumerics(rh.normal);
            hit.Distance = rh.distance;

            // Zero for static world geometry. A non-zero id is what promotes
            // standing-on into riding a reference frame, so only things that
            // actually move should carry one.
            var identity = rh.collider.GetComponentInParent<INetworkEntity>();
            hit.EntityId = identity?.NetworkId ?? 0;

            return true;
        }

        public bool CheckCapsule(SVector3 center, float radius, float segment)
        {
            Vector3 c = ToUnity(center);
            Vector3 offset = Vector3.up * segment;

            int count = Physics.OverlapCapsuleNonAlloc(
                c - offset, c + offset, radius, _overlaps,
                _collisionMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
                if (!IsOwn(_overlaps[i])) return true;

            return false;
        }

        public bool ComputePenetration(
            SVector3 center, float radius, float segment,
            out SVector3 direction, out float distance)
        {
            direction = SVector3.UnitY;
            distance = 0f;

            Vector3 c = ToUnity(center);
            Vector3 offset = Vector3.up * segment;

            int count = Physics.OverlapCapsuleNonAlloc(
                c - offset, c + offset, radius, _overlaps,
                _collisionMask, QueryTriggerInteraction.Ignore);

            if (count == 0) return false;

            // Physics.ComputePenetration would give a proper separation
            // axis, but it needs a probe collider and allocating one per
            // query is not acceptable at tick rate. This approximates:
            // push straight up by the deepest overlap. Correct for terrain,
            // which is the overwhelmingly common case, and approximate under
            // an overhang. If that ever becomes visible, keep a pooled probe
            // collider rather than making this cleverer.
            float deepest = 0f;

            for (int i = 0; i < count; i++)
            {
                if (IsOwn(_overlaps[i])) continue;

                Vector3 closest = _overlaps[i].ClosestPoint(c);
                float depth = radius - Vector3.Distance(closest, c);
                if (depth > deepest) deepest = depth;
            }

            if (deepest <= 0f) return false;

            direction = SVector3.UnitY;
            distance = deepest;
            return true;
        }

        public float SampleWaterLevel(SVector3 position)
        {
            if (_waterMask.value == 0) return float.MinValue;

            Vector3 from = ToUnity(position) + Vector3.up * 200f;

            if (Physics.Raycast(from, Vector3.down, out RaycastHit rh, 400f,
                    _waterMask, QueryTriggerInteraction.Collide))
            {
                return rh.point.y;
            }

            return float.MinValue;
        }

        public static Vector3 ToUnity(SVector3 v) => new Vector3(v.X, v.Y, v.Z);
        public static SVector3 ToNumerics(Vector3 v) => new SVector3(v.x, v.y, v.z);
    }

    /// <summary>
    /// Implemented by anything a character can stand on that moves - ships,
    /// carts, lifts - so a collision hit can be attributed to an entity and
    /// turned into a reference frame.
    /// </summary>
    public interface INetworkEntity
    {
        int NetworkId { get; }
    }
}