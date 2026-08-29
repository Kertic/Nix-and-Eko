using NixAndEko.Environment;
using NixAndEko.Player;
using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// Owns the whole Eko possession cycle. The button flow across a summon is:
    ///
    /// <list type="number">
    /// <item><b>L1 (Nix)</b>: <see cref="BeginPossession"/> — plant Eko on Nix's spot, hand
    /// control to it, and drop Nix into ghost mode (crouched, translucent, intangible, frozen).</item>
    /// <item><b>L1 (Eko)</b>: <see cref="FreezeAndReturn"/> — hand control back to Nix, leaving
    /// Eko frozen where it stood with its current aim preserved. Nix wakes back up.</item>
    /// <item><b>R2 (Eko)</b>: <see cref="FireImmediate"/> — a shortcut that does the L1-then-L1
    /// beat in one press: freeze + return + fire, all in the same frame.</item>
    /// <item><b>L1 (Nix, planted phantom out)</b>: <see cref="FireOrReturnPhantom"/> — if the
    /// player actually aimed Eko during the possession the shot fires (and the phantom orbs
    /// home); otherwise the phantom just orbs home without firing.</item>
    /// <item><b>R2 (Nix, no arrow in hand)</b>: <see cref="TryStartFetch"/> — sends Eko out to
    /// grab Nix's downed arrow and hand it back. If a planted phantom is standing, that phantom
    /// vanishes first and the fetch orb sets off from wherever it stood.</item>
    /// </list>
    ///
    /// While possessed, walking Eko close enough to Nix's downed arrow grabs it too — that's
    /// a separate, direct retrieval path. Eko is a once-per-airtime resource: summoning spends
    /// the charge, and it only comes back when Nix actually stands on ground again.
    /// </summary>
    public class EkoSummoner : MonoBehaviour
    {
        [Header("References")]
        public PlayerController player;
        public PlayerInputReader input;
        public Bow bow;
        public Eko eko;
        [Tooltip("Eko's own locomotion controller — built alongside Nix's, kept dormant between possessions.")]
        public PlayerController ekoPlayer;
        [Tooltip("Eko's own input reader, over the same actions asset as Nix's — see PlayerInputReader.routed.")]
        public PlayerInputReader ekoInput;
        [Tooltip("Nix's Health, granted infinite invuln while she's in ghost mode so nothing can " +
                 "hurt the intangible target.")]
        public Health nixHealth;
        [Tooltip("Nix's sprite renderer — dimmed to translucent while she's in ghost mode.")]
        public SpriteRenderer nixSprite;

        [Header("Arrow grab")]
        [Tooltip("How close Eko must walk to Nix's downed arrow to pick it up.")]
        public float grabRadius = 0.8f;

        [Header("Auto-aim (Eko's shot homes on Nix if she's on its preview line)")]
        [Tooltip("How close (perpendicular, world units) Nix must be to Eko's preview line at " +
                 "release for the shot to home in on her.")]
        public float assistRadius = 1.25f;
        [Tooltip("Nix must be at least this far ahead of Eko along the line for a homing shot — " +
                 "keeps a point-blank fire from auto-catching before the arrow even reads as launched.")]
        public float assistMinDistance = 1.5f;

        [Header("Fetch (R2 with no arrow)")]
        [Tooltip("Seconds the fetch orb takes for each leg (Eko/phantom -> arrow, then arrow -> " +
                 "Nix) — fixed regardless of distance, so a far-off arrow doesn't take forever.")]
        public float fetchLegDuration = 0.35f;

        [Header("Ghost mode look")]
        [Tooltip("Nix's sprite alpha while she's the intangible ghost — 1 = normal, 0 = invisible.")]
        [Range(0f, 1f)]
        public float ghostAlpha = 0.45f;

        /// <summary>False once a phantom has been summoned this airtime, until Nix stands on ground again.</summary>
        public bool CanSummon => !_spent;
        /// <summary>True while the player is directly controlling Eko.</summary>
        public bool Possessing => _possessing;
        /// <summary>True while a fetch orb is currently in flight (out to grab Nix's arrow, or
        /// coming back). Consumed by <see cref="Player.PlayerVisuals"/> to hide the head-orbit
        /// Eko ball while the phantom is out doing the retrieval.</summary>
        public bool Fetching => _fetching;

        bool _spent;
        bool _possessing;
        bool _carryingArrow;
        bool _fetching;   // an orb fetch is in flight (Eko is busy — no summon, no re-fetch)
        CameraFollow _camera;
        Color _nixOriginalColor;
        bool _nixColorCached;
        Collider2D _ekoCol;
        /// <summary>Nix's velocity at the moment she went ghost. Restored on ExitGhostMode so
        /// summoning Eko mid-flight (running jump, air-shot recoil, etc.) doesn't kill her run
        /// — she picks up exactly where she left off when control comes back.</summary>
        Vector2 _nixCachedVelocity;

        void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (input == null && player != null) input = player.Input;
            if (bow == null) bow = GetComponentInChildren<Bow>();
            if (nixHealth == null && player != null) nixHealth = player.GetComponent<Health>();
            if (nixSprite == null && player != null && player.spriteRoot != null)
                nixSprite = player.spriteRoot.GetComponent<SpriteRenderer>();
            if (_ekoCol == null && ekoPlayer != null) _ekoCol = ekoPlayer.GetComponent<Collider2D>();
        }

        void Update()
        {
            if (input == null || bow == null || eko == null || player == null ||
                ekoPlayer == null || ekoInput == null) return;

            // Nix and the phantom must never physically shove each other. The pair was set at
            // build time, but Unity clears Physics2D.IgnoreCollision pairs on some transitions
            // (Rb.simulated toggles for ghost mode, bodyType flips on Kinematic freeze, and any
            // collider that goes disabled/enabled), and every one of those happens across a
            // possession cycle. Re-asserting here is O(1) and immune to which of the two bodies
            // was the last to change state.
            EnsureNoCollisionBetweenNixAndEko();

            // Ghost-mode Nix reports Grounded = false (physics is suspended); still, once she's
            // back in control and touches ground, the summon recharges.
            if (!_possessing && player.GroundedForRecoil) _spent = false;

            // Keep Nix invuln topped up while she's ghosting (rather than one big grant that could
            // race with an existing shorter timer).
            if (_possessing && nixHealth != null) nixHealth.GrantInvuln(0.2f);

            if (_possessing) UpdateWhilePossessing();
            else UpdateWhileNixControlled();
        }

        // ------------------------------------------------------------------ Nix-side input
        void UpdateWhileNixControlled()
        {
            // R2 with no arrow in hand sends Eko out to fetch, regardless of what Eko is doing —
            // a planted phantom vanishes on the spot so the fetch orb can leave from where it
            // stood; a dormant Eko sends the orb from Nix. Grounded-only (coyote counts): a
            // desperate airborne R2 can't summon a fetch, since the whole return loop is meant
            // to close on Nix's feet. Bow.Update already only fires R2 when Nix has an arrow, so
            // this branch is unambiguous.
            if (input.NixBowPressed && !bow.HasAnyArrow && !_fetching && player.GroundedForRecoil
                && PlayerAbilities.RecallArrow)
            {
                TryStartFetch();
                return;
            }

            if (!input.EkoPressed) return;

            if (eko.Active && eko.Frozen)
            {
                // Planted phantom out and Nix pressed L1 — fire it (or, if the shade-fire-arrow
                // ability is locked, just dismiss the phantom back with an orb; the shade can
                // still be summoned and planted, it just can't loose). The phantom always has a
                // valid held aim (defaults to Eko's facing on summon, updates whenever the player
                // aims during the possession), so this is unconditional now: previously a "never
                // aimed → dismiss without firing" branch made it look like L1 was doing nothing,
                // which is the opposite of what a player pressing L1 on a planted phantom expects.
                if (PlayerAbilities.ShadeFireArrow) FirePhantom();
                else eko.DismissWithOrb();
                return;
            }

            if (!eko.Active && CanSummon && !_fetching && PlayerAbilities.MakeShade) BeginPossession();
        }

        /// <summary>Kick off the fetch orb. Bails silently if there's no downed arrow to grab
        /// (Nix must be genuinely empty, not just aiming empty-handed at nothing). If a planted
        /// phantom is out, the phantom is snapshotted and vanishes; the orb takes three legs
        /// (phantom → arrow → Nix → phantom's old spot) and Eko reforms in the same setup at the
        /// end, so a fetch mid-setup doesn't cost you the aim you'd lined up.</summary>
        void TryStartFetch()
        {
            Arrow arrow = bow.LastFiredArrow;
            if (arrow == null) return;

            if (eko.Active && eko.Frozen)
            {
                // Snapshot the phantom, vanish it (the burst doubles as the orb-separating beat),
                // and fire the three-leg fetch that reforms it at the end.
                Vector3 spot = eko.transform.position;
                int facing = ekoPlayer.Facing;
                Vector2 aim = eko.AimDirection;
                bool hadAim = eko.HasAim;
                eko.Vanish();
                StartFetchAndReformPhantom(spot, arrow, spot, facing, aim, hadAim);
                return;
            }

            // Dormant Eko: plain two-leg fetch that leaves from Nix.
            StartFetch(player.transform.position, arrow);
        }

        /// <summary>Two-leg orb fetch — out from <paramref name="from"/> to the downed arrow
        /// (homing on its live visual centre so it always makes contact), then back to Nix. Each
        /// leg takes a fixed <see cref="fetchLegDuration"/> regardless of distance.</summary>
        void StartFetch(Vector3 from, Arrow arrow)
        {
            _fetching = true;
            bool wasBlue = arrow.blue;
            bool grabbed = false;   // did we actually take an arrow off the ground?

            EkoOrb.Chase(from, () => ArrowCenter(arrow), fetchLegDuration, onArrive: () =>
            {
                Vector3 grabAt = arrow != null ? ArrowCenter(arrow) : player.transform.position;
                if (arrow != null)
                {
                    grabbed = true;
                    // MarkReclaimed silences Arrow.OnDestroy's safety-net re-grant: it fires for
                    // an in-flight arrow being destroyed, and would hand Nix a fresh arrow the
                    // instant the fetch orb touched the shot — the whole point of the fetch is
                    // that the arrow rides the ORB back to her, so the grant belongs on the
                    // return leg's onArrive below, not here. Landed pickups (`_stuck`) already
                    // skip the safety net; this covers the in-flight case.
                    arrow.MarkReclaimed();
                    Destroy(arrow.gameObject);
                }

                EkoOrb.Chase(grabAt, () => player.transform.position, fetchLegDuration, onArrive: () =>
                {
                    // Only grant an arrow if we actually collected one — otherwise Nix reclaimed
                    // it via walk-over mid-fetch and this second GiveArrow would duplicate the
                    // stock (letting her fire a fresh arrow while the previous one is still in
                    // the world). Handles the timing race between TryReclaim and this callback.
                    if (grabbed) bow.GiveArrow(wasBlue);
                    _fetching = false;
                });
            });
        }

        /// <summary>Three-leg fetch that reforms the phantom at <paramref name="reformAt"/> with
        /// the snapshotted aim after handing the arrow back — the fetch borrows Eko without
        /// dismantling a setup shot. Legs: <paramref name="from"/> → arrow → Nix → reformAt.</summary>
        void StartFetchAndReformPhantom(Vector3 from, Arrow arrow, Vector3 reformAt,
                                        int facing, Vector2 aim, bool hadAim)
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
                    // MarkReclaimed silences Arrow.OnDestroy's safety-net re-grant: it fires for
                    // an in-flight arrow being destroyed, and would hand Nix a fresh arrow the
                    // instant the fetch orb touched the shot — the whole point of the fetch is
                    // that the arrow rides the ORB back to her, so the grant belongs on the
                    // return leg's onArrive below, not here. Landed pickups (`_stuck`) already
                    // skip the safety net; this covers the in-flight case.
                    arrow.MarkReclaimed();
                    Destroy(arrow.gameObject);
                }

                EkoOrb.Chase(grabAt, () => player.transform.position, fetchLegDuration, onArrive: () =>
                {
                    // See StartFetch: only grant an arrow if we actually collected one, so a
                    // walk-over reclaim that raced this fetch can't double up Nix's stock.
                    if (grabbed) bow.GiveArrow(wasBlue);

                    EkoOrb.Chase(player.transform.position, () => reformAt, fetchLegDuration, onArrive: () =>
                    {
                        ReformPlantedPhantom(reformAt, facing, aim, hadAim);
                        _fetching = false;
                    });
                });
            });
        }

        /// <summary>Re-materialise the phantom exactly where it was before a fetch trip, holding
        /// the aim it had. Bypasses <see cref="BeginPossession"/> — this isn't a fresh summon and
        /// mustn't spend a summon charge or hand control to Eko, only restore the planted state.</summary>
        void ReformPlantedPhantom(Vector3 pos, int facing, Vector2 aim, bool hadAim)
        {
            eko.Summon(pos, facing, player.groundMask);
            SnapEkoBody(pos, facing);
            eko.OverrideAim(aim, hadAim);
            eko.FreezeInPlace();
        }

        // ------------------------------------------------------------------ Eko-side input
        void UpdateWhilePossessing()
        {
            // Note: no walk-over arrow pickup during possession. Nix's dropped arrow is only
            // retrieved by SHOOTING the frozen phantom with it (Eko.OnNixArrowHit) — a walking
            // phantom just passes over the arrow without touching it. This keeps the retrieval
            // beat a deliberate one-shot rather than an incidental collision.

            // R2 during possession = "commit and fire" in one press — a shortcut for L1-then-L1.
            // If the shade can't fire arrows (ability locked), collapse the shortcut to a plain
            // FreezeAndReturn so R2 still hands control back cleanly instead of dead-ending.
            if (ekoInput.NixBowPressed)
            {
                if (PlayerAbilities.ShadeFireArrow) FireImmediate();
                else FreezeAndReturn();
                return;
            }

            // L1 while controlling Eko = hand control back to Nix, leave Eko planted with aim.
            if (ekoInput.EkoPressed) FreezeAndReturn();
        }

        // ------------------------------------------------------------------ transitions
        void BeginPossession()
        {
            _spent = true;
            _possessing = true;
            _carryingArrow = false;

            Vector3 pos = player.transform.position;
            eko.Summon(pos, player.Facing, player.groundMask);
            SnapEkoBody(pos, player.Facing);

            // Route input and swap cameras.
            input.routed = false;
            ekoInput.routed = true;
            FollowTarget(ekoPlayer.transform);

            // Nix goes ghost: frozen in place, invulnerable, translucent, crouched pose.
            EnterGhostMode();
        }

        /// <summary>Hand control back to Nix, leaving Eko frozen where it is with its current aim.
        /// Any arrow Eko was carrying goes back to Nix in the same beat.</summary>
        void FreezeAndReturn()
        {
            _possessing = false;
            ekoInput.routed = false;
            input.routed = true;

            eko.FreezeInPlace();
            HandBackCarriedArrow();
            ExitGhostMode();
            FollowTarget(player.transform);
        }

        /// <summary>R2 shortcut during possession: freeze + return + fire immediately, so the
        /// player can commit an aimed shot without the L1-then-L1 double beat.</summary>
        void FireImmediate()
        {
            // Same wind-down as FreezeAndReturn, but the aim we fire on is whatever's live *now*
            // (before we mark HasAim off). Force HasAim on so a straight R2 mash with no aim still
            // fires along the default direction — the player asked to shoot, honour it.
            _possessing = false;
            ekoInput.routed = false;
            input.routed = true;

            eko.FreezeInPlace();
            HandBackCarriedArrow();
            ExitGhostMode();
            FollowTarget(player.transform);

            FirePhantom();
        }

        /// <summary>Nix-side L1 on a planted phantom: fire if it was aimed, otherwise just return.</summary>
        void FireOrReturnPhantom()
        {
            if (eko.HasAim) FirePhantom();
            else eko.DismissWithOrb();
        }

        void FirePhantom()
        {
            Transform homeTarget = PlayerOnPreviewLine() ? player.transform : null;
            eko.Loose(bow.ArrowSpeed(), player.Col, homeTarget);
            eko.DismissWithOrb();
        }

        /// <summary>Is Nix close enough to Eko's straight preview line — ahead of the phantom and
        /// within <see cref="assistRadius"/> of the line — to earn a homing shot?</summary>
        bool PlayerOnPreviewLine()
        {
            Vector2 origin = eko.transform.position;
            Vector2 dir = eko.AimDirection;
            Vector2 toPlayer = (Vector2)player.transform.position - origin;

            float along = Vector2.Dot(toPlayer, dir);
            if (along < assistMinDistance || along > eko.previewDistance) return false;

            Vector2 perp = toPlayer - dir * along;
            return perp.magnitude <= assistRadius;
        }

        // ------------------------------------------------------------------ Nix ghost mode
        void EnterGhostMode()
        {
            // Snapshot Nix's velocity BEFORE SetFrozen zeroes it, so ExitGhostMode can hand it
            // back and she resumes with whatever momentum she had at summon time.
            _nixCachedVelocity = player.Velocity;

            player.ForceGhostPose = true;
            player.SetFrozen(true);
            player.SetIntangible(true);   // ghost Nix: nothing collides with her, nothing senses her
            if (nixSprite != null)
            {
                if (!_nixColorCached) { _nixOriginalColor = nixSprite.color; _nixColorCached = true; }
                var c = _nixOriginalColor; c.a = ghostAlpha;
                nixSprite.color = c;
            }
            if (nixHealth != null) nixHealth.GrantInvuln(9999f);
        }

        void ExitGhostMode()
        {
            player.SetIntangible(false);
            player.SetFrozen(false);
            // SetFrozen(false) zeroes velocity to wake cleanly — restore the pre-summon momentum
            // AFTER it so a running/jumping Nix picks back up where she left off. Also drop her
            // state machine into Fall if she was airborne when frozen, since Sense() hasn't run
            // yet this frame and Idle would otherwise think she's grounded for one tick.
            player.Velocity = _nixCachedVelocity;
            if (_nixCachedVelocity.sqrMagnitude > 0.0001f)
                player.Machine.ChangeState(_nixCachedVelocity.y > 0.01f ? (Core.IState)player.Jump : player.Fall);
            player.ForceGhostPose = false;
            if (nixSprite != null && _nixColorCached) nixSprite.color = _nixOriginalColor;
            // Clear the huge invuln grant — leave a normal short one so she isn't hit instantly on wake.
            if (nixHealth != null) nixHealth.GrantInvuln(0.3f);
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>Snap Eko's body to <paramref name="pos"/> facing <paramref name="facing"/> at
        /// a dead stop — the transform, the interpolated rigidbody, and the state machine.</summary>
        void SnapEkoBody(Vector3 pos, int facing)
        {
            ekoPlayer.SetFrozen(false);   // just in case a previous dismiss left it suspended
            if (ekoPlayer.Rb != null) ekoPlayer.Rb.position = pos;
            ekoPlayer.transform.position = pos;
            ekoPlayer.Velocity = Vector2.zero;
            ekoPlayer.SetFacing(facing);
            ekoPlayer.Machine.ChangeState(ekoPlayer.Idle);
        }

        void TryGrabArrow()
        {
            if (_carryingArrow) return;

            Arrow arrow = bow.LastFiredArrow;
            if (arrow == null) return;

            Vector3 center = ArrowCenter(arrow);
            if (Vector2.Distance(ekoPlayer.transform.position, center) > grabRadius) return;

            Destroy(arrow.gameObject);
            _carryingArrow = true;
            Sfx.Play(Sfx.Id.EkoCatch);
            Particle.Burst(center, Palette.Blue, 10, 5f);
        }

        void HandBackCarriedArrow()
        {
            if (!_carryingArrow) return;
            bow.GiveArrow(false);
            _carryingArrow = false;
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
            // Physics2D.IgnoreCollision is a per-pair write and Unity has no cheap "is this pair
            // ignored?" query — just re-assert. The internal cost is a small hash lookup.
            Physics2D.IgnoreCollision(player.Col, _ekoCol, true);
        }

        /// <summary>Point the main camera at whoever's being controlled. Looked up lazily (and
        /// cached) rather than wired at build time — the camera isn't set up yet when the player is
        /// spawned; see <see cref="Level.LevelLoader"/> / the test-level builder.</summary>
        void FollowTarget(Transform target)
        {
            if (_camera == null) _camera = FindAnyObjectByType<CameraFollow>();
            if (_camera != null) _camera.target = target;
        }

        void OnDisable()
        {
            // Never leave the game soft-locked with Nix's input muted or her sprite ghosted.
            if (_possessing)
            {
                _possessing = false;
                if (ekoInput != null) ekoInput.routed = false;
                if (input != null) input.routed = true;
                ExitGhostMode();
                if (eko != null && eko.Active) eko.Vanish();
            }
            _fetching = false;
        }
    }
}
