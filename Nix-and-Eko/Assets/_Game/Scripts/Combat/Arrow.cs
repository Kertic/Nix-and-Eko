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
        public float stuckLifetime = 6f;
        [Tooltip("Seconds before an in-flight arrow that never hits anything despawns.")]
        public float flightLifetime = 4f;
        [Tooltip("Gravity scale while flying.")]
        public float gravityScale = 2.2f;

        [Header("Charge (0..1), set by the Bow at launch")]
        [Range(0f, 1f)] public float charge;

        Rigidbody2D _rb;
        Collider2D _col;
        bool _stuck;

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _col = GetComponent<Collider2D>();
            _rb.gravityScale = gravityScale;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        /// <summary>Launch the arrow. Called by the Bow.</summary>
        public void Launch(Vector2 velocity, float chargeAmount)
        {
            charge = chargeAmount;
            _rb.linearVelocity = velocity;
            Orient();
            if (flightLifetime > 0f) Destroy(gameObject, flightLifetime + stuckLifetime);
        }

        void Update()
        {
            if (!_stuck) Orient();
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
