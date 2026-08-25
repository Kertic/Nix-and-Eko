using NixAndEko.Environment;
using NixAndEko.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NixAndEko.Combat
{
    /// <summary>
    /// Drives aiming and shooting in parallel with locomotion, so the player can fire while
    /// running, jumping or wall-sliding. Aiming and firing are separate: the right stick (gamepad)
    /// or the mouse cursor (KB&amp;M) points the shot, and a press of the Nix Bow button (R2 / LMB)
    /// looses it. There is no draw/charge — every shot fires at full speed and full recoil.
    ///
    /// Nix carries a single physical arrow. Firing spends it (<see cref="HasArrow"/> goes false) and
    /// leaves the arrow in the world; she gets it back by walking over it, by sending Eko to fetch
    /// it (see <see cref="EkoSummoner"/>), or when one of Eko's phantom arrows catches her. While
    /// empty the reticle still tracks and greys out so Eko can be lined up.
    /// </summary>
    public class Bow : MonoBehaviour
    {
        [Header("References")]
        public PlayerController player;
        public PlayerInputReader input;
        public Arrow arrowPrefab;
        [Tooltip("Where arrows spawn from (rotates to aim). Defaults to this transform.")]
        public Transform muzzle;
        [Tooltip("Reticle shown while aiming; points along the (snapped) aim direction.")]
        public Transform aimIndicator;
        [Tooltip("Sprite on the aim indicator; tinted by ammo state. Optional.")]
        public SpriteRenderer aimIndicatorRenderer;
        [Tooltip("LineRenderer that previews the arrow's arc while aiming. Optional.")]
        public LineRenderer trajectory;
        [Tooltip("Legacy mouse drag anchor marker — unused now that the mouse aims by cursor position. Kept hidden.")]
        public Transform dragAnchorIndicator;
        [Tooltip("Small dot markers dropped wherever the trajectory preview crosses clean through a one-way platform instead of stopping there.")]
        public Transform[] passThroughMarkers;

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
        [Tooltip("Velocity the player is set to (opposite the shot) on firing — a dash-style burst, not an add-on. Overridden by PlayerConfig when present.")]
        public float recoilMax = 14f;
        [Tooltip("Lower recoil bound, kept for the Eko-catch launch which still lerps by the arrow's stored charge. Overridden by PlayerConfig when present.")]
        public float recoilMin = 4f;
        [Tooltip("Apply recoil while grounded, for downward shots only (S / SW / SE) — that's the \"bow jump\". Sideways/upward ground shots never touch velocity, so running is never interrupted. Overridden by PlayerConfig when present.")]
        public bool recoilWhileGrounded = true;
        [Tooltip("Seconds of steering input lockout after a recoil burst, so held input can't immediately cancel the kick out. Overridden by PlayerConfig when present.")]
        public float recoilInputLock = 0.08f;

        [Header("Tuning (falls back to PlayerConfig when present)")]
        [Tooltip("Flat arrow speed — every shot fires at this speed now that charge is gone.")]
        public float arrowSpeed = 34f;

        [Header("Indicator feel")]
        public float indicatorDistance = 1.4f;
        [Tooltip("Reticle tint while holding an arrow (ready to fire).")]
        public Color readyColor = new Color(1f, 0.9f, 0.5f, 1f);
        [Tooltip("Reticle tint when empty — aim still shows (greyed) so Eko can be lined up.")]
        public Color noAmmoColor = new Color(0.55f, 0.6f, 0.7f, 0.5f);
        [Tooltip("Trajectory / reticle tint applied when Nix is wielding one of Eko's (blue) arrows.")]
        public Color blueColor = new Color(0.35f, 0.75f, 1f, 1f);

        /// <summary>
        /// True whenever the bow is being aimed (stick deflected, or mouse aiming) — regardless of
        /// whether Nix currently holds an arrow. Out of arrows the reticle still tracks and greys.
        /// </summary>
        public bool IsAiming { get; private set; }
        /// <summary>The current snapped aim direction (unit vector).</summary>
        public Vector2 AimDirection { get; private set; } = Vector2.right;

        /// <summary>Does Nix currently hold her arrow (ready to fire)?</summary>
        public bool HasArrow { get; private set; } = true;
        /// <summary>Is the held arrow one of Eko's (drawn blue)?</summary>
        public bool ArrowIsBlue { get; private set; }
        /// <summary>The last arrow Nix fired that's now lying in the world to be reclaimed (null once picked up / gone).</summary>
        public Arrow LastFiredArrow { get; private set; }

        Camera _cam;
        bool _snapNow;                     // skip hysteresis for one frame (fresh stick flick)
        bool _aimFromStickLast;
        float _arrowGravity = 2.2f;        // arrow's gravityScale, for arc prediction

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
            if (dragAnchorIndicator != null) dragAnchorIndicator.gameObject.SetActive(false);

            if (player != null && player.Config != null)
            {
                arrowSpeed = player.Config.arrowMaxSpeed;
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

            IsAiming = input.AimStickActive || input.MouseAiming;
            AimDirection = ResolveAim();

            if (IsAiming && player != null && Mathf.Abs(AimDirection.x) > 0.1f)
                player.SetFacing(AimDirection.x > 0 ? 1 : -1);

            UpdateIndicator(IsAiming);

            if (input.NixBowPressed && HasArrow)
                Fire(AimDirection);
        }

        // -------------------------------------------------------------------- Aim resolution
        /// <summary>Currently locked 8-way sector (0 = E, going CCW in 45° steps).</summary>
        int _aimSector;

        Vector2 ResolveAim()
        {
            Vector2 raw = GetRawAim();
            if (!eightDirectional) return raw.sqrMagnitude > 0.0001f ? raw.normalized : AimDirection;

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
        /// Aim comes from the right stick when it's deflected, otherwise from the mouse cursor's
        /// direction off the player centre. A fresh stick flick snaps exactly where it points
        /// (skipping hysteresis for that one frame).
        /// </summary>
        Vector2 GetRawAim()
        {
            if (input.AimStickActive)
            {
                if (!_aimFromStickLast) _snapNow = true;
                _aimFromStickLast = true;
                return input.AimStickDirection;
            }
            _aimFromStickLast = false;

            if (input.MouseAiming && Mouse.current != null)
            {
                if (_cam == null) _cam = Camera.main;
                if (_cam != null)
                {
                    Vector3 mp = Mouse.current.position.ReadValue();
                    mp.z = -_cam.transform.position.z;
                    Vector3 world = _cam.ScreenToWorldPoint(mp);
                    Vector2 d = (Vector2)(world - Origin);
                    if (d.sqrMagnitude > 0.0001f) return d;
                }
            }

            return player != null ? new Vector2(player.Facing, 0f) : Vector2.right;
        }

        Vector2 SnapSticky(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.0001f) return SectorToDir(_aimSector);

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float fromCurrent = Mathf.DeltaAngle(_aimSector * 45f, angle);

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

        // -------------------------------------------------------------------- Indicators
        void UpdateIndicator(bool aiming)
        {
            if (aimIndicator != null)
            {
                aimIndicator.gameObject.SetActive(aiming);
                if (aiming)
                {
                    aimIndicator.position = Origin + (Vector3)AimDirection * indicatorDistance;
                    aimIndicator.right = AimDirection;
                    if (aimIndicatorRenderer != null)
                        aimIndicatorRenderer.color = !HasArrow ? noAmmoColor
                                                   : ArrowIsBlue ? blueColor : readyColor;
                }
            }

            UpdateTrajectory();
        }

        /// <summary>
        /// Preview the arrow's parabolic arc, clipped at the first surface it would actually stop
        /// at. Shown only while aiming with an arrow in hand. A one-way platform only clips the line
        /// if the arc would hit its blocking face (<see cref="OneWayPlatform.Blocks"/>); a shot
        /// arcing up through one from below gets a small marker dropped where it crosses instead.
        /// </summary>
        void UpdateTrajectory()
        {
            if (trajectory == null) return;

            if (!IsAiming || !HasArrow)
            {
                trajectory.positionCount = 0;
                HidePassThroughMarkers();
                return;
            }

            Vector2 p0 = Origin;
            Vector2 v0 = AimDirection * arrowSpeed;
            Vector2 accel = new Vector2(0f, Physics2D.gravity.y * _arrowGravity);
            LayerMask mask = player != null ? player.groundMask : default;

            trajectory.positionCount = trajectorySteps;
            Vector2 prev = p0;
            int count = 0;
            int markerCount = 0;
            Collider2D lastPassThrough = null;

            for (int i = 0; i < trajectorySteps; i++)
            {
                float t = i * trajectoryStep;
                Vector2 pt = p0 + v0 * t + 0.5f * accel * (t * t);

                if (i > 0 && mask.value != 0)
                {
                    var hit = Physics2D.Linecast(prev, pt, mask);
                    if (hit.collider != null)
                    {
                        if (OneWayPlatform.Blocks(hit))
                        {
                            trajectory.SetPosition(count++, hit.point);
                            prev = pt;
                            break;
                        }

                        if (hit.collider != lastPassThrough &&
                            passThroughMarkers != null && markerCount < passThroughMarkers.Length)
                        {
                            ShowPassThroughMarker(markerCount++, hit.point);
                            lastPassThrough = hit.collider;
                        }
                    }
                    else
                    {
                        lastPassThrough = null;
                    }
                }

                trajectory.SetPosition(count++, pt);
                prev = pt;
            }

            trajectory.positionCount = count;
            HidePassThroughMarkers(markerCount);

            Color c = ArrowIsBlue ? blueColor : readyColor;
            trajectory.startColor = c;
            trajectory.endColor = new Color(c.r, c.g, c.b, 0f); // fade out toward the end
        }

        void ShowPassThroughMarker(int index, Vector2 pos)
        {
            if (passThroughMarkers == null || index >= passThroughMarkers.Length) return;
            Transform m = passThroughMarkers[index];
            if (m == null) return;
            m.gameObject.SetActive(true);
            m.position = pos;
        }

        void HidePassThroughMarkers(int fromIndex = 0)
        {
            if (passThroughMarkers == null) return;
            for (int i = fromIndex; i < passThroughMarkers.Length; i++)
                if (passThroughMarkers[i] != null) passThroughMarkers[i].gameObject.SetActive(false);
        }

        // -------------------------------------------------------------------- Arrow inventory
        public float ArrowSpeed() => arrowSpeed;

        /// <summary>Hand Nix an arrow back — from a walk-over pickup, an Eko fetch, or an Eko-arrow
        /// catch. <paramref name="blue"/> marks it as one of Eko's arrows (drawn blue).</summary>
        public void GiveArrow(bool blue)
        {
            HasArrow = true;
            ArrowIsBlue = blue;
            LastFiredArrow = null;
        }

        /// <summary>
        /// Legacy name kept for the Eko-catch path: reloading Nix's shot mid-air now means handing
        /// her one of Eko's blue arrows. See <see cref="EkoArrowTarget"/>.
        /// </summary>
        public void RefreshAirShot() => GiveArrow(true);

        void Fire(Vector2 aimDir)
        {
            if (arrowPrefab == null)
            {
                Debug.LogWarning("[Bow] No arrow prefab assigned.", this);
                return;
            }

            Quaternion rot = Quaternion.FromToRotation(Vector3.right, aimDir);
            Arrow arrow = Instantiate(arrowPrefab, Origin, rot); // origin = player center, matches the arc preview
            arrow.gameObject.SetActive(true); // template may be inactive; copies must run

            arrow.SetNixArrow(this, player != null ? player.Col : null, ArrowIsBlue);
            arrow.Launch(aimDir * arrowSpeed, 1f);
            ApplyRecoil(aimDir, 1f);

            // A blue (Eko's) arrow is spent on use — never a pickup and never an Eko-fetch target,
            // and firing it must not clobber the reference to Nix's real downed arrow still out in
            // the world (the Eko-catch flow), which stays the fetch target.
            if (!arrow.blue) LastFiredArrow = arrow;
            HasArrow = false;
            ArrowIsBlue = false;
        }

        // -------------------------------------------------------------------- Recoil
        void ApplyRecoil(Vector2 aimDir, float charge)
        {
            if (player == null) return;
            if (aimDir.sqrMagnitude < 0.0001f) return;

            // Grounded (or still within coyote): only a downward shot triggers recoil — the "bow jump".
            if (player.GroundedForRecoil && (!recoilWhileGrounded || aimDir.y > -0.5f)) return;

            float speed = Mathf.Lerp(recoilMin, recoilMax, charge);
            ApplyLaunch((-aimDir).normalized, speed);
        }

        /// <summary>
        /// The momentum boost Eko's arrow hands Nix when it catches her — a burst along the aim the
        /// phantom was planted with (aim up-right, get flung up-right), not opposite it like Nix's
        /// own recoil. Replaces the whole velocity vector (never dampening momentum already running
        /// along the aim), so only the frozen aim decides where she goes.
        /// </summary>
        public void EkoLaunch(Vector2 aimDir, float charge)
        {
            if (player == null) return;
            if (aimDir.sqrMagnitude < 0.0001f) return;

            Vector2 dir = aimDir.normalized;
            float speed = Mathf.Lerp(recoilMin, recoilMax, charge);

            float along = Vector2.Dot(player.Velocity, dir);
            player.Velocity = dir * Mathf.Max(speed, along);

            if (dir.y > 0.1f) player.Machine.ChangeState(player.Jump);
            if (recoilInputLock > 0f) player.LockInput(recoilInputLock);
        }

        /// <summary>
        /// Nix's own recoil burst. Set velocity to a clean burst of <paramref name="speed"/> along
        /// <paramref name="kickDir"/>, per-axis: an axis the kick doesn't touch is left alone, and
        /// one it does only overrides opposing momentum. An upward burst enters the floaty rising
        /// state, and steering input is briefly locked so held input can't eat it.
        /// </summary>
        void ApplyLaunch(Vector2 kickDir, float speed)
        {
            if (player == null) return;

            Vector2 v = player.Velocity;
            v.x = ResolveRecoilAxis(v.x, kickDir.x, speed);
            v.y = ResolveRecoilAxis(v.y, kickDir.y, speed);
            player.Velocity = v;

            if (kickDir.y > 0.1f)
                player.Machine.ChangeState(player.Jump);

            if (recoilInputLock > 0f) player.LockInput(recoilInputLock);
        }

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
