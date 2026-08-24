using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// Sits on Nix and catches Eko's arrows. A hit pops a small blue burst and reloads Nix's air
    /// shot, so a well-placed echo lets her fire — and so recoil again — without ever touching
    /// the ground. Nix's own arrows never reach this: the Bow tells them to ignore her collider.
    /// </summary>
    public class EkoArrowTarget : MonoBehaviour, IArrowHittable
    {
        public Bow bow;

        [Header("Hit effect")]
        public Color burstColor = Palette.Blue;
        public int burstCount = 14;
        public float burstSpeed = 7f;

        public bool OnArrowHit(Arrow arrow)
        {
            // Only echo arrows do anything; either way nothing ever sticks into Nix.
            if (arrow == null || !arrow.isEkoArrow) return false;

            if (bow != null) bow.RefreshAirShot();
            Particle.Burst(transform.position, burstColor, burstCount, burstSpeed);
            return false;
        }
    }
}
