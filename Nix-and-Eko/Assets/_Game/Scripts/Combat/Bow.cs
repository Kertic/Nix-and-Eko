using NixAndEko.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NixAndEko.Combat
{
    /// <summary>
    /// Drives aiming and shooting in parallel with locomotion, so the player can fire while
    /// running, jumping or wall-sliding. Hold Attack to draw (charge), release to fire.
    /// On mouse, aim is a drag: press to pin an anchor at the cursor, drag away from it to
    /// choose one of 8 directions, release to fire. On a gamepad the right stick does both jobs
    /// at once: pushing it out starts the draw and points the shot, letting it spring back to
    /// centre fires along the direction it was last pushed.
    /// A reticle and trajectory arc indicate draw + aim.
    /// </summary>
    public class Bow : MonoBehaviour
    {
        [Header("References")]
        public PlayerController player;
        public PlayerInputReader input;
        public Arrow arrowPrefab;
        [Tooltip("Where arrows spawn from (rotates to aim). Defaults to this transform.")]
        public Transform muzzle;
        [Tooltip("Reticle shown while drawing; points along the (snapped) aim direction.")]
        public Transform aimIndicator;
        [Tooltip("Sprite on the aim indicator; tinted by charge. Optional.")]
        public SpriteRenderer aimIndicatorRenderer;
        [Tooltip("LineRenderer that previews the arrow's arc while drawing. Optional.")]
        public LineRenderer trajectory;
        [Tooltip("Small marker drawn where the drag started. Optional.")]
        public Transform dragAnchorIndicator;

        [Header("Trajectory preview")]
        [Tooltip("How many sample points along the predicted arc.")]
        public int trajectorySteps = 26;
        [Tooltip("Simulated seconds between samples.")]
        public float trajectoryStep = 0.045f;

        [Header("Aim")]
        [Tooltip("Snap firing to 8 directions (N, NE, E, SE, S, SW, W, NW).")]
        public bool eightDirectional = true;
        [Tooltip("Extra degrees past a sector boundary the aim must travel before switching direction. Prevents flicker at the 45° edges. Overridden by PlayerConfig when present.")]
        [Range(0f, 22f)]
        public float aimHysteresis = 12f;
        [Tooltip("How far the cursor must move from the anchor (screen pixels) before a direction registers.")]
        public float dragDeadzonePixels = 14f;

        [Header("Recoil")]
        [Tooltip("Velocity the player is set to (opposite the shot) at zero draw — a dash-style burst, not an add-on. Overridden by PlayerConfig when present.")]
        public float recoilMin = 4f;
        [Tooltip("Velocity the player is set to (opposite the shot) at full draw — a dash-style burst, not an add-on. Overridden by PlayerConfig when present.")]
        public float recoilMax = 14f;
        [Tooltip("Apply recoil while grounded. Overridden by PlayerConfig when present.")]
        public bool recoilWhileGrounded = false;
        [Tooltip("Seconds of steering input lockout after a recoil burst, so held input can't immediately cancel the kick out. Overridden by PlayerConfig when present.")]
        public float recoilInputLock = 0.08f;

        [Header("Ammo")]
        [Tooltip("Only one shot per airtime: after firing mid-air the bow is spent until the archer lands.")]
        public bool oneShotPerAirtime = true;

        [Header("Tuning (falls back to PlayerConfig when present)")]
        public float drawTime = 0.2f;
        public float minSpeed = 10f;
        public float maxSpeed = 34f;

        [Header("Indicator feel")]
        public float indicatorNearDistance = 0.7f;
        public float indicatorFarDistance = 1.6f;
        public Color chargeStartColor = new Color(1f, 1f, 1f, 0.6f);
        public Color chargeFullColor = new Color(1f, 0.35f, 0.35f, 1f);

        public float Charge { get; private set; }   // 0..1
        public bool IsDrawing { get; private set; }
        /// <summary>The current snapped aim direction (unit vector).</summary>
        public Vector2 AimDirection { get; private set; } = Vector2.right;

        /// <summary>False while the bow is spent mid-air, so UI can grey the reticle out.</summary>
        public bool CanFire => !oneShotPerAirtime || !_shotSpent;

        Camera _cam;
        Vector2 _dragAnchorScreen;         // where Attack was pressed, in screen pixels
        bool _hasAnchor;                   // is a drag gesture currently active
        bool _hasAim;                      // has the drag cleared the deadzone yet
        bool _aimFromStick;                // this draw is steered by the right stick, not the mouse
        bool _snapNow;                     // skip hysteresis for one frame (fresh stick flick)
        bool _shotSpent;                   // fired since last touching the ground
        float _arrowGravity = 2.2f;   // arrow's gravityScale, for arc prediction

        /// <summary>The world point aiming originates from — the player's center.</summary>
        Vector3 Origin => player != null ? player.transform.position : muzzle.position;

        void Awake()
        {
            if (muzzle == null) muzzle = transform;
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (input == null && player != null) input = player.Input;
            if (aimIndicator != null && aimIndicatorRenderer == null)
                aimIndicatorRenderer = aimIndicator.GetComponentInChildren<SpriteRenderer>();
            if (arrowPrefab != null) _arrowGravity = arrowPrefab.gravityScale;

            if (player != null && player.Config != null)
            {
                drawTime = player.Config.bowDrawTime;
                minSpeed = player.Config.arrowMinSpeed;
                maxSpeed = player.Config.arrowMaxSpeed;
                recoilMin = player.Config.recoilMin;
                recoilMax = player.Config.recoilMax;
                recoilWhileGrounded = player.Config.recoilWhileGrounded;
                recoilInputLock = player.Config.recoilInputLock;
                aimHysteresis = player.Config.aimHysteresis;
            }
        }

        void Update()
        {
            if (input == null) return;

            // Landing reloads the bow.
            if (player != null && player.Grounded) _shotSpent = false;

            UpdateAimSource();
            UpdateDragAnchor();

            AimDirection = ResolveAim();
            UpdateIndicator();

            if (input.AttackHeld && CanFire)
            {
                IsDrawing = true;
                Charge = Mathf.Clamp01(Charge + Time.deltaTime / Mathf.Max(0.01f, drawTime));
                if (player != null && Mathf.Abs(AimDirection.x) > 0.1f)
                    player.SetFacing(AimDirection.x > 0 ? 1 : -1);
            }

            if (input.AttackReleased)
            {
                if (IsDrawing && CanFire) Fire(AimDirection, Charge);
                Charge = 0f;
                IsDrawing = false;
                _aimFromStick = false;   // the next gesture picks its own source
            }
        }

        /// <summary>Currently locked 8-way sector (0 = E, going CCW in 45° steps).</summary>
        int _aimSector;

        Vector2 ResolveAim()
        {
            Vector2 raw = GetRawAim();
            if (!eightDirectional) return raw.normalized;

            // A fresh flick of the stick should land exactly where it is pushed, so the very
            // first frame of that gesture ignores the anti-flicker hysteresis.
            if (_snapNow && raw.sqrMagnitude > 0.0001f)
            {
                _snapNow = false;
                int nearest = Mathf.RoundToInt(Mathf.Atan2(raw.y, raw.x) * Mathf.Rad2Deg / 45f);
                _aimSector = ((nearest % 8) + 8) % 8;
                return SectorToDir(_aimSector);
            }

            return SnapSticky(raw);
        }

        /// <summary>
        /// Work out whether this draw is a stick gesture or a mouse drag. The stick claims the
        /// draw the moment it leaves centre and keeps it until the shot goes off, so the release
        /// frame still fires along the direction the stick was pushed.
        /// </summary>
        void UpdateAimSource()
        {
            if (!input.AimStickActive) return;

            if (!_aimFromStick) _snapNow = true;   // fresh flick: point exactly where it's pushed
            _aimFromStick = true;
        }

        /// <summary>
        /// Snap to 8 directions with hysteresis: the aim keeps its current sector until the raw
        /// angle travels past the sector's edge by <see cref="aimHysteresis"/> degrees, so the
        /// reticle doesn't flicker when aiming near a 45° boundary.
        /// </summary>
        Vector2 SnapSticky(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.0001f) return SectorToDir(_aimSector);

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float fromCurrent = Mathf.DeltaAngle(_aimSector * 45f, angle);

            // Only leave the current sector once we're clearly past its half-width (22.5°) plus margin.
            if (Mathf.Abs(fromCurrent) > 22.5f + aimHysteresis)
            {
                int nearest = Mathf.RoundToInt(angle / 45f);
                _aimSector = ((nearest % 8) + 8) % 8;
            }

            return SectorToDir(_aimSector);
        }

        static Vector2 SectorToDir(int sector)
        {
            float rad = sector * 45f * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;
        }

        /// <summary>
        /// Aim comes from the right stick when one is in play, otherwise from a mouse drag.
        /// For the mouse: pressing Attack pins an anchor at the cursor, and the
        /// direction dragged away from that anchor is the direction the arrow flies (snapped to
        /// eight). The gesture is measured in screen space so a scrolling camera can't skew it.
        /// Before the drag clears the deadzone the aim holds its last value, then the facing.
        /// </summary>
        Vector2 GetRawAim()
        {
            // Right stick: the direction it's pushed is the direction the arrow flies. The
            // input reader latches that direction, so the springback never steals the aim.
            if (_aimFromStick) return input.AimStickDirection;

            if (Mouse.current != null && _hasAnchor)
            {
                Vector2 drag = Mouse.current.position.ReadValue() - _dragAnchorScreen;
                if (drag.sqrMagnitude > dragDeadzonePixels * dragDeadzonePixels)
                {
                    _hasAim = true;
                    return drag;   // screen and world axes align for an unrotated ortho camera
                }
            }

            // Drag hasn't left the anchor yet — keep pointing where we already were.
            if (_hasAim) return SectorToDir(_aimSector);

            return player != null ? new Vector2(player.Facing, 0f) : Vector2.right;
        }

        /// <summary>Pin/release the drag anchor and keep its on-screen marker in place.</summary>
        void UpdateDragAnchor()
        {
            if (input.AttackPressed && !_aimFromStick && Mouse.current != null)
            {
                _dragAnchorScreen = Mouse.current.position.ReadValue();
                _hasAnchor = true;
                _hasAim = false;   // a fresh gesture starts from the archer's facing
            }

            if (!input.AttackHeld || _aimFromStick) _hasAnchor = false;

            if (dragAnchorIndicator == null) return;
            dragAnchorIndicator.gameObject.SetActive(_hasAnchor);
            if (!_hasAnchor) return;

            // Re-project every frame so the marker stays under the spot that was clicked.
            if (_cam == null) _cam = Camera.main;
            if (_cam != null)
            {
                Vector3 world = _cam.ScreenToWorldPoint(new Vector3(
                    _dragAnchorScreen.x, _dragAnchorScreen.y, -_cam.transform.position.z));
                world.z = 0f;
                dragAnchorIndicator.position = world;
            }
        }

        void UpdateIndicator()
        {
            if (aimIndicator != null)
            {
                aimIndicator.gameObject.SetActive(IsDrawing);
                if (IsDrawing)
                {
                    float dist = Mathf.Lerp(indicatorNearDistance, indicatorFarDistance, Charge);
                    aimIndicator.position = Origin + (Vector3)AimDirection * dist;
                    aimIndicator.right = AimDirection;
                    if (aimIndicatorRenderer != null)
                        aimIndicatorRenderer.color = Color.Lerp(chargeStartColor, chargeFullColor, Charge);
                }
            }

            UpdateTrajectory();
        }

        /// <summary>Preview the arrow's parabolic arc, clipped at the first surface it would hit.</summary>
        void UpdateTrajectory()
        {
            if (trajectory == null) return;

            if (!IsDrawing)
            {
                trajectory.positionCount = 0;
                return;
            }

            float speed = Mathf.Lerp(minSpeed, maxSpeed, Charge);
            Vector2 p0 = Origin;
            Vector2 v0 = AimDirection * speed;
            Vector2 accel = new Vector2(0f, Physics2D.gravity.y * _arrowGravity);
            LayerMask mask = player != null ? player.groundMask : default;

            trajectory.positionCount = trajectorySteps;
            Vector2 prev = p0;
            int count = 0;

            for (int i = 0; i < trajectorySteps; i++)
            {
                float t = i * trajectoryStep;
                Vector2 pt = p0 + v0 * t + 0.5f * accel * (t * t);

                if (i > 0 && mask.value != 0)
                {
                    var hit = Physics2D.Linecast(prev, pt, mask);
                    if (hit.collider != null)
                    {
                        trajectory.SetPosition(count++, hit.point);
                        break;
                    }
                }

                trajectory.SetPosition(count++, pt);
                prev = pt;
            }

            trajectory.positionCount = count;

            Color c = Color.Lerp(chargeStartColor, chargeFullColor, Charge);
            trajectory.startColor = c;
            trajectory.endColor = new Color(c.r, c.g, c.b, 0f); // fade out toward the end
        }

        void Fire(Vector2 aimDir, float charge)
        {
            if (arrowPrefab == null)
            {
                Debug.LogWarning("[Bow] No arrow prefab assigned.", this);
                return;
            }

            float speed = Mathf.Lerp(minSpeed, maxSpeed, charge);
            Quaternion rot = Quaternion.FromToRotation(Vector3.right, aimDir);
            Arrow arrow = Instantiate(arrowPrefab, Origin, rot); // origin = player center, matches the arc preview
            arrow.gameObject.SetActive(true); // template may be inactive; copies must run

            if (player != null)
            {
                var arrowCol = arrow.GetComponent<Collider2D>();
                if (arrowCol != null && player.Col != null)
                    Physics2D.IgnoreCollision(arrowCol, player.Col, true);
            }

            arrow.Launch(aimDir * speed, charge);
            ApplyRecoil(aimDir, charge);

            // Airborne shots spend the bow until the archer next touches the ground.
            if (player != null && !player.Grounded) _shotSpent = true;
        }

        /// <summary>
        /// Recoil overrides the player's velocity opposite the shot — like a Celeste dash or
        /// double jump, it's a clean burst to a fixed speed rather than a shove added on top of
        /// whatever you were already carrying. Shooting downward launches you upward at a
        /// consistent speed, shooting left sends you right at a consistent speed, etc. Scales
        /// with draw charge. An axis the kick doesn't touch (e.g. horizontal, for a straight-down
        /// shot) is left alone entirely, and an axis it does touch only overrides momentum that
        /// opposes it — sailing right and firing straight down keeps that rightward speed instead
        /// of zeroing it. Steering input is briefly locked out afterward so held input can't
        /// immediately eat into the burst.
        /// </summary>
        void ApplyRecoil(Vector2 aimDir, float charge)
        {
            if (player == null) return;

            // Standing on the ground, the archer is braced — no recoil kick.
            if (player.Grounded && !recoilWhileGrounded) return;

            if (aimDir.sqrMagnitude < 0.0001f) return;

            float speed = Mathf.Lerp(recoilMin, recoilMax, charge);
            Vector2 kickDir = (-aimDir).normalized;
            Vector2 v = player.Velocity;

            v.x = ResolveRecoilAxis(v.x, kickDir.x, speed);
            v.y = ResolveRecoilAxis(v.y, kickDir.y, speed);

            player.Velocity = v;

            // If the kick lifts us off the ground, hand control to the airborne states.
            if (kickDir.y > 0.1f && player.Grounded)
                player.Machine.ChangeState(player.Fall);

            if (recoilInputLock > 0f) player.LockInput(recoilInputLock);
        }

        /// <summary>
        /// Blends the recoil burst onto one axis of existing velocity. A kick with (near) zero
        /// component on this axis doesn't touch it at all. Otherwise it overrides — unless
        /// existing momentum already runs the same direction as the kick and is faster, in which
        /// case momentum wins and isn't dampened.
        /// </summary>
        static float ResolveRecoilAxis(float current, float kickAxis, float speed)
        {
            if (Mathf.Abs(kickAxis) < 0.001f) return current;

            float target = kickAxis * speed;
            if (Mathf.Sign(current) == Mathf.Sign(target) && Mathf.Abs(current) > Mathf.Abs(target))
                return current;

            return target;
        }
    }
}
