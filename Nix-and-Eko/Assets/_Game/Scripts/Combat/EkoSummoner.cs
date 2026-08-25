using NixAndEko.Player;
using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// Drives the Eko phantom (a faerie, they/them) and Nix's arrow retrieval.
    ///
    /// <para><b>Eko button (L1)</b> is a toggle. First press plants an echo of Nix where she stands;
    /// if she was aiming a real (fireable) shot it copies that shot and holds it (aiming pose +
    /// straight preview), otherwise the phantom just stands there. A second press looses the held
    /// shot — one of Eko's own blue arrows, so it doesn't matter whether Nix still holds an arrow —
    /// and then, if Nix is on the ground, the phantom returns; if she's airborne it lingers in a
    /// standing pose until she lands (so the retrieval loop can't be double-dipped). A press while a
    /// shot-less phantom stands simply returns it.</para>
    ///
    /// <para><b>Nix Bow button (R2) while empty</b> sends Eko to fetch the downed arrow: on the
    /// ground, a blue orb zips out to the arrow and back, handing it to Nix. Airborne it does
    /// nothing; and if a phantom is already out, Eko just flashes to show they're occupied — we
    /// never yank a setup phantom to run an errand.</para>
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

        [Header("Aim assist")]
        [Tooltip("How close (perpendicular, world units) Nix must be to Eko's preview line at " +
                 "release for the shot to home in on her.")]
        public float assistRadius = 1.25f;
        [Tooltip("Nix must be at least this far ahead of Eko along the line for a homing shot.")]
        public float assistMinDistance = 1.5f;

        [Header("Fetch")]
        [Tooltip("Seconds for the fetch orb to reach the downed arrow (and again to zip back).")]
        public float fetchHop = 0.18f;

        /// <summary>False once a phantom has been summoned, until Nix next touches the ground.</summary>
        public bool CanSummon => !_spent;

        bool _spent;
        bool _lingering;   // phantom fired mid-air and is standing until Nix lands
        bool _fetching;    // an orb fetch is in flight

        void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (input == null && player != null) input = player.Input;
            if (bow == null) bow = GetComponentInChildren<Bow>();
        }

        void Update()
        {
            if (input == null || bow == null || eko == null || player == null) return;

            // Landing gives the phantom back, and retires a phantom that's been standing since it
            // fired mid-air (returns it home with the orb visual).
            if (player.GroundedForRecoil)
            {
                _spent = false;
                if (_lingering && eko.Active) { eko.DismissWithOrb(); _lingering = false; }
            }

            HandleEkoButton();
            HandleFetchButton();

            if (eko.Active && eko.Prepared) eko.UpdatePreview();
        }

        void HandleEkoButton()
        {
            if (!input.EkoPressed) return;

            if (!eko.Active)
            {
                if (CanSummon) Summon();
                return;
            }

            // Phantom is out: a prepared shot fires (and returns/lingers); a shot-less one just returns.
            if (eko.Prepared) FirePhantom();
            else eko.DismissWithOrb();
        }

        void Summon()
        {
            _spent = true;
            _lingering = false;
            // A phantom copies a shot only when Nix is actually holding a fireable, aimed arrow.
            bool prepared = bow.IsAiming && bow.HasArrow;
            eko.Summon(player.transform.position, bow.AimDirection, player.Facing,
                       player.groundMask, prepared);
        }

        void FirePhantom()
        {
            Transform homeTarget = PlayerOnPreviewLine() ? player.transform : null;
            eko.Loose(bow.ArrowSpeed(), 1f, player.Col, homeTarget);

            // Grounded: return right away. Airborne: linger as a standing phantom until Nix lands,
            // so she can't also fetch her downed arrow while this shot is in play.
            if (player.GroundedForRecoil) eko.DismissWithOrb();
            else { eko.MakeStanding(); _lingering = true; }
        }

        void HandleFetchButton()
        {
            if (!input.NixBowPressed || bow.HasArrow || _fetching) return;

            // A phantom out on a setup is occupied — flash instead of running the errand.
            if (eko.Active) { eko.FlashBusy(); return; }

            // Airborne, or nothing to fetch: no retrieval.
            if (!player.GroundedForRecoil) return;
            Arrow arrow = bow.LastFiredArrow;
            if (arrow == null) return;

            StartFetch(arrow);
        }

        /// <summary>Orb out to the downed arrow, then orb back to hand it over.</summary>
        void StartFetch(Arrow arrow)
        {
            _fetching = true;
            bool wasBlue = arrow.blue;
            Vector3 arrowPos = arrow.transform.position;
            Vector3 nixPos = player.transform.position;

            EkoOrb.Fly(nixPos, arrowPos, fetchHop, onArrive: () =>
            {
                if (arrow != null) Destroy(arrow.gameObject);
                EkoOrb.Fly(arrowPos, player.transform.position, fetchHop, onArrive: () =>
                {
                    bow.GiveArrow(wasBlue);
                    _fetching = false;
                });
            });
        }

        /// <summary>
        /// Is Nix close enough to Eko's straight preview line — ahead of the phantom and within
        /// <see cref="assistRadius"/> of the line — to earn a homing shot?
        /// </summary>
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

        void OnDisable()
        {
            if (eko != null && eko.Active) eko.Dismiss();
            _lingering = false;
            _fetching = false;
        }
    }
}
