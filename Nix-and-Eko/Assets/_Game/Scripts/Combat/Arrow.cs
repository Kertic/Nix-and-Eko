using System.Collections.Generic;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// A fired arrow. Flies as a physics body, orients to its velocity, and on impact either
    /// triggers an <see cref="IArrowHittable"/> or sticks into the surface as a decoration
    /// (stuck arrows are inert — they cannot be stood on).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public class Arrow : MonoBehaviour
    {
        [Tooltip("Seconds before a stuck arrow disappears (0 = never).")]
        public float stuckLifetime = 1f;
        [Tooltip("Seconds before an in-flight arrow that never hits anything despawns.")]
        public float flightLifetime = 4f;
        [Tooltip("Gravity scale while flying.")]
        public float gravityScale = 2.2f;

        [Header("Charge (0..1), set by the Bow at launch")]
        [Range(0f, 1f)] public float charge;

        [Header("Eko")]
        [Tooltip("Ignore gravity and fly dead straight. Eko's arrows do this; Nix's arc.")]
        public bool flyStraight;
        [Tooltip("Fired by Eko — catching Nix with it reloads her air shot instead of doing nothing.")]
        public bool isEkoArrow;

        Rigidbody2D _rb;
        Collider2D _col;
        bool _stuck;
        /// <summary>Collider the arrow passes through until it has physically cleared it — see <see cref="ArmAgainst"/>.</summary>
        Collider2D _armAgainst;

        /// <summary>
        /// Every live arrow, so a freshly fired one can be told to pass through the others.
        /// Without this, two arrows sharing space — successive shots spawn on top of each other at
        /// the muzzle, or two arcs crossing mid-air — collide and both freeze in place.
        /// </summary>
        static readonly List<Arrow> Active = new List<Arrow>();

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _col = GetComponent<Collider2D>();
            _rb.gravityScale = gravityScale;
            _rb.freezeRotation = true;   // we orient the arrow to its velocity; keep physics from spinning it
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        void OnDestroy() => Active.Remove(this);

        /// <summary>
        /// Pass through <paramref name="col"/> until the arrow has physically cleared it, then
        /// start colliding with it normally. Eko's arrows spawn inside Nix (Eko stands where Nix
        /// was when she was summoned), so without this an echo shot would instantly hit Nix and
        /// hand back a free reload; this way the arrow has to actually travel back into her.
        /// </summary>
        public void ArmAgainst(Collider2D col)
        {
            if (col == null || _col == null) return;
            _armAgainst = col;
            Physics2D.IgnoreCollision(_col, col, true);
        }

        /// <summary>Launch the arrow. Called by the Bow (and by Eko).</summary>
        public void Launch(Vector2 velocity, float chargeAmount)
        {
            charge = chargeAmount;
            _rb.gravityScale = flyStraight ? 0f : gravityScale;

            // Never collide with another arrow — set before the next physics step so no impulse lands.
            foreach (Arrow other in Active)
                if (other != null && other._col != null)
                    Physics2D.IgnoreCollision(_col, other._col, true);
            Active.Add(this);

            _rb.linearVelocity = velocity;
            Orient();
            if (flightLifetime > 0f) Destroy(gameObject, flightLifetime + stuckLifetime);
        }

        void Update()
        {
            if (_stuck) return;

            Orient();

            // Once the arrow is clear of the collider it was launched inside of, let it hit.
            if (_armAgainst != null && !_col.bounds.Intersects(_armAgainst.bounds))
            {
                Physics2D.IgnoreCollision(_col, _armAgainst, false);
                _armAgainst = null;
            }
        }

        void Orient()
        {
            Vector2 v = _rb.linearVelocity;
            if (v.sqrMagnitude > 0.01f)
                transform.right = v.normalized;
        }

        void OnCollisionEnter2D(Collision2D collision) => Impact(collision.collider, collision.GetContact(0).point);
        void OnTriggerEnter2D(Collider2D other) => Impact(other, transform.position);

        void Impact(Collider2D other, Vector2 point)
        {
            if (_stuck) return;

            // Give the struck object a chance to react (and to reject sticking).
            var hittable = other.GetComponentInParent<IArrowHittable>();
            if (hittable != null)
            {
                bool shouldStick = hittable.OnArrowHit(this);
                if (!shouldStick) { Destroy(gameObject); return; }
            }

            Stick(other.transform);
        }

        void Stick(Transform surface)
        {
            _stuck = true;
            _rb.linearVelocity = Vector2.zero;
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.simulated = true;

            // Ride moving platforms.
            transform.SetParent(surface, worldPositionStays: true);

            // Stuck arrows are purely cosmetic — never standable, never blocking.
            _col.enabled = false;

            if (stuckLifetime > 0f) Destroy(gameObject, stuckLifetime);
        }
    }
}
