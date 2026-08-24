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
        [Tooltip("Eko's aim direction — the way Nix gets flung when this arrow catches her (set by Eko).")]
        public Vector2 ekoAim = Vector2.right;
        [Tooltip("Degrees per second the homing arrow can turn toward its mark (aim assist).")]
        public float homingTurnRate = 720f;

        Rigidbody2D _rb;
        Collider2D _col;
        bool _stuck;
        /// <summary>Nix's collider for an Eko arrow — see <see cref="SetCatchTarget"/>.</summary>
        Collider2D _catchCol;
        IArrowHittable _catchHittable;
        /// <summary>The arrow must clear Nix's bounds once before a catch counts (anti free-catch on spawn).</summary>
        bool _catchArmed;
        /// <summary>When set, the arrow curves toward this and phases through everything else — see <see cref="HomeTo"/>.</summary>
        Transform _homingTarget;
        float _homeSpeed;

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
        /// Register Nix as this Eko arrow's catch target. The arrow never physically collides with
        /// her, so it never shoves her — the momentum boost is applied deliberately by
        /// <see cref="Bow.EkoLaunch"/> along Eko's aim, not by an incidental collision impulse (that
        /// would double up on straight shots and be absent on trigger-based homing shots). The
        /// catch is detected by overlap once the arrow has first cleared her bounds, so an arrow
        /// spawned on top of her (Eko stands where Nix was) can't hand back a free reload on frame one.
        /// </summary>
        public void SetCatchTarget(Collider2D nixCol)
        {
            if (nixCol == null || _col == null) return;
            _catchCol = nixCol;
            _catchHittable = nixCol.GetComponentInParent<IArrowHittable>();
            _catchArmed = false;
            Physics2D.IgnoreCollision(_col, nixCol, true);   // never impulse Nix
        }

        /// <summary>
        /// Aim assist: curve toward <paramref name="target"/> and phase through everything else,
        /// so an Eko shot released while Nix is on the preview line always finds its mark. Turns
        /// the arrow into a trigger (no physical blocking) and steers it manually.
        /// </summary>
        public void HomeTo(Transform target)
        {
            _homingTarget = target;
            if (_col != null) _col.isTrigger = true;   // pass through walls; still reports overlaps
        }

        /// <summary>Launch the arrow. Called by the Bow (and by Eko).</summary>
        public void Launch(Vector2 velocity, float chargeAmount)
        {
            charge = chargeAmount;
            _rb.gravityScale = _homingTarget != null || flyStraight ? 0f : gravityScale;
            _homeSpeed = velocity.magnitude;

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
        }

        void FixedUpdate()
        {
            if (_stuck) return;

            TryCatchNix();
            if (_homingTarget != null) HomingSteer();
        }

        /// <summary>
        /// Detect an Eko arrow catching Nix by overlap (physical collision with her is ignored, so
        /// nothing shoves her). Requires the arrow to have cleared her bounds once first, so a shot
        /// spawned overlapping her doesn't catch on frame one.
        /// </summary>
        void TryCatchNix()
        {
            if (_catchCol == null) return;

            bool overlap = _col.bounds.Intersects(_catchCol.bounds);
            if (!_catchArmed)
            {
                if (!overlap) _catchArmed = true;   // cleared her once — a re-entry now counts
                return;
            }

            if (overlap)
            {
                // OnArrowHit reloads Nix's air shot + glide and applies the EkoLaunch boost.
                _catchHittable?.OnArrowHit(this);
                Destroy(gameObject);
            }
        }

        void HomingSteer()
        {
            // Steer the velocity toward the mark at a fixed turn rate — fast enough to guarantee
            // the hit, slow enough to read as a curving arrow rather than a snap.
            Vector2 to = (Vector2)_homingTarget.position - _rb.position;
            if (to.sqrMagnitude < 0.0001f) return;

            float speed = Mathf.Max(_homeSpeed, 0.01f);
            Vector2 current = _rb.linearVelocity.sqrMagnitude > 0.0001f
                ? _rb.linearVelocity.normalized : to.normalized;
            float maxRad = homingTurnRate * Mathf.Deg2Rad * Time.fixedDeltaTime;
            Vector2 steered = Vector3.RotateTowards(current, to.normalized, maxRad, 0f);
            _rb.linearVelocity = steered.normalized * speed;
        }

        void Orient()
        {
            Vector2 v = _rb.linearVelocity;
            if (v.sqrMagnitude > 0.01f)
                transform.right = v.normalized;
        }

        void OnCollisionEnter2D(Collision2D collision)
        {
            Vector2 point = collision.GetContact(0).point;
            if (!MovingInto(point)) return;
            Impact(collision.collider, point);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            // Triggers don't give a contact point; the nearest point on the other collider is
            // the closest stand-in for "where would we touch it".
            Vector2 point = other.ClosestPoint(transform.position);
            if (!MovingInto(point)) return;
            Impact(other, point);
        }

        /// <summary>
        /// True only when the arrow is actually flying toward <paramref name="point"/> — i.e. the
        /// hit is ahead of it along its travel vector, not behind or beside it. Stops an arrow
        /// that spawns already overlapping something (the muzzle, a wall corner, the ground under
        /// a grounded shot) from instantly sticking to a surface it's really moving away from:
        /// moving right, it only catches things to its right; moving up, only things above; etc.
        /// </summary>
        bool MovingInto(Vector2 point)
        {
            if (_homingTarget != null) return true;      // homing shots always connect with their mark
            Vector2 v = _rb.linearVelocity;
            if (v.sqrMagnitude < 0.0001f) return true;   // not moving: no direction to gate on
            return Vector2.Dot(v, point - (Vector2)transform.position) > 0f;
        }

        void Impact(Collider2D other, Vector2 point)
        {
            if (_stuck) return;

            // Aim-assist arrows phase through everything that isn't their mark — walls, floors,
            // switches all ignored, so the shot can't be stopped short of Nix.
            if (_homingTarget != null &&
                other.transform != _homingTarget && !other.transform.IsChildOf(_homingTarget))
                return;

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
