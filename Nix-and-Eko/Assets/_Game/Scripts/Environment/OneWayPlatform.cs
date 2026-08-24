using System.Collections;
using NixAndEko.Player;
using UnityEngine;

namespace NixAndEko.Environment
{
    /// <summary>
    /// A platform you can jump up through and land on, and drop down through with crouch+jump.
    /// Uses a <see cref="PlatformEffector2D"/> for the one-way behaviour.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(PlatformEffector2D))]
    public class OneWayPlatform : MonoBehaviour
    {
        [Tooltip("Seconds collision with the player is disabled during a drop-through.")]
        public float dropSeconds = 0.35f;

        Collider2D _col;

        void Awake() => _col = GetComponent<Collider2D>();

        void Reset()
        {
            var col = GetComponent<Collider2D>();
            col.usedByEffector = true;
            if (col is BoxCollider2D box)
                box.edgeRadius = 0.05f;   // rounds corners so skimming an edge doesn't snag
            var eff = GetComponent<PlatformEffector2D>();
            eff.useOneWay = true;
            eff.surfaceArc = 100f;       // wide pass-through zone so ascending through it can't clip a "solid" contact
            eff.useSideFriction = false;
            eff.useSideBounce = false;
        }

        /// <summary>Temporarily let a specific collider pass down through this platform.</summary>
        public void DropFor(Collider2D passenger)
        {
            if (_col == null) _col = GetComponent<Collider2D>();
            StartCoroutine(DropRoutine(passenger));
        }

        IEnumerator DropRoutine(Collider2D passenger)
        {
            Physics2D.IgnoreCollision(passenger, _col, true);
            yield return new WaitForSeconds(dropSeconds);
            if (passenger != null && _col != null)
                Physics2D.IgnoreCollision(passenger, _col, false);
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
