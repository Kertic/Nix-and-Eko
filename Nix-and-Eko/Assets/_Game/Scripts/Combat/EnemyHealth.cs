using NixAndEko.Environment;
using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// Hit points, damage feedback and the chill status for the primitive enemies. Arrows and melee
    /// both route through <see cref="Damage"/>; the chill status is applied by <see cref="OnArrowHit"/>
    /// when a <em>blue</em> arrow strikes (Eko's own shots, and the blue arrows Nix earned from an Eko
    /// catch — see <see cref="Arrow.blue"/>). Chill does no damage itself: it tints the enemy blue,
    /// slows their movement (see <see cref="SpeedMultiplier"/>), and makes the next arrow strike
    /// deal extra damage. The status is a timer, not consumed on hit — it fades when the timer runs
    /// out. Touching the enemy hurts the player (contact damage).
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class EnemyHealth : MonoBehaviour, IArrowHittable
    {
        [Header("Health")]
        public int maxHealth = 3;
        [Tooltip("Damage a regular (non-blue) arrow deals on hit.")]
        public int arrowDamage = 1;

        [Header("Chill (applied by blue / Eko arrows)")]
        [Tooltip("Seconds the chill status lasts before it fades on its own.")]
        public float chillDuration = 4f;
        [Tooltip("Movement/timing speed while chilled — 1 = normal, 0 = fully stopped. EnemyWalker " +
                 "and EnemySlammer read this each step.")]
        [Range(0f, 1f)] public float chillSpeedMultiplier = 0.4f;
        [Tooltip("Damage multiplier on the arrow that strikes a chilled enemy — the chill breaks " +
                 "on hit only once the multiplier has been applied. 2 = double damage.")]
        public float chillDamageMultiplier = 2f;
        [Tooltip("Colour the sprite is tinted while chilled.")]
        public Color chillTint = new Color(0.55f, 0.8f, 1f, 1f);

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
        /// <summary>True while the chill status is active.</summary>
        public bool Chilled => _chillTimer > 0f;
        /// <summary>1 normally, <see cref="chillSpeedMultiplier"/> while chilled — read by
        /// movement scripts (EnemyWalker, EnemySlammer) each step so no per-enemy plumbing
        /// beyond a shared reference is needed.</summary>
        public float SpeedMultiplier => Chilled ? chillSpeedMultiplier : 1f;

        float _chillTimer;
        Color _spriteBaseColor;
        bool _spriteBaseColorCached;

        void Awake()
        {
            Current = maxHealth;
            if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
            if (flash == null && sprite != null) flash = sprite.GetComponent<HitFlash>();
        }

        void Update()
        {
            if (_chillTimer <= 0f) return;
            _chillTimer -= Time.deltaTime;
            if (_chillTimer <= 0f) EndChill();
        }

        /// <summary>Deal <paramref name="amount"/> damage from <paramref name="sourcePos"/>. If
        /// the enemy is chilled, the incoming damage is scaled by <see cref="chillDamageMultiplier"/>
        /// before it's applied.</summary>
        public void Damage(int amount, Vector2 sourcePos)
        {
            if (Dead || amount <= 0) return;

            if (Chilled) amount = Mathf.Max(1, Mathf.RoundToInt(amount * chillDamageMultiplier));

            Current = Mathf.Max(0, Current - amount);
            if (bar != null) bar.SetFraction(Current / (float)Mathf.Max(1, maxHealth));
            if (flash != null) flash.Flash();
            Sfx.Play(Sfx.Id.EnemyHit);

            if (Current <= 0) Die();
        }

        /// <summary>Apply (or refresh) the chill status. Tints the sprite and starts the timer
        /// — repeated blue-arrow hits refresh it rather than stacking.</summary>
        public void ApplyChill()
        {
            if (Dead) return;

            if (!Chilled && sprite != null)
            {
                if (!_spriteBaseColorCached) { _spriteBaseColor = sprite.color; _spriteBaseColorCached = true; }
                sprite.color = chillTint;
                Particle.Burst(transform.position, chillTint, 8, 4f, 0.35f, 0.5f);
            }
            _chillTimer = Mathf.Max(_chillTimer, chillDuration);
        }

        void EndChill()
        {
            _chillTimer = 0f;
            if (sprite != null && _spriteBaseColorCached) sprite.color = _spriteBaseColor;
        }

        void Die()
        {
            Particle.Burst(transform.position, deathBurst, 18, 8f);
            Particle.Burst(transform.position, Color.white, 10, 5f);
            Destroy(gameObject);
        }

        public bool OnArrowHit(Arrow arrow)
        {
            // Blue arrows (Eko's) apply chill instead of damaging. Every other arrow damages,
            // with the chill multiplier stacked on top by Damage() when the enemy is chilled.
            if (arrow != null && arrow.blue)
            {
                ApplyChill();
                return false;   // never stick into an enemy
            }

            Damage(arrowDamage, arrow != null ? (Vector2)arrow.transform.position : (Vector2)transform.position);
            return false;
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
