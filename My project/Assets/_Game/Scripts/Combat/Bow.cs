using NixAndEko.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NixAndEko.Combat
{
    /// <summary>
    /// Drives aiming and shooting in parallel with locomotion, so the player can fire while
    /// running, jumping or wall-sliding. Hold Attack to draw (charge), release to fire.
    /// Aim is snapped to 8 cardinal/ordinal directions. A reticle indicates draw + aim.
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

        [Header("Recoil")]
        [Tooltip("Speed applied to the player opposite the shot at zero draw. Overridden by PlayerConfig when present.")]
        public float recoilMin = 4f;
        [Tooltip("Speed applied to the player opposite the shot at full draw. Overridden by PlayerConfig when present.")]
        public float recoilMax = 14f;

        [Header("Tuning (falls back to PlayerConfig when present)")]
        public float drawTime = 0.5f;
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

        Camera _cam;
        float _arrowGravity = 2.2f;   // arrow's gravityScale, for arc prediction

        /// <summary>The world point aiming originates from — the player's center.</summary>
        Vector3 Origin => player != null ? player.transform.position : muzzle.position;

        void Awake()
        {
            _cam = Camera.main;
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
                aimHysteresis = player.Config.aimHysteresis;
            }
        }

        void Update()
        {
            if (input == null) return;

            AimDirection = ResolveAim();
            UpdateIndicator();

            if (input.AttackHeld)
            {
                IsDrawing = true;
                Charge = Mathf.Clamp01(Charge + Time.deltaTime / Mathf.Max(0.01f, drawTime));
                if (player != null && Mathf.Abs(AimDirection.x) > 0.1f)
                    player.SetFacing(AimDirection.x > 0 ? 1 : -1);
            }

            if (input.AttackReleased && IsDrawing)
            {
                Fire(AimDirection, Charge);
                Charge = 0f;
                IsDrawing = false;
            }
        }

        /// <summary>Currently locked 8-way sector (0 = E, going CCW in 45° steps).</summary>
        int _aimSector;

        Vector2 ResolveAim()
        {
            Vector2 raw = GetRawAim();
            return eightDirectional ? SnapSticky(raw) : raw.normalized;
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

        Vector2 GetRawAim()
        {
            // Gamepad right stick takes priority when actively used.
            if (input.Look.sqrMagnitude > 0.15f)
                return input.Look;

            // Otherwise aim from the muzzle toward the mouse.
            if (_cam == null) _cam = Camera.main;
            if (_cam != null && Mouse.current != null)
            {
                Vector3 mouse = _cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
                Vector2 dir = (Vector2)mouse - (Vector2)Origin;
                if (dir.sqrMagnitude > 0.001f) return dir;
            }

            return player != null ? new Vector2(player.Facing, 0f) : Vector2.right;
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
        }

        /// <summary>
        /// Push the player opposite the shot — shooting downward pogo-jumps you upward,
        /// shooting left flings you right, etc. Scales with draw charge.
        /// </summary>
        void ApplyRecoil(Vector2 aimDir, float charge)
        {
            if (player == null) return;

            float recoil = Mathf.Lerp(recoilMin, recoilMax, charge);
            Vector2 kick = -aimDir * recoil;
            Vector2 v = player.Velocity;

            // Vertical: an upward kick replaces any downward speed (a clean pogo boost);
            // a downward kick just adds.
            if (kick.y > 0f) v.y = Mathf.Max(v.y, 0f) + kick.y;
            else v.y += kick.y;

            v.x += kick.x;

            player.Velocity = v;

            // If the kick lifts us off the ground, hand control to the airborne states.
            if (kick.y > 0.1f && player.Grounded)
                player.Machine.ChangeState(player.Fall);
        }
    }
}
