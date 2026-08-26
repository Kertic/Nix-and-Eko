using NixAndEko.Player;
using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// Drives the Eko possession and Nix's arrow retrieval.
    ///
    /// <para><b>Eko button (L1)</b> plants Eko where Nix stands and hands control straight to it —
    /// Nix freezes on the spot (her own locomotion keeps ticking with no input, so she still falls
    /// if summoned mid-air) while the player walks/jumps Eko around instead, using Eko's own full
    /// locomotion (<see cref="ekoPlayer"/>, built alongside Nix's — see
    /// <see cref="Level.PlayerFactory"/>). Eko has no bow: the Nix Bow button doesn't fire while
    /// possessed, it tries to send control home instead. That only lands cleanly while Nix is
    /// grounded (an orb carries Eko back and, if they'd grabbed Nix's downed arrow along the way,
    /// hands it over on arrival); pressed while she's still airborne, Eko is yanked out of the
    /// world on the spot instead — a "vanish" that ends the possession early (a carried arrow is
    /// still handed back either way, so a mistimed press can't strand Nix without ammo).</para>
    ///
    /// <para>While possessed, walking Eko close enough to Nix's last fired arrow grabs it (see
    /// <see cref="TryGrabArrow"/>) — the only way to retrieve it in this mode; there's no
    /// automatic fetch any more.</para>
    ///
    /// Eko is a once-per-airtime resource, exactly like the mid-air bow shot: summoning spends the
    /// charge, and it only comes back by touching the ground.
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

        [Header("Arrow grab")]
        [Tooltip("How close Eko must walk to Nix's downed arrow to pick it up.")]
        public float grabRadius = 0.8f;

        /// <summary>False once a phantom has been summoned, until Nix next touches the ground.</summary>
        public bool CanSummon => !_spent;
        /// <summary>True while the player is directly controlling Eko.</summary>
        public bool Possessing => _possessing;

        bool _spent;
        bool _possessing;
        bool _carryingArrow;   // Eko grabbed Nix's downed arrow and is holding it for the return
        CameraFollow _camera;

        void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (input == null && player != null) input = player.Input;
            if (bow == null) bow = GetComponentInChildren<Bow>();
        }

        void Update()
        {
            if (input == null || bow == null || eko == null || player == null ||
                ekoPlayer == null || ekoInput == null) return;

            // Landing gives the phantom back.
            if (player.GroundedForRecoil) _spent = false;

            if (_possessing) { UpdateWhilePossessing(); return; }

            if (input.EkoPressed && CanSummon && !eko.Active) BeginPossession();
        }

        void BeginPossession()
        {
            _spent = true;
            _possessing = true;
            _carryingArrow = false;

            Vector3 pos = player.transform.position;
            eko.Summon(pos, player.Facing);   // sets transform.position — same transform as ekoPlayer

            // Also sync the Rigidbody2D's internal position (a transform write alone doesn't fully
            // relocate an interpolated body), and reset velocity/state to a clean start regardless
            // of whatever Eko was doing the last time it was dismissed.
            ekoPlayer.Rb.position = pos;
            ekoPlayer.Velocity = Vector2.zero;
            ekoPlayer.SetFacing(player.Facing);
            ekoPlayer.Machine.ChangeState(ekoPlayer.Idle);

            // Route the physical input to Eko and mute Nix's own reader.
            input.routed = false;
            ekoInput.routed = true;

            FollowTarget(ekoPlayer.transform);
        }

        void UpdateWhilePossessing()
        {
            TryGrabArrow();

            if (ekoInput.NixBowPressed)
                EndPossession(vanish: !player.GroundedForRecoil);
        }

        /// <summary>Walk Eko close enough to Nix's last fired (and still down) arrow to grab it —
        /// held until the possession ends, then handed to Nix.</summary>
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

        /// <summary>
        /// End the possession. <paramref name="vanish"/> true means Nix wasn't grounded to receive
        /// Eko, so the phantom is yanked out on the spot instead of flying home cleanly. Either way
        /// a carried arrow is handed back — losing the clean return is punishment enough without
        /// also stranding Nix out of ammo.
        /// </summary>
        void EndPossession(bool vanish)
        {
            _possessing = false;
            ekoInput.routed = false;
            input.routed = true;

            if (_carryingArrow)
            {
                bow.GiveArrow(false);
                _carryingArrow = false;
            }

            if (vanish) eko.Vanish();
            else eko.DismissWithOrb();

            FollowTarget(player.transform);
        }

        /// <summary>The arrow's true visual centre — its rendered bounds, not the nock-pivoted transform.</summary>
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
            // Never leave the game soft-locked with Nix's input permanently muted.
            if (_possessing) EndPossession(vanish: true);
        }
    }
}
