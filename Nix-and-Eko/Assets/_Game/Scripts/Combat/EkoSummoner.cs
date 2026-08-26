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

        bool _spent;
        bool _possessing;
        bool _carryingArrow;
        bool _fetching;   // an orb fetch is in flight (Eko is busy — no summon, no re-fetch)
        CameraFollow _camera;
        Color _nixOriginalColor;
        bool _nixColorCached;

        void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (input == null && player != null) input = player.Input;
            if (bow == null) bow = GetComponentInChildren<Bow>();
            if (nixHealth == null && player != null) nixHealth = player.GetComponent<Health>();
            if (nixSprite == null && player != null && player.spriteRoot != null)
                nixSprite = player.spriteRoot.GetComponent<SpriteRenderer>();
        }

        void Update()
        {
            if (input == null || bow == null || eko == null || player == null ||
                ekoPlayer == null || ekoInput == null) return;

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
            // R2 with no arrow in hand always sends Eko out to fetch, regardless of what Eko is
            // doing right now — a planted phantom vanishes on the spot so the fetch orb can leave
            // from where it stood; a dormant Eko sends the orb from Nix. Bow.Update already only
            // fires on R2 when Nix has an arrow, so this branch is unambiguous.
            if (input.NixBowPressed && !bow.HasAnyArrow && !_fetching)
            {
                TryStartFetch();
                return;
            }

            if (!input.EkoPressed) return;

            if (eko.Active && eko.Frozen)
            {
                // Planted phantom out and Nix pressed L1 — fire it (or dismiss cleanly if no aim was set).
                FireOrReturnPhantom();
                return;
            }

            if (!eko.Active && CanSummon && !_fetching) BeginPossession();
        }

        /// <summary>Kick off the fetch orb. Bails silently if there's no downed arrow to grab
        /// (Nix must be genuinely empty, not just aiming empty-handed at nothing).</summary>
        void TryStartFetch()
        {
            Arrow arrow = bow.LastFiredArrow;
            if (arrow == null) return;

            // A planted phantom vanishes on the spot and the orb leaves from where it stood — the
            // vanish burst doubles as the "orb is separating" beat. If Eko was dormant, the orb
            // just sets off from Nix.
            Vector3 from = player.transform.position;
            if (eko.Active)
            {
                from = eko.transform.position;
                eko.Vanish();
            }

            StartFetch(from, arrow);
        }

        /// <summary>Orb out from <paramref name="from"/> to the downed arrow (homing on its live
        /// visual centre so it always makes contact), grab it, then orb back to hand it to Nix.
        /// Each leg takes a fixed <see cref="fetchLegDuration"/> regardless of distance.</summary>
        void StartFetch(Vector3 from, Arrow arrow)
        {
            _fetching = true;
            bool wasBlue = arrow.blue;

            EkoOrb.Chase(from, () => ArrowCenter(arrow), fetchLegDuration, onArrive: () =>
            {
                Vector3 grabAt = arrow != null ? ArrowCenter(arrow) : player.transform.position;
                if (arrow != null) Destroy(arrow.gameObject);

                EkoOrb.Chase(grabAt, () => player.transform.position, fetchLegDuration, onArrive: () =>
                {
                    bow.GiveArrow(wasBlue);
                    _fetching = false;
                });
            });
        }

        // ------------------------------------------------------------------ Eko-side input
        void UpdateWhilePossessing()
        {
            TryGrabArrow();

            // R2 during possession = "commit and fire" in one press — a shortcut for L1-then-L1.
            if (ekoInput.NixBowPressed) { FireImmediate(); return; }

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
            eko.Loose(bow.ArrowSpeed(), player.Col);
            eko.DismissWithOrb();
        }

        // ------------------------------------------------------------------ Nix ghost mode
        void EnterGhostMode()
        {
            player.ForceGhostPose = true;
            player.SetFrozen(true);
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
            player.SetFrozen(false);
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
