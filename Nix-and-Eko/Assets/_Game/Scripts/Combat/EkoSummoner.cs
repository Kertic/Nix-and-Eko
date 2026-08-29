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
    /// <item><b>Tap L1</b> (arrow stuck): Nix dashes along the arrow's flight line to where the
    /// arrow stopped. Brief invulnerability, ends by reclaiming the arrow. Rejected silently if
    /// the arrow is still mid-air (<see cref="Arrow.IsPickup"/> false).</item>
    /// <item><b>Hold L1</b>: after a short tap threshold, the arrow morphs into the phantom in
    /// place. <see cref="Time.timeScale"/> is dropped to 0 — every enemy, projectile, and Nix
    /// herself all freeze, so the aim is <b>bullet-time perfect</b>. The player aims with the same
    /// stick or mouse they'd use for Nix's bow (relative to the phantom's world position, 8-way
    /// snapped for consistency with <see cref="Bow"/>).</item>
    /// <item><b>Release</b>: the phantom looses a blue arrow along the held aim and orbs home.
    /// Time resumes. If the shot catches Nix, <see cref="EkoArrowTarget"/> hands her a bonus
    /// arrow, the momentum kick, glide refill, AND <b>+1 air jump</b>
    /// (<see cref="PlayerController.ExtraJumps"/>).</item>
    /// <item><b>R2 (Nix, no arrow, grounded)</b>: <see cref="TryStartFetch"/> — unchanged.</item>
    /// </list>
    ///
    /// The morph runs on unscaled time so the visual reads at real-world speed even though
    /// gameplay is frozen. Coroutine-free — a small state machine keeps everything inspectable
    /// from the profiler.
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
        [Tooltip("Bright-blue tether tint that zips from Nix to the arrow during the dash.")]
        public Color tetherColor = new Color(0.4f, 0.85f, 1f, 1f);
        [Tooltip("Line width of the tether, in world units.")]
        public float tetherWidth = 0.15f;

        /// <summary>Dash travel time, locked to the fetch orb's single-leg duration so the
        /// dash and a recall feel like matching motions.</summary>
        float DashDuration => Mathf.Max(0.01f, fetchLegDuration);

        [Header("Morph (hold L1)")]
        [Tooltip("Seconds the arrow-to-phantom morph animation takes (unscaled).")]
        public float morphDuration = 0.15f;
        [Tooltip("Phantom's starting scale on morph — grows to 1 over morphDuration.")]
        public float morphStartScale = 0.15f;

        [Header("Aim")]
        public bool eightDirectional = true;
        [Range(0f, 22f)] public float aimHysteresis = 12f;

        [Header("Fetch (R2 with no arrow)")]
        public float fetchLegDuration = 0.35f;

        /// <summary>True while the phantom is standing at the arrow, mid-morph or aiming.</summary>
        public bool PhantomOut => _ui != UiState.Idle && _ui != UiState.Dashing;

        /// <summary>True while a fetch orb is in flight (out to grab Nix's arrow, or coming back).</summary>
        public bool Fetching => _fetching;

        enum UiState { Idle, Dashing, Morphing, Aiming }
        UiState _ui = UiState.Idle;

        // Tap/hold tracking
        float _pressAt;
        bool _consumedPressForHold;   // set once we transition from Idle → Morphing; blocks tap-release

        // Dash state
        float _dashTimer;
        Vector3 _dashStart;
        Vector3 _dashEnd;
        Vector3 _dashTetherTarget;      // where the tether's far end is anchored (arrow's spot)
        Arrow _dashArrow;
        LineRenderer _dashTether;       // spawned on dash start, destroyed on end

        // Morph / aim state
        float _morphTimer;
        Arrow _morphArrow;
        float _savedTimeScale = 1f;

        // Aim resolution
        int _aimSector;
        bool _snapNow;
        bool _aimFromStickLast;
        Camera _cam;

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
            // Never leave time frozen or Nix intangible if this component tears down mid-flow.
            if (_ui == UiState.Morphing || _ui == UiState.Aiming) RestoreTimeScale();
            if (_ui == UiState.Dashing) EndDash(reclaim: false);
            DestroyDashTether();
            if (eko != null && eko.Active) eko.Dismiss();
            _ui = UiState.Idle;
            _consumedPressForHold = false;
            _fetching = false;
        }

        void Update()
        {
            if (input == null || bow == null || eko == null || player == null || ekoPlayer == null) return;

            // Pause menu → let it handle timescale, roll back our own state cleanly.
            if (PauseMenu.IsGameplayPaused)
            {
                if (_ui == UiState.Morphing || _ui == UiState.Aiming) CancelAim();
                return;
            }

            EnsureNoCollisionBetweenNixAndEko();

            switch (_ui)
            {
                case UiState.Idle:     TickIdle();     break;
                case UiState.Dashing:  TickDash();     break;
                case UiState.Morphing: TickMorph();    break;
                case UiState.Aiming:   TickAim();      break;
            }

            // R2 fetch is independent of the phantom state.
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
                // No arrow to morph — swallow the hold so releasing doesn't dash to nothing.
                _consumedPressForHold = true;
            }

            // Tap release: run the dash if the arrow is stuck.
            if (input.EkoReleased && !_consumedPressForHold)
            {
                Arrow a = bow.LastFiredArrow;
                if (a != null && a.IsPickup && PlayerAbilities.MakeShade) StartDash(a);
            }
        }

        // ================================================================== Dash (tap L1)
        void StartDash(Arrow arrow)
        {
            _ui = UiState.Dashing;
            _dashTimer = 0f;
            _dashArrow = arrow;
            _dashStart = player.transform.position;
            _dashTetherTarget = arrow.transform.position;

            Vector3 flight = arrow.transform.right;
            _dashEnd = arrow.transform.position - flight.normalized * dashLandOffset;

            player.SetFrozen(true);           // no state ticks, no physics motion
            player.SetIntangible(true);       // out of the simulation while the transform lerps
            if (nixHealth != null) nixHealth.GrantInvuln(DashDuration + dashInvuln);

            SpawnDashTether(_dashStart, _dashTetherTarget);
            Sfx.Play(Sfx.Id.EkoZip, 1.05f);
        }

        void TickDash()
        {
            _dashTimer += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(_dashTimer / DashDuration);
            float eased = Mathf.SmoothStep(0f, 1f, u);
            Vector3 pos = Vector3.Lerp(_dashStart, _dashEnd, eased);
            player.transform.position = pos;
            if (player.Rb != null) player.Rb.position = pos;   // keep the frozen body in sync

            // Tether: near end tracks Nix's live position, far end stays anchored on the arrow.
            // Reads as "Nix being reeled in along the blue line" — the same feel as the fetch orb
            // travelling out, but visualised as a tether instead of an orb.
            if (_dashTether != null)
            {
                _dashTether.SetPosition(0, pos);
                _dashTether.SetPosition(1, _dashTetherTarget);
            }

            if (u >= 1f) EndDash(reclaim: true);
        }

        void EndDash(bool reclaim)
        {
            player.SetIntangible(false);
            player.SetFrozen(false);
            DestroyDashTether();

            if (reclaim && _dashArrow != null)
            {
                _dashArrow.MarkReclaimed();
                Destroy(_dashArrow.gameObject);
                bow.GiveArrow(false);
            }
            _dashArrow = null;
            _ui = UiState.Idle;
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
            // ProceduralLine assigns a valid Sprites/Default material so the LineRenderer
            // draws in Play Mode without a material asset to reference — same pattern the
            // Bow trajectory and Eko preview use.
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

            // Prime the aim tracker so the first live aim update reads cleanly.
            _snapNow = true;
            _aimFromStickLast = input.AimStickActive;

            // Freeze gameplay. Save current scale so a stacked hitstop can't be trampled.
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        void TickMorph()
        {
            // Hold released mid-morph = commit whatever the current aim is. It's ambiguous —
            // treat as fire so a released button never leaves the game silently paused.
            if (input.EkoReleased) { FirePhantomAndExit(); return; }

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
            _ui = UiState.Aiming;
        }

        // ================================================================== Aim (frozen time)
        void TickAim()
        {
            UpdatePhantomAim();

            if (input.EkoReleased) FirePhantomAndExit();
        }

        void UpdatePhantomAim()
        {
            Vector2 raw = GetRawAim();
            eko.AimDirection = eightDirectional
                ? SnapEight(raw)
                : (raw.sqrMagnitude > 0.0001f ? raw.normalized : eko.AimDirection);

            if (Mathf.Abs(eko.AimDirection.x) > 0.1f)
                ekoPlayer.SetFacing(eko.AimDirection.x > 0f ? 1 : -1);
        }

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
                    Vector2 d = (Vector2)(world - eko.transform.position);
                    if (d.sqrMagnitude > 0.0001f) return d;
                }
            }
            return eko.AimDirection;
        }

        Vector2 SnapEight(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.0001f) return SectorToDir(_aimSector);

            if (_snapNow)
            {
                _snapNow = false;
                int nearest = Mathf.RoundToInt(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg / 45f);
                _aimSector = ((nearest % 8) + 8) % 8;
                return SectorToDir(_aimSector);
            }

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

        // ================================================================== Fire / cancel
        void FirePhantomAndExit()
        {
            // If we're still mid-morph and never took the arrow, kill it now so it doesn't
            // linger tiny.
            if (_morphArrow != null)
            {
                _morphArrow.MarkReclaimed();
                Destroy(_morphArrow.gameObject);
                _morphArrow = null;
            }

            if (PlayerAbilities.ShadeFireArrow)
            {
                // The phantom's shot IS Nix's arrow relocated (see Eko.Loose). Register it as
                // the new LastFiredArrow so the next L1 tap dashes to it and R2 recalls it —
                // and so a shot that never lands falls back through Arrow's safety-net grant,
                // handing Nix her normal arrow back rather than soft-locking her out.
                Arrow shot = eko.Loose(bow.ArrowSpeed(), player.Col);
                if (shot != null) bow.SetLastFiredArrow(shot);
            }
            eko.DismissWithOrb();
            RestoreTimeScale();
            _ui = UiState.Idle;
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
            _ui = UiState.Idle;
            _consumedPressForHold = false;
        }

        void RestoreTimeScale()
        {
            // The pause menu owns timescale while it's open — never lift its freeze from under it.
            if (PauseMenu.IsGameplayPaused) return;
            // Otherwise, only restore when we still own the freeze (avoid stomping a hitstop that
            // happened to overlap the end of our aim).
            if (Time.timeScale == 0f) Time.timeScale = _savedTimeScale > 0f ? _savedTimeScale : 1f;
        }

        // ================================================================== Fetch (R2)
        void HandleFetch()
        {
            if (_ui != UiState.Idle) return;   // no fetching mid-dash or mid-aim
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
