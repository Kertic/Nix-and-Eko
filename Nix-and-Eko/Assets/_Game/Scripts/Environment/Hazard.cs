using NixAndEko.Combat;
using UnityEngine;

namespace NixAndEko.Environment
{
    /// <summary>
    /// Damages the player on contact (spikes, lava, enemies). Works as a trigger or a solid
    /// collider. Optionally destroyed when hit by an arrow (a simple breakable enemy target).
    /// </summary>
    public class Hazard : MonoBehaviour, IArrowHittable
    {
        public int damage = 1;
        [Tooltip("Arrows destroy this object (e.g. a fragile enemy) instead of sticking.")]
        public bool destroyedByArrow = false;

        void OnTriggerEnter2D(Collider2D other) => TryHurt(other);
        void OnTriggerStay2D(Collider2D other) => TryHurt(other);
        void OnCollisionEnter2D(Collision2D c) => TryHurt(c.collider);
        void OnCollisionStay2D(Collision2D c) => TryHurt(c.collider);

        void TryHurt(Collider2D other)
        {
            var health = other.GetComponentInParent<Health>();
            if (health != null)
                health.Damage(damage, transform.position);
        }

        public bool OnArrowHit(Arrow arrow)
        {
            if (destroyedByArrow)
            {
                Destroy(gameObject);
                return false; // consume the arrow
            }
            return true; // arrow sticks
        }
    }
}
