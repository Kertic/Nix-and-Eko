using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// Sits on Nix and catches Eko's arrows. A hit pops a small blue burst, reloads Nix's air
    /// shot and tops her glide meter back up — so a well-placed echo lets her fire (and so recoil)
    /// and glide again without ever touching the ground. Nix's own arrows never reach this: the
    /// Bow tells them to ignore her collider.
    /// </summary>
    public class EkoArrowTarget : MonoBehaviour, IArrowHittable
    {
        public Bow bow;
        public NixAndEko.Player.PlayerController player;

        [Header("Hit effect")]
        public Color burstColor = Palette.Blue;
        public int burstCount = 14;
        public float burstSpeed = 7f;

        void Awake()
        {
            if (player == null) player = GetComponent<NixAndEko.Player.PlayerController>();
        }

        public bool OnArrowHit(Arrow arrow)
        {
            // Only echo arrows do anything; either way nothing ever sticks into Nix.
            if (arrow == null || !arrow.isEkoArrow) return false;

            if (bow != null)
            {
                bow.RefreshAirShot();
                // Fling Nix along Eko's aim — aim up-right, get launched up-right. This is the
                // momentum boost, distinct from Nix's own recoil which kicks opposite the shot.
                bow.EkoLaunch(arrow.ekoAim, arrow.charge);
            }
            if (player != null) player.RefillGlide();
            Particle.Burst(transform.position, burstColor, burstCount, burstSpeed);
            Sfx.Play(Sfx.Id.EkoCatch);
            return false;
        }
    }
}
