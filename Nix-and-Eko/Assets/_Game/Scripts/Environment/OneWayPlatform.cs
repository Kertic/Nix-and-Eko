using System.Collections.Generic;
using NixAndEko.Player;
using UnityEngine;

namespace NixAndEko.Environment
{
    /// <summary>
    /// A platform you can jump up through and land on, and drop down through with crouch+jump.
    ///
    /// A <see cref="PlatformEffector2D"/> handles the one-way rule for incidental passers-by
    /// (arrows), but it is NOT what governs the player. An effector decides per-contact, after the
    /// bodies are already touching, which lets a body that is partway inside the platform get
    /// resolved in whatever direction the contact normal happens to point — that's what produces
    /// side-snagging and "standing halfway through the plank". For the player the decision is made
    /// *before* the physics step instead, by <see cref="OneWayPassenger"/>, using the one rule that
    /// makes those states unrepresentable:
    ///
    ///   the platform is solid only while the passenger's feet are at or above its top surface.
    ///
    /// Feet above the top means the whole body is above the top (the feet are its lowest point), so
    /// collision is only ever switched on from a position that cannot already be overlapping. There
    /// is no side contact to resolve because approaching from the side means feet below the top,
    /// and no partial penetration because the platform simply isn't there until it's cleared.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(PlatformEffector2D))]
    public class OneWayPlatform : MonoBehaviour
    {
        [Tooltip("Seconds collision with the player is disabled during a drop-through.")]
        public float dropSeconds = 0.35f;

        /// <summary>Every live one-way platform, so the player can arbitrate against all of them without a physics query.</summary>
        public static readonly List<OneWayPlatform> All = new List<OneWayPlatform>();

        Collider2D _col;
        PlatformEffector2D _effector;

        /// <summary>Who is currently dropping through, and for how much longer.</summary>
        Collider2D _dropPassenger;
        float _dropTimer;

        public Collider2D Collider => _col != null ? _col : (_col = GetComponent<Collider2D>());

        /// <summary>World Y of the surface you stand on — the whole one-way rule pivots on this.</summary>
        public float Top => Collider.bounds.max.y;

        void Awake()
        {
            _col = GetComponent<Collider2D>();
            _effector = GetComponent<PlatformEffector2D>();
        }

        void OnEnable() => All.Add(this);

        void OnDisable()
        {
            All.Remove(this);
            _dropPassenger = null;
            _dropTimer = 0f;
        }

        void Update()
        {
            if (_dropTimer > 0f)
            {
                _dropTimer -= Time.deltaTime;
                if (_dropTimer <= 0f) _dropPassenger = null;
            }
        }

        void Reset()
        {
            var col = GetComponent<Collider2D>();
            col.usedByEffector = true;
            // No edge radius: it inflates the collider past the visible plank, so "resting on top"
            // would float above the sprite. The rounding was there to stop edges snagging, which
            // the feet-above-top rule now prevents outright.
            if (col is BoxCollider2D box) box.edgeRadius = 0f;
            var eff = GetComponent<PlatformEffector2D>();
            eff.useOneWay = true;
            eff.surfaceArc = 100f;       // arrows: only near-vertical contacts count as the surface
            eff.useSideFriction = false;
            eff.useSideBounce = false;
        }

        /// <summary>
        /// Should this platform be solid for <paramref name="passenger"/> right now? True only once
        /// the passenger has fully cleared the top surface, and never while they're dropping
        /// through. <paramref name="tolerance"/> forgives float error while resting on the surface.
        /// </summary>
        public bool IsSolidFor(Collider2D passenger, float tolerance = 0.02f)
        {
            if (passenger == null) return true;
            if (IsDroppingFor(passenger)) return false;
            return passenger.bounds.min.y >= Top - tolerance;
        }

        /// <summary>True while <paramref name="passenger"/> is mid drop-through of this platform.</summary>
        public bool IsDroppingFor(Collider2D passenger) =>
            _dropTimer > 0f && _dropPassenger == passenger;

        /// <summary>
        /// Is <paramref name="hit"/> solid for <paramref name="passenger"/>? Ordinary geometry
        /// always is; a one-way platform defers to <see cref="IsSolidFor"/>. Lets position probes
        /// (the player's ground/wall casts) agree with the collision state, instead of reporting
        /// ground the player is currently passing through.
        /// </summary>
        public static bool IsSolid(Collider2D hit, Collider2D passenger)
        {
            if (hit == null) return false;
            var owp = hit.GetComponent<OneWayPlatform>();
            return owp == null || owp.IsSolidFor(passenger);
        }

        /// <summary>True if <paramref name="hit"/> is a one-way platform at all.</summary>
        public static bool Is(Collider2D hit) =>
            hit != null && hit.GetComponent<OneWayPlatform>() != null;

        /// <summary>Temporarily let a specific collider pass down through this platform.</summary>
        public void DropFor(Collider2D passenger)
        {
            _dropPassenger = passenger;
            _dropTimer = dropSeconds;
        }

        /// <summary>
        /// Does a cast against this collider actually stop something? Mirrors
        /// <see cref="PlatformEffector2D"/>'s own rule — solid only on the face whose normal falls
        /// within <c>surfaceArc</c> of the effector's up direction (the top face, for a normal flat
        /// platform) — so trajectory previews can tell a real block from a pass-through the same
        /// way the physics engine will when the shot actually flies.
        /// </summary>
        public bool BlocksHit(RaycastHit2D hit)
        {
            if (_effector == null) _effector = GetComponent<PlatformEffector2D>();
            if (_effector == null || !_effector.useOneWay) return true;
            return Vector2.Angle(hit.normal, transform.up) <= _effector.surfaceArc * 0.5f;
        }

        /// <summary>
        /// Convenience for callers that only have the hit, not a specific platform reference: true
        /// if <paramref name="hit"/> isn't a one-way platform at all (ordinary solid ground always
        /// blocks) or, if it is, whether that hit lands on its blocking face.
        /// </summary>
        public static bool Blocks(RaycastHit2D hit)
        {
            var owp = hit.collider != null ? hit.collider.GetComponent<OneWayPlatform>() : null;
            return owp == null || owp.BlocksHit(hit);
        }

        /// <summary>Find a one-way platform directly under the player's feet and drop through it.</summary>
        public static bool TryDropThrough(PlayerController p)
        {
            Bounds b = p.Col.bounds;
            Vector2 feet = new Vector2(b.center.x, b.min.y);
            var hits = Physics2D.OverlapBoxAll(feet, new Vector2(b.size.x * 0.9f, 0.2f), 0f);
            foreach (var h in hits)
            {
                var owp = h.GetComponent<OneWayPlatform>();
                if (owp != null)
                {
                    owp.DropFor(p.Col);
                    return true;
                }
            }
            return false;
        }
    }
}
