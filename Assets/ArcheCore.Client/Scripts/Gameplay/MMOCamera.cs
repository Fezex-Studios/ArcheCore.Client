using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace ArcheCore.Client.Gameplay
{
    /// <summary>
    /// Third-person orbit camera in the WoW / ArcheAge idiom.
    ///
    /// THE TWO MOUSE BUTTONS DO DIFFERENT JOBS. This is the thing that makes
    /// a camera feel like an MMO rather than an action game, and getting it
    /// wrong is immediately obvious to anyone who has played one:
    ///
    ///   LEFT DRAG  - free look. The camera orbits; the character does not
    ///                turn. You glance behind you while still running
    ///                forward. The camera keeps that offset afterwards.
    ///
    ///   RIGHT DRAG - steering. Camera and character turn together, and
    ///                A/D become strafe instead of turn (see
    ///                LocalPlayerInput). This is how you actually drive.
    ///
    /// The character's facing is NOT owned here. This class reports its yaw
    /// and whether the right button is held; LocalPlayerInput decides what
    /// the character does with that. Keeping facing in the input layer is
    /// what lets it go into MoveInput.Yaw and travel to the server, which a
    /// camera has no business doing.
    ///
    /// SPLIT ACROSS Update AND LateUpdate, AND THE SPLIT MATTERS.
    ///
    /// Mouse look is read in Update, at execution order -200, so it lands
    /// BEFORE LocalPlayerInput (-100) asks for the yaw. Reading the mouse in
    /// LateUpdate instead - which is what this used to do - meant the
    /// character steered by the PREVIOUS frame's camera angle every frame:
    /// a permanent one-frame lag between mouse and character that reads as
    /// the camera dragging.
    ///
    /// Positioning stays in LateUpdate, because it needs the character's
    /// final position for the frame and LocalCharacterMotor writes its
    /// interpolated render pose during Update. So: input first, character
    /// second, camera placement last.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class MMOCamera : MonoBehaviour
    {
        [Header("Target")]

        /// <summary>
        /// For scenes with no spawn flow - an isolated motor test, a
        /// character-select backdrop. PlayerRegistry calls SetTarget in the
        /// live game and this stays empty.
        /// </summary>
        [SerializeField] private Transform fallbackTarget;

        /// <summary>
        /// Orbit pivot above the target's transform - roughly the
        /// character's head. Measured from the FEET, since that is where
        /// LocalCharacterMotor puts the transform (CharacterController had
        /// it at the waist, so this is not the same number as before).
        /// </summary>
        [SerializeField] private float pivotHeight = 1.6f;

        [Header("Orbit")]

        [SerializeField] private float distance = 6f;
        [SerializeField] private float minDistance = 0f;
        [SerializeField] private float maxDistance = 20f;
        [SerializeField] private float zoomStep = 0.8f;
        [SerializeField] private float zoomDamping = 14f;

        /// <summary>
        /// Degrees of yaw per unit of mouse delta.
        ///
        /// DPI-DEPENDENT BY DESIGN. Mouse delta arrives in pixel-ish units,
        /// so a 1600 DPI mouse produces twice the delta of an 800 DPI one
        /// for the same hand movement. That is how every game in this genre
        /// works and why they all ship a sensitivity slider: players tune
        /// this to their hardware, and "normalising" it would break the
        /// muscle memory they brought with them.
        ///
        /// What must NOT vary is framerate - hence no Time.deltaTime in
        /// HandleDrag. Note also that Windows' "Enhance pointer precision"
        /// applies acceleration to this delta, which makes sensitivity
        /// non-linear with speed. If turning feels inconsistent rather than
        /// simply fast or slow, that setting is the usual culprit and it is
        /// outside the game's control.
        ///
        /// Runtime value lives in CameraSettings so an options screen can
        /// change and persist it; this field is the default.
        /// </summary>
        [SerializeField] private float mouseSensitivity = 0.15f;

        /// <summary>
        /// Vertical sensitivity as a multiple of horizontal. Separate
        /// because pitch is clamped to a much smaller range than yaw, so the
        /// same number covers very different amounts of travel on each axis
        /// and many players want them tuned apart.
        /// </summary>
        [SerializeField] private float verticalSensitivityScale = 1f;

        [SerializeField] private bool invertY = false;

        [SerializeField] private float minPitch = -25f;
        [SerializeField] private float maxPitch = 80f;

        [Header("Follow")]

        /// <summary>
        /// Ease the camera back behind the character as it moves, the way
        /// ArcheAge does. Set to 0 for WoW's behaviour, which leaves your
        /// offset alone until you steer.
        /// </summary>
        [SerializeField] private float autoAlignSpeed = 3f;

        /// <summary>Only auto-align while actually moving - snapping the view
        /// around while a player stands still reading their bags is
        /// infuriating.</summary>
        [SerializeField] private float autoAlignMinSpeed = 0.5f;

        /// <summary>
        /// Grace period after free look ends before the camera starts
        /// easing back behind the character. Without it, releasing the left
        /// button snaps the view away the instant you let go, so a quick
        /// glance behind you is punished and free look feels like something
        /// the camera is fighting rather than a feature.
        /// </summary>
        [SerializeField] private float autoAlignDelay = 0.6f;

        [Header("First person")]

        /// <summary>
        /// Zoom past this and the camera sits at the pivot, WoW-style.
        /// IsFirstPerson goes true so the character mesh can be hidden -
        /// this class does not do the hiding, since it does not know how the
        /// character is rendered.
        /// </summary>
        [SerializeField] private float firstPersonThreshold = 0.4f;

        [Header("Occlusion")]

        [SerializeField] private bool avoidOcclusion = true;

        /// <summary>
        /// Static world geometry ONLY. Leave this as Everything and the
        /// camera sphere-casts against other players and NPCs, so it lurches
        /// forward every time someone walks behind you.
        /// </summary>
        [SerializeField] private LayerMask occlusionMask = ~0;

        [SerializeField] private float occlusionRadius = 0.25f;
        [SerializeField] private float occlusionReturnSpeed = 5f;

        [Header("Input")]

        /// <summary>
        /// Pixels of movement before a held button counts as a camera drag
        /// rather than a click.
        ///
        /// Without this every left-click is a drag: the cursor locks and
        /// hides on mouse-DOWN and warps back on release, so clicking
        /// anything in the world makes the pointer flicker, and the smallest
        /// hand tremor rotates the view. A click and a drag start
        /// identically and can only be told apart by what happens next,
        /// which is why the decision has to be deferred rather than made on
        /// the press.
        /// </summary>
        [SerializeField] private float dragThresholdPixels = 4f;

        public static MMOCamera Instance { get; private set; }

        private Transform _target;
        private float _yaw;
        private float _pitch = 20f;
        private float _currentDistance;
        private float _occludedDistance;

        // Held = the button is down and was not claimed by UI.
        // Dragging = held AND moved far enough to count as a drag.
        // The gap between them is what keeps a click a click.
        private bool _leftHeld, _rightHeld;
        private bool _leftDragging, _rightDragging;
        private Vector2 _dragAccumulator;
        private float _freeLookEndTime = -99f;
        private bool _cursorCaptured;
        private Vector2 _cursorRestorePoint;
        private Vector3 _lastTargetPosition;

        /// <summary>
        /// Scratch for the occlusion sweep. Sized generously - a cast into a
        /// crowded corner can legitimately touch a lot of geometry, and hits
        /// past the end of the buffer are silently discarded by PhysX, which
        /// would mean missing the NEAREST occluder and clipping through it.
        /// </summary>
        private readonly RaycastHit[] _occlusionHits = new RaycastHit[16];

        public Transform Target => _target;
        public float Yaw => _yaw;
        public float Pitch => _pitch;

        /// <summary>True while the right button steers. LocalPlayerInput
        /// reads this to decide whether A/D turn or strafe, and whether the
        /// character's facing is locked to the camera.</summary>
        public bool IsSteering => _rightDragging;

        /// <summary>
        /// Both buttons held. MOMENTARY - it means "move forward while this
        /// is true", which is what WoW and ArcheAge do. It is not an autorun
        /// toggle; treating it as one means clicking both buttons once sends
        /// the character running until something else stops it.
        /// </summary>
        public bool BothButtonsHeld => _leftHeld && _rightHeld;

        /// <summary>
        /// Left button only: the camera orbits and the character is not
        /// steered by it at all.
        ///
        /// Nothing downstream has to do anything special with it any more:
        /// movement is expressed in the character's own space, so orbiting
        /// the camera cannot steer you and free look works by default. It
        /// stays exposed because the flag is genuinely useful - autoAlign
        /// uses it here, and animation or UI may want to know the player is
        /// looking around rather than driving.
        ///
        /// Right wins when both are held: both together is autorun plus
        /// steering, not free look.
        /// </summary>
        public bool IsFreeLook => _leftDragging && !_rightDragging;

        public bool IsFirstPerson => _currentDistance <= firstPersonThreshold;

        private void Awake()
        {
            Instance = this;
            _currentDistance = distance;
            _occludedDistance = distance;
        }

        private void OnDestroy()
        {
            // A destroyed Unity object is a non-null reference that throws on
            // use, so leaving Instance dangling across a scene load is the
            // most confusing possible failure.
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (_target == null && fallbackTarget != null)
                SetTarget(fallbackTarget);
        }

        /// <summary>Mouse look, early - before anything reads Yaw.</summary>
        private void Update()
        {
            if (_target == null) return;

            HandleDrag();
            HandleZoom();
        }

        /// <summary>Placement, late - after the character has moved.</summary>
        private void LateUpdate()
        {
            if (_target == null) return;

            AutoAlign();
            PositionCamera();

            _lastTargetPosition = _target.position;
        }

        private void HandleDrag()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            bool overUI = EventSystem.current != null &&
                          EventSystem.current.IsPointerOverGameObject();

            // A press may only be claimed OUTSIDE UI. Re-testing every frame
            // instead means sweeping the pointer across a HUD element
            // mid-turn kills the rotation and the camera stops halfway
            // round, which feels like the game dropping input.
            if (mouse.leftButton.wasPressedThisFrame && !overUI)  _leftHeld = true;
            if (mouse.rightButton.wasPressedThisFrame && !overUI) _rightHeld = true;

            bool wasFreeLook = IsFreeLook;

            if (!mouse.leftButton.isPressed)
            {
                _leftHeld = false;
                _leftDragging = false;
            }

            if (!mouse.rightButton.isPressed)
            {
                _rightHeld = false;
                _rightDragging = false;
            }

            if (wasFreeLook && !IsFreeLook)
                _freeLookEndTime = Time.time;

            bool held = _leftHeld || _rightHeld;
            bool dragging = _leftDragging || _rightDragging;

            Vector2 delta = mouse.delta.ReadValue();

            if (held && !dragging)
            {
                // Held but not yet a drag. Accumulate movement and promote
                // only once it passes the threshold - until then this is
                // still a click and the cursor is left alone.
                _dragAccumulator += delta;

                if (_dragAccumulator.magnitude >= dragThresholdPixels)
                {
                    if (_leftHeld) _leftDragging = true;
                    if (_rightHeld) _rightDragging = true;
                    dragging = true;

                    // Remember where the pointer was so it can be put back
                    // on release. Without this the cursor reappears wherever
                    // the drag ended, and someone who turned while their
                    // pointer was over a bag slot loses it.
                    _cursorRestorePoint = mouse.position.ReadValue();
                    CaptureCursor(true);
                }
            }
            else if (!held)
            {
                _dragAccumulator = Vector2.zero;
                if (_cursorCaptured) CaptureCursor(false, mouse);
            }

            if (!dragging) return;

            // NO Time.deltaTime. Mouse delta is already accumulated movement
            // since the last frame - larger at low framerates - so scaling
            // it by frame time (also larger) squares the error and makes
            // sensitivity several times higher on a slow machine. Only
            // rate-based input, like a gamepad stick reporting a held
            // position, is scaled by frame time.
            float sensitivity = CameraSettings.Sensitivity > 0f
                ? CameraSettings.Sensitivity
                : mouseSensitivity;

            float verticalScale = CameraSettings.VerticalScale > 0f
                ? CameraSettings.VerticalScale
                : verticalSensitivityScale;

            bool invert = CameraSettings.HasInvertY ? CameraSettings.InvertY : invertY;

            _yaw += delta.x * sensitivity;
            _pitch += (invert ? 1f : -1f) * delta.y * sensitivity * verticalScale;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }

        private void CaptureCursor(bool capture, Mouse mouse = null)
        {
            _cursorCaptured = capture;
            Cursor.lockState = capture ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !capture;

            if (!capture && mouse != null)
                mouse.WarpCursorPosition(_cursorRestorePoint);
        }

        private void HandleZoom()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f) return;

            distance -= Mathf.Sign(scroll) * zoomStep;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        private void AutoAlign()
        {
            if (autoAlignSpeed <= 0f || _rightDragging || _leftDragging) return;

            // Leave a deliberate glance alone for a moment after it ends.
            if (Time.time - _freeLookEndTime < autoAlignDelay) return;

            Vector3 motion = _target.position - _lastTargetPosition;
            motion.y = 0f;

            if (motion.magnitude / Mathf.Max(Time.deltaTime, 1e-4f) < autoAlignMinSpeed)
                return;

            _yaw = Mathf.LerpAngle(_yaw, _target.eulerAngles.y, Time.deltaTime * autoAlignSpeed);
        }

        /// <summary>
        /// Called by LocalPlayerInput when the character turns under its own
        /// power (A/D keys). The camera carries that rotation so it stays
        /// behind the character, while any free-look offset is preserved -
        /// turning with the keys should not undo a deliberate glance over
        /// your shoulder.
        /// </summary>
        public void CarryCharacterTurn(float deltaDegrees)
        {
            if (_leftDragging) return;
            _yaw += deltaDegrees;
        }

        private void PositionCamera()
        {
            Vector3 pivot = _target.position + Vector3.up * pivotHeight;
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 back = rotation * Vector3.back;

            float desired = distance;

            if (avoidOcclusion && distance > firstPersonThreshold)
            {
                // Sphere, not ray: a ray lets the near plane clip into a
                // wall it passed beside.
                if (TryFindOccluder(pivot, back, distance, out float hitDistance))
                {
                    // CLAMPED TO minDistance, NOT ZERO.
                    //
                    // Letting occlusion pull all the way in means any
                    // occluder can drop the player into first person. That
                    // happens constantly and without warning: pitch down far
                    // enough at a large distance and the camera dips below
                    // the terrain behind the character, the ground counts as
                    // an occluder, and the view snaps inside the character's
                    // head. From the player's side it reads as the camera
                    // randomly zooming in while they rotate.
                    //
                    // First person is a place the player chooses to go by
                    // scrolling past firstPersonThreshold - reachable only
                    // if minDistance is 0 - never somewhere a wall puts
                    // them. Pulling in to minDistance and no further keeps
                    // the character visible even in a tight corner, which is
                    // the correct failure.
                    desired = Mathf.Max(minDistance, hitDistance);
                }
            }

            // Pull in instantly, push out slowly. Asymmetric on purpose:
            // easing INTO a wall means the wall is already through the near
            // plane before the camera reacts, while easing back out is just
            // a gentle return. Matching them makes the camera either laggy
            // or twitchy, depending which speed you pick.
            _occludedDistance = desired < _occludedDistance
                ? desired
                : Mathf.Lerp(_occludedDistance, desired, Time.deltaTime * occlusionReturnSpeed);

            _currentDistance = Mathf.Lerp(
                _currentDistance, _occludedDistance, Time.deltaTime * zoomDamping);

            transform.position = pivot + back * _currentDistance;
            transform.rotation = rotation;
        }

        /// <summary>
        /// Nearest occluder between the pivot and the camera, ignoring the
        /// character's own colliders.
        ///
        /// THE SWEEP STARTS INSIDE THE CHARACTER. The pivot is at head
        /// height, so any collider on the character - a hit volume, a
        /// weapon, a facial detail with a stray BoxCollider - overlaps the
        /// sphere at distance zero. PhysX reports that as a hit at zero
        /// range with a zero normal, and the camera collapses to the pivot:
        /// the player is abruptly inside their own head, at any distance and
        /// any angle, with no obvious cause.
        ///
        /// Filtering by parentage rather than by layer for the same reason
        /// the movement collision world does it: a layer convention only
        /// holds while everyone building a prefab remembers it, and this one
        /// cannot be forgotten.
        ///
        /// Zero-distance hits from anything else are skipped too - the
        /// camera is already inside that geometry and there is nothing
        /// useful to pull back to.
        /// </summary>
        private bool TryFindOccluder(Vector3 origin, Vector3 direction, float range, out float nearest)
        {
            // Parameters are 'range' and 'nearest' rather than 'maxDistance'
            // and 'distance' because this type has SERIALIZED FIELDS by both
            // of those names. A parameter that shadows a field compiles
            // silently and reads correctly, and then one day someone edits
            // the method meaning to touch the field and touches the
            // parameter instead. Not worth the ambiguity for two words.
            nearest = range;

            int count = Physics.SphereCastNonAlloc(
                origin, occlusionRadius, direction, _occlusionHits,
                range, occlusionMask, QueryTriggerInteraction.Ignore);

            bool found = false;

            for (int i = 0; i < count; i++)
            {
                // Not 'collider' - that shadows the long-deprecated
                // Component.collider property, which still resolves and
                // still confuses readers.
                var hitCollider = _occlusionHits[i].collider;
                if (hitCollider == null) continue;

                if (_target != null && hitCollider.transform.IsChildOf(_target)) continue;

                float d = _occlusionHits[i].distance;
                if (d <= 0f) continue;

                if (d < nearest)
                {
                    nearest = d;
                    found = true;
                }
            }

            return found;
        }

        public void SetTarget(Transform t)
        {
            _target = t;
            if (t == null) return;

            _yaw = t.eulerAngles.y;
            _lastTargetPosition = t.position;

            // Skip damping for the first frame, or the camera slides in from
            // wherever it was - across the map, on a respawn.
            _currentDistance = distance;
            _occludedDistance = distance;
            PositionCamera();
        }

        public Vector3 GetCameraForward()
        {
            Vector3 f = transform.forward;
            f.y = 0f;
            return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
        }

        public Vector3 GetCameraRight()
        {
            Vector3 r = transform.right;
            r.y = 0f;
            return r.sqrMagnitude > 1e-6f ? r.normalized : Vector3.right;
        }
    }
}