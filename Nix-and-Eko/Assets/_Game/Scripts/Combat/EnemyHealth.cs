using NixAndEko.Environment;
using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// Hit points + damage feedback for the primitive enemies. Arrows and melee both route through
    /// <see cref="Damage"/>. The health bar stays hidden until the first hit; every hit pops a white
    /// flash (see <see cref="HitFlash"/>). Touching the enemy hurts the player (contact damage).
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class EnemyHealth : MonoBehaviour, IArrowHittable
    {
        [Header("Health")]
        public int maxHealth = 3;
        [Tooltip("Damage an arrow deals on hit.")]
        public int arrowDamage = 1;

        [Header("Contact")]
        [Tooltip("Damage dealt to the player on contact (0 = harmless to touch).")]
        public int contactDamage = 1;

        [Header("Feedback")]
        public SpriteRenderer sprite;
        public HitFlash flash;
        public EnemyHealthBar bar;
        public Color deathBurst = new Color(0.85f, 0.3f, 0.3f);

        public int Current { get; private set; }
        public bool Dead => Current <= 0;

        void Awake()
        {
            Current = maxHealth;
            if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
            if (flash == null && sprite != null) flash = sprite.GetComponent<HitFlash>();
        }

        /// <summary>Deal <paramref name="amount"/> damage from <paramref name="sourcePos"/>.</summary>
        public void Damage(int amount, Vector2 sourcePos)
        {
            if (Dead || amount <= 0) return;

            Current = Mathf.Max(0, Current - amount);
            if (bar != null) bar.SetFraction(Current / (float)Mathf.Max(1, maxHealth));
            if (flash != null) flash.Flash();

            if (Current <= 0) Die();
        }

        void Die()
        {
            Particle.Burst(transform.position, deathBurst, 18, 8f);
            Particle.Burst(transform.position, Color.white, 10, 5f);
            Destroy(gameObject);
        }

        public bool OnArrowHit(Arrow arrow)
        {
            Damage(arrowDamage, arrow != null ? (Vector2)arrow.transform.position : (Vector2)transform.position);
            return false;   // never stick into an enemy (Nix's own arrow still drops as a pickup)
        }

        void OnTriggerStay2D(Collider2D other) => TouchPlayer(other);
        void OnTriggerEnter2D(Collider2D other) => TouchPlayer(other);
        void OnCollisionStay2D(Collision2D c) => TouchPlayer(c.collider);

        void TouchPlayer(Collider2D other)
        {
            if (Dead || contactDamage <= 0) return;
            var health = other.GetComponentInParent<Health>();
            if (health != null) health.Damage(contactDamage, transform.position);
        }
    }
}
