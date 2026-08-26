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
        [Tooltip("Seconds the fetch orb takes for each leg (Eko -> arrow, then arrow -> Nix) — fixed " +
                 "regardless of distance, so a far-off arrow doesn't take forever to retrieve.")]
        public float fetchLegDuration = 0.35f;
        [Tooltip("Hold the Nix Bow button this long, while a phantom is out and Nix is empty, to " +
                 "force the phantom home and fetch the arrow anyway (a QOL 'L1 then R2' shortcut).")]
        public float fetchChargeTime = 0.6f;

        /// <summary>False once a phantom has been summoned, until Nix next touches the ground.</summary>
        public bool CanSummon => !_spent;

        bool _spent;
        bool _lingering;   // phantom fired mid-air and is standing until Nix lands
        bool _fetching;    // an orb fetch is in flight
        float _fetchCharge; // seconds the forced-fetch hold has been building

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
                // Can't plant a phantom while Eko is off retrieving an arrow.
                if (CanSummon && !_fetching) Summon();
                return;
            }

            ReturnPhantom();
        }

        /// <summary>The L1-on-an-active-phantom behaviour: a prepared shot fires (and then returns or
        /// lingers airborne); a shot-less phantom just returns.</summary>
        void ReturnPhantom()
        {
            if (eko.Prepared) FirePhantom();
            else eko.DismissWithOrb();
        }

        void Summon()
        {
            // Never plant a phantom while Eko is off retrieving an arrow.
            if (_fetching) return;
            _spent = true;
            _lingering = false;
            // A phantom copies a shot only when Nix is actually holding a fireable, aimed arrow.
            bool prepared = bow.IsAiming && bow.HasAnyArrow;
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
            // Fetch only matters while Nix has nothing to fire (otherwise R2 fires her arrow).
            if (bow.HasAnyArrow || _fetching) { _fetchCharge = 0f; return; }

            if (eko.Active)
            {
                // A phantom out on a setup is occupied — flash on the press to say so. But holding
                // R2 charges the phantom up and then forces the whole "L1 to return, R2 to fetch"
                // combo, so the player doesn't have to interrupt the setup by hand. Grounded only.
                if (input.NixBowPressed) eko.FlashBusy();

                if (input.NixBowHeld && player.GroundedForRecoil)
                {
                    _fetchCharge += Time.deltaTime;
                    eko.ChargeVis = _fetchCharge / Mathf.Max(0.01f, fetchChargeTime);
                    if (_fetchCharge >= fetchChargeTime)
                    {
                        _fetchCharge = 0f;
                        ForceFetchFromPhantom();
                    }
                }
                else _fetchCharge = 0f;
                return;
            }

            _fetchCharge = 0f;
            if (input.NixBowPressed) TryStartFetch();
        }

        /// <summary>Send Eko out on the orb fetch, if there's a downed arrow to grab and Nix is grounded.</summary>
        void TryStartFetch()
        {
            if (bow.HasAnyArrow || _fetching) return;
            if (!player.GroundedForRecoil) return;
            Arrow arrow = bow.LastFiredArrow;
            if (arrow == null) return;
            StartFetch(player.transform.position, arrow);
        }

        /// <summary>
        /// The held-R2 override: the phantom looses its prepared shot (if any) and then, instead of
        /// zipping home first, collapses into the fetch orb right where it stood and zooms out to
        /// the arrow before returning to Nix — one continuous path, never two orbs crossing.
        /// </summary>
        void ForceFetchFromPhantom()
        {
            Vector3 ekoPos = eko.transform.position;
            FirePhantomShotOnly();   // loose a prepared shot, if there is one — no separate return orb
            eko.Dismiss();           // silent: the fetch orb below is the phantom's only exit visual

            if (bow.HasAnyArrow || _fetching) return;
            if (!player.GroundedForRecoil) return;
            Arrow arrow = bow.LastFiredArrow;
            if (arrow == null) return;
            StartFetch(ekoPos, arrow);
        }

        /// <summary>Loose the phantom's prepared shot without dismissing it or spawning a return orb.</summary>
        void FirePhantomShotOnly()
        {
            if (!eko.Prepared) return;
            Transform homeTarget = PlayerOnPreviewLine() ? player.transform : null;
            eko.Loose(bow.ArrowSpeed(), 1f, player.Col, homeTarget);
        }

        /// <summary>
        /// Orb out from <paramref name="from"/> to the downed arrow — homing onto its live visual
        /// centre so it always makes contact instead of stopping short at the nock-pivoted transform
        /// — grab it, then orb back to hand it over to Nix. Each leg takes a fixed
        /// <see cref="fetchLegDuration"/> regardless of how far away the arrow landed.
        /// </summary>
        void StartFetch(Vector3 from, Arrow arrow)
        {
            _fetching = true;
            bool wasBlue = arrow.blue;

            EkoOrb.Chase(from, () => ArrowCenter(arrow), fetchLegDuration,
                onArrive: () =>
            {
                Vector3 grabAt = arrow != null ? ArrowCenter(arrow) : player.transform.position;
                if (arrow != null) Destroy(arrow.gameObject);

                EkoOrb.Chase(grabAt, () => player.transform.position, fetchLegDuration,
                    onArrive: () =>
                {
                    bow.GiveArrow(wasBlue);
                    _fetching = false;
                });
            });
        }

        /// <summary>The arrow's true visual centre — its rendered bounds, not the nock-pivoted transform.</summary>
        static Vector3 ArrowCenter(Arrow arrow)
        {
            if (arrow == null) return Vector3.zero;
            var sr = arrow.GetComponentInChildren<SpriteRenderer>();
            return sr != null ? sr.bounds.center : arrow.transform.position;
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
            _fetchCharge = 0f;
        }
    }
}
