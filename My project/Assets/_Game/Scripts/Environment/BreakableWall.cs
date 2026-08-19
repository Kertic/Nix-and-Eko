using NixAndEko.Combat;
using UnityEngine;
using UnityEngine.Events;

namespace NixAndEko.Environment
{
    /// <summary>
    /// A wall that shatters when shot, optionally only by a sufficiently charged arrow.
    /// A core metroidvania gate: hides paths until the player has the aim/power to open them.
    /// </summary>
    public class BreakableWall : MonoBehaviour, IArrowHittable
    {
        [Tooltip("Hits required to break (each qualifying arrow counts as one).")]
        public int hitsToBreak = 1;
        [Range(0f, 1f)]
        [Tooltip("Minimum arrow charge that counts as a hit (1 = needs full draw).")]
        public float minCharge = 0f;

        public UnityEvent onBroken;

        int _hits;

        public bool OnArrowHit(Arrow arrow)
        {
            if (arrow.charge < minCharge)
                return true; // not strong enough — arrow just sticks

            _hits++;
            if (_hits >= hitsToBreak)
            {
                onBroken?.Invoke();
                Destroy(gameObject);
                return false; // consume the arrow
            }
            return false;
        }
    }
}
