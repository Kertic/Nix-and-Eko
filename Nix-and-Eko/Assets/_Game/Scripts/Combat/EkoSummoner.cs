using NixAndEko.Environment;
using NixAndEko.Player;
using NixAndEko.Util;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NixAndEko.Combat
{
    /// <summary>
    /// Owns the Eko button flow:
    ///
    /// <list type="bullet">
    /// <item><b>Tap L1</b> (arrow stuck, hasn't dashed this airtime): Nix dashes along a bright
    /// blue tether to the arrow. Frozen + intangible during flight, brief invuln, reclaims the
    /// arrow on arrival. The dash spends the airtime's grapple charge — one per airtime, reset
    /// on ground touch. In-flight arrows and spectral arrows reject the tap.</item>
    /// <item><b>Hold L1</b>: after a short tap threshold, the arrow morphs into the phantom in
    /// place. Camera <b>snaps to Eko</b> so the player can see what they're aiming at. During
    /// the morph animation, time still runs at normal speed. Once the morph finishes,
    /// <see cref="Time.timeScale"/> drops to 0 for a bullet-time aim — everything freezes. Aim
    /// is full 360°, driven by the same stick or mouse Nix uses for the bow (relative to the
    /// phantom's world position).</item>
    /// <item><b>Release</b>: the phantom looses a spectral arrow (spent on use, not
    /// grapplable, doesn't refill LastFiredArrow) and <b>stays where it is</b>, watching. Time
    /// resumes, camera snaps back to Nix. When the spectral shot catches Nix,
    /// <see cref="EkoArrowTarget"/> grants +1 spectral (blue) slot, glide refill, momentum boost
    /// and +1 air jump — NOT her normal slot (that would let her chain morphs indefinitely).</item>
    /// <item><b>Ground touch (after firing)</b>: the phantom orbs home and Nix's normal arrow
    /// slot is restored. This is the only path back to her normal ammo after a morph, so a
    /// perfect morph → self-catch loop still ends with her having to land before firing again.</item>
    /// <item><b>R2 (Nix, no arrow, grounded)</b>: <see cref="TryStartFetch"/> — unchanged.</item>
    /// </list>
    /// </summary>
    public class EkoSummoner : MonoBehaviour
    {
        [Header("References")]
        public PlayerController player;
        public PlayerInputReader input;
        public Bow bow;
        public Eko eko;
        [Tooltip("Eko's own controller — frozen throughout, kept for the collider / animator.")]
        public PlayerController ekoPlayer;
        [Tooltip("Nix's Health — brief invuln while she's mid-dash so a target she's dashing " +
                 "into can't hurt her mid-flight.")]
        public Health nixHealth;

        [Header("Tap / hold")]
        [Tooltip("Seconds L1 must be held past the initial press before it counts as a hold " +
                 "(morph + aim) rather than a tap (dash). Runs on unscaled time.")]
        public float tapHoldThreshold = 0.15f;

        [Header("Dash to arrow (tap L1)")]
        [Tooltip("How far past the arrow's tip Nix lands (world units, along the reverse of the " +
                 "arrow's flight direction). Keeps her from ending inside the wall the arrow's " +
                 "embedded in.")]
        public float dashLandOffset = 0.7f;
        [Tooltip("Bonus invulnerability granted for the dash + a short landing grace.")]
        public float dashInvuln = 0.35f;
        [Tooltip("Bright-blue tether tint that Eko throws from the arrow spot to Nix.")]
        public Color tetherColor = new Color(0.4f, 0.85f, 1f, 1f);
        [Tooltip("Line width of the tether, in world units.")]
        public float tetherWidth = 0.15f;
        [Tooltip("Seconds the tether takes to whip out from Eko to Nix before she starts " +
                 "travelling along it. Unscaled time.")]
        public float tetherThrowDuration = 0.09f;

        /// <summary>Dash travel time, locked to the fetch orb's single-leg duration so the
        /// dash and a recall feel like matching motions.</summary>
        float DashDuration => Mathf.Max(0.01f, fetchLegDuration);

        [Header("Morph (hold L1)")]
        [Tooltip("Seconds the arrow-to-phantom morph animation takes (unscaled).")]
        public float morphDuration = 0.15f;
        [Tooltip("Phantom's starting scale on morph — grows to 1 over morphDuration.")]
        public float morphStartScale = 0.15f;

        [Header("Fetch (R2 with no arrow)")]
        public float fetchLegDuration = 0.35f;

        /// <summary>True while the phantom is out (morphing, aiming, or waiting to return).</summary>
        public bool PhantomOut => _ui == UiState.Morphing || _ui == UiState.Aiming || _ui == UiState.AwaitingGround;

        /// <summary>True while a fetch orb is in flight.</summary>
        public bool Fetching => _fetching;

        /// <summary>True while the grapple has already been spent this airtime. Reset on ground touch.</summary>
        public bool DashSpentThisAirtime => _dashSpentThisAirtime;

        enum UiState { Idle, Dashing, Morphing, Aiming, AwaitingGround }
        UiState _ui = UiState.Idle;

        // Tap/hold tracking
        float _pressAt;
        bool _consumedPressForHold;

        // Dash state
        enum DashPhase { Throwing, Travelling }
        DashPhase _dashPhase;
        float _dashTimer;
        Vector3 _dashStart;
        Vector3 _dashEnd;
        Vector3 _dashTetherTarget;
        Arrow _dashArrow;
        LineRenderer _dashTether;
        bool _dashSpentThisAirtime;
        /// <summary>Set on a successful dash-tether: the arrow was left stuck AND the phantom is
        /// still standing at the arrow spot. When Nix touches ground again, the phantom orbs
        /// home carrying the arrow (see TickAwaitingGround).</summary>
        bool _dashArrowPendingReturn;

        // Morph / aim state
        float _morphTimer;
        Arrow _morphArrow;
        float _savedTimeScale = 1f;

        // Aim
        Camera _cam;

        // Camera panning
        CameraFollow _cameraFollow;

        bool _fetching;
        Collider2D _ekoCol;

        void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (input == null && player != null) input = player.Input;
            if (bow == null) bow = GetComponentInChildren<Bow>();
            if (nixHealth == null && player != null) nixHealth = player.GetComponent<Health>();
            if (_ekoCol == null && ekoPlayer != null) _ekoCol = ekoPlayer.GetComponent<Collider2D>();
        }

        void OnDisable()
        {
            if (_ui == UiState.Morphing || _ui == UiState.Aiming) RestoreTimeScale();
            if (_ui == UiState.Dashing) EndDash(reclaim: false);
            DestroyDashTether();
            if (eko != null && eko.Active) eko.Dismiss();
            FocusCameraOn(player != null ? player.transform : null, snap: true);
            _ui = UiState.Idle;
            _consumedPressForHold = false;
            _fetching = false;
            _dashArrowPendingReturn = false;
        }

        void Update()
        {
            if (input == null || bow == null || eko == null || player == null || ekoPlayer == null) return;

            if (PauseMenu.IsGameplayPaused)
            {
                if (_ui == UiState.Morphing || _ui == UiState.Aiming) CancelAim();
                return;
            }

            EnsureNoCollisionBetweenNixAndEko();

            // Reset per-airtime resources on ground touch. (Auto-retrieve after a dash is
            // handled by TickAwaitingGround now — the phantom is standing at the arrow spot
            // and orbs home with it.)
            if (player.Grounded) _dashSpentThisAirtime = false;

            switch (_ui)
            {
                case UiState.Idle:            TickIdle();           break;
                case UiState.Dashing:         TickDash();           break;
                case UiState.Morphing:        TickMorph();          break;
                case UiState.Aiming:          TickAim();            break;
                case UiState.AwaitingGround:  TickAwaitingGround(); break;
            }

            // R2 fetch is independent of the phantom state (still allowed while phantom waits).
            HandleFetch();
        }

        // ================================================================== Idle → tap-or-hold
        void TickIdle()
        {
            if (input.EkoPressed)
            {
                _pressAt = Time.unscaledTime;
                _consumedPressForHold = false;
            }

            // Hold detected while pressed: transition to Morphing if an arrow is available.
            if (input.EkoHeld && !_consumedPressForHold
                && Time.unscaledTime - _pressAt > tapHoldThreshold)
            {
                if (PlayerAbilities.MakeShade && bow.LastFiredArrow != null)
                {
                    _consumedPressForHold = true;
                    StartMorph(bow.LastFiredArrow);
                    return;
                }
                _consumedPressForHold = true;   // no arrow to morph — swallow the hold cleanly
            }

            // Tap release: dash to the arrow if it's stuck AND we haven't already dashed this airtime.
            if (input.EkoReleased && !_consumedPressForHold)
            {
                Arrow a = bow.LastFiredArrow;
                if (a != null && a.IsPickup && PlayerAbilities.MakeShade && !_dashSpentThisAirtime)
                    StartDash(a);
            }
        }

        // ================================================================== Dash (tap L1)
        void StartDash(Arrow arrow)
        {
            _ui = UiState.Dashing;
            _dashPhase = DashPhase.Throwing;
            _dashTimer = 0f;
            _dashArrow = arrow;
            _dashStart = player.transform.position;
            _dashTetherTarget = arrow.transform.position;

            Vector3 flight = arrow.transform.right;
            Vector3 arrowPos = arrow.transform.position;
            int facing = flight.x >= 0f ? 1 : -1;
            _dashEnd = arrowPos - flight.normalized * dashLandOffset;

            _dashSpentThisAirtime = true;   // one grapple per airtime; resets on ground touch

            // Eko materialises where the arrow is — he's the one throwing the tether. Aim UI
            // stays off (this isn't a morph aim), and the phantom's PlayerController freezes so
            // his transform doesn't drift. He'll stay standing there through the dash and the
            // post-dash wait, so a follow-up hold-L1 can't spawn a SECOND phantom off the same
            // arrow.
            eko.Summon(arrowPos, facing);
            ekoPlayer.SetFrozen(true);
            SnapPhantomBody(arrowPos, facing);
            eko.AimUiVisible = false;

            player.SetFrozen(true);
            player.SetIntangible(true);
            if (nixHealth != null) nixHealth.GrantInvuln(tetherThrowDuration + DashDuration + dashInvuln);

            // Tether starts as a zero-length line at Eko; the Throwing phase whips it out to Nix.
            SpawnDashTether(arrowPos, arrowPos);
            Sfx.Play(Sfx.Id.EkoZip, 1.05f);
        }

        void TickDash()
        {
            _dashTimer += Time.unscaledDeltaTime;

            if (_dashPhase == DashPhase.Throwing)
            {
                // Tether whips out from Eko (fixed at the arrow spot) toward Nix's start
                // position. Nix hasn't moved yet.
                float u = Mathf.Clamp01(_dashTimer / Mathf.Max(0.01f, tetherThrowDuration));
                float eased = Mathf.SmoothStep(0f, 1f, u);
                Vector3 tetherEnd = Vector3.Lerp(_dashTetherTarget, _dashStart, eased);
                if (_dashTether != null)
                {
                    _dashTether.SetPosition(0, _dashTetherTarget);
                    _dashTether.SetPosition(1, tetherEnd);
                }
                if (u >= 1f) { _dashPhase = DashPhase.Travelling; _dashTimer = 0f; }
                return;
            }

            // Travelling phase — Nix zips along the tether toward Eko/arrow.
            float t = Mathf.Clamp01(_dashTimer / DashDuration);
            float travelEased = Mathf.SmoothStep(0f, 1f, t);
            Vector3 pos = Vector3.Lerp(_dashStart, _dashEnd, travelEased);
            player.transform.position = pos;
            if (player.Rb != null) player.Rb.position = pos;

            if (_dashTether != null)
            {
                _dashTether.SetPosition(0, pos);
                _dashTether.SetPosition(1, _dashTetherTarget);
            }

            if (t >= 1f) EndDash(reclaim: true);
        }

        void EndDash(bool reclaim)
        {
            player.SetIntangible(false);
            player.SetFrozen(false);
            DestroyDashTether();

            if (reclaim && _dashArrow != null)
            {
                // Grant spectral. Arrow stays stuck. Phantom stays out at the arrow spot —
                // transition to AwaitingGround so a follow-up hold-L1 can't spawn another
                // phantom off the same arrow, and the ground-touch handler orbs the phantom
                // home with the arrow.
                bow.GiveArrow(blue: true);
                _dashArrowPendingReturn = true;
                _ui = UiState.AwaitingGround;
            }
            else
            {
                // Dash aborted — quietly retire the phantom too.
                if (eko != null && eko.Active) eko.Vanish();
                _ui = UiState.Idle;
            }
            _dashArrow = null;
            _consumedPressForHold = false;
        }

        // ------------------------------------------------------------------ dash tether
        void SpawnDashTether(Vector3 from, Vector3 to)
        {
            var go = new GameObject("DashTether");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = tetherWidth;
            lr.numCapVertices = 4;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.sortingOrder = 22;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.startColor = tetherColor;
            lr.endColor = tetherColor;
            go.AddComponent<ProceduralLine>();
            _dashTether = lr;
        }

        void DestroyDashTether()
        {
            if (_dashTether != null) Destroy(_dashTether.gameObject);
            _dashTether = null;
        }

        // ================================================================== Morph (hold L1)
        void StartMorph(Arrow arrow)
        {
            _ui = UiState.Morphing;
            _morphTimer = 0f;
            _morphArrow = arrow;

            Vector3 pos = arrow.transform.position;
            Vector3 flight = arrow.transform.right;
            int facing = flight.x >= 0f ? 1 : -1;

            eko.Summon(pos, facing);
            ekoPlayer.SetFrozen(true);
            SnapPhantomBody(pos, facing);
            eko.AimDirection = flight.sqrMagnitude > 0.0001f ? (Vector2)flight.normalized : Vector2.right;
            eko.transform.localScale = Vector3.one * morphStartScale;
            eko.AimUiVisible = false;   // hide reticle/preview during the morph animation

            // Camera snaps to the phantom so the aim opens up already framed on Eko. The snap
            // (not a smooth pan) is important — a smooth pan wouldn't finish before we freeze
            // time on Aiming entry, leaving the aim off-center.
            FocusCameraOn(eko.transform, snap: true);
        }

        void TickMorph()
        {
            // Hold released mid-morph = commit whatever the current aim is (fires spectral).
            if (input.EkoReleased) { FirePhantomAndWait(); return; }

            _morphTimer += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(_morphTimer / Mathf.Max(0.01f, morphDuration));
            float eased = Mathf.SmoothStep(0f, 1f, u);
            eko.transform.localScale = Vector3.one * Mathf.Lerp(morphStartScale, 1f, eased);
            if (_morphArrow != null) _morphArrow.transform.localScale = Vector3.one * (1f - eased);

            if (u >= 1f) FinishMorph();
        }

        void FinishMorph()
        {
            if (_morphArrow != null)
            {
                _morphArrow.MarkReclaimed();
                Destroy(_morphArrow.gameObject);
                _morphArrow = null;
            }
            eko.transform.localScale = Vector3.one;
            eko.AimUiVisible = true;

            // Freeze the world for the aim. Save the current scale so a stacked hitstop can't
            // trample it.
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            _ui = UiState.Aiming;
        }

        // ================================================================== Aim (frozen time)
        void TickAim()
        {
            UpdatePhantomAim();
            if (input.EkoReleased) FirePhantomAndWait();
        }

        void UpdatePhantomAim()
        {
            Vector2 raw = GetRawAim();
            if (raw.sqrMagnitude > 0.0001f) eko.AimDirection = raw.normalized;

            if (Mathf.Abs(eko.AimDirection.x) > 0.1f)
                ekoPlayer.SetFacing(eko.AimDirection.x > 0f ? 1 : -1);
        }

        Vector2 GetRawAim()
        {
            if (input.AimStickActive) return input.AimStickDirection;

            if (input.MouseAiming && Mouse.current != null)
            {
                if (_cam == null) _cam = Camera.main;
                if (_cam != null)
                {
                    Vector3 mp = Mouse.current.position.ReadValue();
                    mp.z = -_cam.transform.position.z;
                    Vector3 world = _cam.ScreenToWorldPoint(mp);
                    Vector2 d = (Vector2)(world - eko.transform.position);
                    if (d.sqrMagnitude > 0.0001f) return d;
                }
            }
            return eko.AimDirection;
        }

        // ================================================================== Fire / cancel
        void FirePhantomAndWait()
        {
            // If the release lands mid-morph, wipe the shrinking arrow now.
            if (_morphArrow != null)
            {
                _morphArrow.MarkReclaimed();
                Destroy(_morphArrow.gameObject);
                _morphArrow = null;
            }

            // Loose the spectral shot. Purely spent-on-use — not registered as LastFiredArrow so
            // it can't be dashed to or morphed. Its only job is catching Nix on the way for the
            // bonus + air jump.
            if (PlayerAbilities.ShadeFireArrow) eko.Loose(bow.ArrowSpeed(), player.Col);

            // The phantom holds position after firing — no orb home yet. It watches Nix until
            // she touches ground; that's the deliberate cost of the morph, and the trigger that
            // hands her normal arrow back on return.
            eko.AimUiVisible = false;
            _ui = UiState.AwaitingGround;

            // Time and camera come back to Nix.
            RestoreTimeScale();
            FocusCameraOn(player.transform, snap: false);   // smooth pan back — travel time is meaningful here
            _consumedPressForHold = false;
        }

        void CancelAim()
        {
            if (_morphArrow != null)
            {
                _morphArrow.MarkReclaimed();
                Destroy(_morphArrow.gameObject);
                _morphArrow = null;
            }
            if (eko.Active) eko.Vanish();
            RestoreTimeScale();
            FocusCameraOn(player.transform, snap: false);
            _ui = UiState.Idle;
            _consumedPressForHold = false;
        }

        void RestoreTimeScale()
        {
            if (PauseMenu.IsGameplayPaused) return;
            if (Time.timeScale == 0f) Time.timeScale = _savedTimeScale > 0f ? _savedTimeScale : 1f;
        }

        // ================================================================== Awaiting ground touch
        void TickAwaitingGround()
        {
            if (!player.Grounded) return;

            // Two flavours of "phantom is standing out waiting for Nix to land":
            //  1. After a dash-tether — the arrow is still stuck at the phantom's spot; the
            //     phantom orbs home carrying it, granting Nix her normal slot back.
            //  2. After a hold-morph fire — the arrow was consumed by the morph; the phantom
            //     orbs home empty-handed and hands the normal slot back on arrival.
            if (_dashArrowPendingReturn) OrbPhantomHomeWithArrow();
            else { eko.DismissWithOrb(); bow.GiveArrow(false); }

            _ui = UiState.Idle;
        }

        /// <summary>Post-dash landing: the phantom is standing at the arrow spot and rides home
        /// carrying it as one orb — the arrow's destroyed, the phantom's dismissed silently
        /// (the orb IS the visual), and Nix's normal slot fills when the orb touches her.</summary>
        void OrbPhantomHomeWithArrow()
        {
            _dashArrowPendingReturn = false;
            Arrow a = bow.LastFiredArrow;
            Vector3 from = eko.Active ? eko.transform.position : player.transform.position;

            if (a != null)
            {
                a.MarkReclaimed();
                Destroy(a.gameObject);
            }
            if (eko.Active) eko.Dismiss();   // silent — the chase orb below is the visual

            _fetching = true;
            EkoOrb.Chase(from, () => player.transform.position, fetchLegDuration, onArrive: () =>
            {
                bow.GiveArrow(false);
                _fetching = false;
            });
        }

        // ================================================================== Fetch (R2)
        void HandleFetch()
        {
            if (_ui != UiState.Idle && _ui != UiState.AwaitingGround) return;
            if (!input.NixBowPressed) return;
            if (bow.HasAnyArrow || _fetching) return;
            if (!player.GroundedForRecoil || !PlayerAbilities.RecallArrow) return;

            Arrow arrow = bow.LastFiredArrow;
            if (arrow == null) return;
            StartFetch(player.transform.position, arrow);
        }

        void StartFetch(Vector3 from, Arrow arrow)
        {
            _fetching = true;
            bool wasBlue = arrow.blue;
            bool grabbed = false;

            EkoOrb.Chase(from, () => ArrowCenter(arrow), fetchLegDuration, onArrive: () =>
            {
                Vector3 grabAt = arrow != null ? ArrowCenter(arrow) : player.transform.position;
                if (arrow != null)
                {
                    grabbed = true;
                    arrow.MarkReclaimed();
                    Destroy(arrow.gameObject);
                }

                EkoOrb.Chase(grabAt, () => player.transform.position, fetchLegDuration, onArrive: () =>
                {
                    if (grabbed) bow.GiveArrow(wasBlue);
                    _fetching = false;
                });
            });
        }

        // ================================================================== Camera
        void FocusCameraOn(Transform target, bool snap)
        {
            if (_cameraFollow == null) _cameraFollow = FindAnyObjectByType<CameraFollow>();
            if (_cameraFollow == null || target == null) return;
            if (snap) _cameraFollow.SnapToTarget(target);
            else _cameraFollow.target = target;
        }

        // ================================================================== helpers
        void SnapPhantomBody(Vector3 pos, int facing)
        {
            if (ekoPlayer.Rb != null)
            {
                ekoPlayer.Rb.position = pos;
                ekoPlayer.Rb.linearVelocity = Vector2.zero;
            }
            ekoPlayer.transform.position = pos;
            ekoPlayer.SetFacing(facing);
            ekoPlayer.Machine.ChangeState(ekoPlayer.Idle);
        }

        static Vector3 ArrowCenter(Arrow arrow)
        {
            if (arrow == null) return Vector3.zero;
            var sr = arrow.GetComponentInChildren<SpriteRenderer>();
            return sr != null ? sr.bounds.center : arrow.transform.position;
        }

        void EnsureNoCollisionBetweenNixAndEko()
        {
            if (_ekoCol == null && ekoPlayer != null) _ekoCol = ekoPlayer.GetComponent<Collider2D>();
            if (player == null || player.Col == null || _ekoCol == null) return;
            Physics2D.IgnoreCollision(player.Col, _ekoCol, true);
        }
    }
}
