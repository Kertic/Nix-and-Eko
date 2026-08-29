using NixAndEko.Environment;
using NixAndEko.Player;
using NixAndEko.Util;
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

        /// <summary>Does Nix hold her own physical arrow (retrievable once fired)?</summary>
        public bool HasNormalArrow { get; private set; } = true;
        /// <summary>Does Nix hold one of Eko's blue arrows (spent on use, never retrievable)?</summary>
        public bool HasBlueArrow { get; private set; }
        /// <summary>Can Nix fire at all (either slot)?</summary>
        public bool HasAnyArrow => HasNormalArrow || HasBlueArrow;
        /// <summary>A blue arrow fires first when both slots are stocked, so this is what the next shot will be.</summary>
        public bool FiresBlueNext => HasBlueArrow;
        /// <summary>The last <em>normal</em> arrow Nix fired that's now lying in the world to be reclaimed (null once picked up / gone).</summary>
        public Arrow LastFiredArrow { get; private set; }

        /// <summary>Register a phantom-fired shot as Nix's new persistent arrow — updates
        /// <see cref="LastFiredArrow"/> so L1 dash and R2 fetch can find it. Called by
        /// <see cref="EkoSummoner"/> right after <see cref="Eko.Loose"/>.</summary>
        public void SetLastFiredArrow(Arrow arrow) => LastFiredArrow = arrow;

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

            // A blue arrow is Eko's — an in-air bonus that doesn't survive a landing. Touching the
            // ground clears the blue slot (her own normal arrow is untouched).
            if (player != null && player.Grounded) HasBlueArrow = false;

            IsAiming = input.AimStickActive || input.MouseAiming;
            AimDirection = ResolveAim();

            if (IsAiming && player != null && Mathf.Abs(AimDirection.x) > 0.1f)
                player.SetFacing(AimDirection.x > 0 ? 1 : -1);

            UpdateIndicator(IsAiming);

            if (input.NixBowPressed && HasAnyArrow)
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
                        aimIndicatorRenderer.color = !HasAnyArrow ? noAmmoColor
                                                   : FiresBlueNext ? blueColor : readyColor;
                }
            }

            UpdateTrajectory();
        }

        /// <summary>
        /// Preview the arrow's straight flight path, clipped at the first surface it would stop
        /// at — Nix's arrows now fly gravity-free (same as Eko's), so no arc simulation is needed:
        /// a single raycast gives the exact line and the surface it stops on. A one-way platform
        /// only clips the line if the shot would hit its blocking face
        /// (<see cref="OneWayPlatform.Blocks"/>); a shot that would pass clean through gets a
        /// small marker dropped where it crosses and the cast continues past it.
        /// </summary>
        void UpdateTrajectory()
        {
            if (trajectory == null) return;

            if (!IsAiming || !HasAnyArrow)
            {
                trajectory.positionCount = 0;
                HidePassThroughMarkers();
                return;
            }

            const float previewDistance = 40f;
            Vector2 origin = Origin;
            Vector2 end = origin + AimDirection * previewDistance;
            LayerMask mask = player != null ? player.groundMask : default;
            int markerCount = 0;

            if (mask.value != 0)
            {
                Vector2 castOrigin = origin;
                float remaining = previewDistance;
                Collider2D lastPassThrough = null;

                // Bounded rather than "while true" — plenty for any stack of one-ways and a
                // guarantee the cast can't loop forever on a degenerate setup.
                for (int i = 0; i < 8 && remaining > 0.01f; i++)
                {
                    var hit = Physics2D.Raycast(castOrigin, AimDirection, remaining, mask);
                    if (hit.collider == null) break;

                    if (OneWayPlatform.Blocks(hit))
                    {
                        end = hit.point;
                        break;
                    }

                    if (hit.collider != lastPassThrough &&
                        passThroughMarkers != null && markerCount < passThroughMarkers.Length)
                    {
                        ShowPassThroughMarker(markerCount++, hit.point);
                        lastPassThrough = hit.collider;
                    }

                    float advanced = Vector2.Distance(castOrigin, hit.point) + 0.05f;
                    castOrigin += AimDirection * advanced;
                    remaining -= advanced;
                }
            }

            HidePassThroughMarkers(markerCount);

            trajectory.positionCount = 2;
            trajectory.SetPosition(0, origin);
            trajectory.SetPosition(1, end);

            Color c = FiresBlueNext ? blueColor : readyColor;
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

        /// <summary>Hand Nix an arrow back. <paramref name="blue"/> fills the blue slot (an Eko arrow,
        /// spent on use); otherwise the normal slot (her own physical arrow). The two slots are
        /// independent, so an Eko-arrow catch grants a bonus blue shot without disturbing her normal
        /// arrow's state — down in the world or in hand.
        ///
        /// This does NOT touch <see cref="LastFiredArrow"/>: an Eko-arrow catch happens while her
        /// real arrow is still down and must stay the fetch target; and when a walk-over / fetch
        /// reclaim calls this, the reclaimed arrow is destroyed in the same breath, so
        /// <see cref="LastFiredArrow"/> naturally reads as null without an explicit clear.</summary>
        public void GiveArrow(bool blue)
        {
            if (blue) HasBlueArrow = true;
            else HasNormalArrow = true;
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

            // A blue arrow fires first when both slots are stocked, and is spent on use.
            bool blue = FiresBlueNext;

            Quaternion rot = Quaternion.FromToRotation(Vector3.right, aimDir);
            Arrow arrow = Instantiate(arrowPrefab, Origin, rot); // origin = player center, matches the arc preview
            arrow.gameObject.SetActive(true); // template may be inactive; copies must run

            arrow.flyStraight = true;   // Nix's arrows now ignore gravity, just like Eko's.
            arrow.SetNixArrow(this, player != null ? player.Col : null, blue);
            arrow.Launch(aimDir * arrowSpeed, 1f);
            ApplyRecoil(aimDir, 1f);
            Sfx.Play(Sfx.Id.Bow, blue ? 1.15f : 1f);

            if (blue)
            {
                // Blue arrow spent — never a pickup, never a fetch target, and the normal slot
                // (in hand or down in the world) is left exactly as it was.
                HasBlueArrow = false;
            }
            else
            {
                LastFiredArrow = arrow;
                HasNormalArrow = false;
            }
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
