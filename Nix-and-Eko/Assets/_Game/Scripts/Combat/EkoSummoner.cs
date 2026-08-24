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

            // Button released: the echo looses what they were holding, then fades.
            eko.Loose(bow.ArrowSpeed(_charge), player.Col);
            eko.Dismiss();
        }

        void Summon()
        {
            _spent = true;
            _charge = bow.Charge;
            eko.Summon(player.transform.position, bow.AimDirection, player.Facing, player.groundMask);
        }

        void OnDisable()
        {
            if (eko != null && eko.Active) eko.Dismiss();
        }
    }
}
