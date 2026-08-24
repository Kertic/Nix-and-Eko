using NixAndEko.Player;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// Drives the Eko phantom from Nix's side: press the summon button (R2) while aiming to plant
    /// an echo of Nix where she stands, hold to keep the phantom there, release to make them loose
    /// their shot. Eko (a faerie, they/them) can be summoned even when Nix is out of arrows — the
    /// reticle just greys out — since Eko fires their own arrow, not one of Nix's.
    ///
    /// Eko is a once-per-airtime resource, exactly like the mid-air bow shot: summoning spends
    /// the charge, and it only comes back by touching the ground. So a phantom planted in mid-air
    /// is the only one you get until you land — you have to commit to it, let their arrow fly, and
    /// get back to solid ground before another is available.
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
                 "release for the shot to home in on her. Roughly her own width feels forgiving " +
                 "without auto-hitting from way off the line.")]
        public float assistRadius = 1.25f;

        /// <summary>False once a phantom has been summoned, until Nix next touches the ground.</summary>
        public bool CanSummon => !_spent;

        bool _spent;
        float _charge;

        void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (input == null && player != null) input = player.Input;
            if (bow == null) bow = GetComponentInChildren<Bow>();
        }

        void Update()
        {
            if (input == null || bow == null || eko == null || player == null) return;

            // Landing (or still within the coyote window) is the only thing that gives the
            // phantom back — airtime never refills it.
            if (player.GroundedForRecoil && !eko.Active) _spent = false;

            if (!eko.Active)
            {
                // A phantom is an echo of a shot being lined up, so the bow has to be aimed —
                // but it needn't be a fireable shot, so this works even out of arrows.
                if (input.EkoPressed && CanSummon && bow.IsAiming) Summon();
                return;
            }

            if (input.EkoHeld)
            {
                // Eko holds their draw the whole time they're standing there, so a longer summon
                // means a stronger shot — same draw curve as Nix's own bow.
                _charge = Mathf.Clamp01(_charge + Time.deltaTime / Mathf.Max(0.01f, bow.drawTime));
                eko.UpdatePreview();
                return;
            }

            // Button released: the echo looses what they were holding, then fades. If Nix is
            // sitting on the preview line, the shot homes in on her (aim assist).
            Transform homeTarget = PlayerOnPreviewLine() ? player.transform : null;
            eko.Loose(bow.ArrowSpeed(_charge), _charge, player.Col, homeTarget);
            eko.Dismiss();
        }

        /// <summary>
        /// Is Nix close enough to Eko's straight preview line — ahead of the phantom and within
        /// <see cref="assistRadius"/> of the line — to earn a homing shot? Measured against the
        /// full aim ray (not the wall-clipped visual), so a wall between them doesn't deny the
        /// assist; the homing arrow phases through it anyway.
        /// </summary>
        bool PlayerOnPreviewLine()
        {
            Vector2 origin = eko.transform.position;
            Vector2 dir = eko.AimDirection;
            Vector2 toPlayer = (Vector2)player.transform.position - origin;

            float along = Vector2.Dot(toPlayer, dir);
            if (along < 0f || along > eko.previewDistance) return false;   // behind Eko or too far

            Vector2 perp = toPlayer - dir * along;
            return perp.magnitude <= assistRadius;
        }

        void Summon()
        {
            _spent = true;
            _charge = bow.Charge;
            eko.Summon(player.transform.position, bow.AimDirection, player.Facing, player.groundMask);
            // Mute Nix's own aim now that the gesture belongs to Eko, until the stick goes neutral.
            bow.SuppressUntilRelease();
        }

        void OnDisable()
        {
            if (eko != null && eko.Active) eko.Dismiss();
        }
    }
}
