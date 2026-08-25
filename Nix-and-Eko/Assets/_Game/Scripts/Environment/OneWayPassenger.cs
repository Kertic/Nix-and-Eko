using System.Collections.Generic;
using NixAndEko.Player;
using UnityEngine;

namespace NixAndEko.Environment
{
    /// <summary>
    /// Arbitrates the player's collision against every <see cref="OneWayPlatform"/> in the level,
    /// once per physics step, *before* the step runs — so a one-way platform is only ever switched
    /// solid from a position that cannot already be overlapping it (see
    /// <see cref="OneWayPlatform"/> for why the decision has to happen here rather than in the
    /// effector). The result is that side contacts and partial penetration are unrepresentable
    /// rather than merely unlikely.
    ///
    /// On top of the hard rule there's a landing assist: descending with your feet a hair below a
    /// platform's surface — a near-miss at the apex, or a sliver of penetration left over from a
    /// depenetration nudge — places you on top of it instead of letting you sink through.
    /// </summary>
    [DefaultExecutionOrder(50)]   // after PlayerController.FixedUpdate has set this step's velocity
    public class OneWayPassenger : MonoBehaviour
    {
        public PlayerController player;

        [Header("Landing assist")]
        [Tooltip("Descending with feet within this far below a platform's surface snaps you on " +
                 "top of it. Keep it well under the player's height — this is a nudge for " +
                 "near-misses, not a magnet.")]
        public float snapDistance = 0.15f;

        Collider2D _col;

        /// <summary>
        /// Platforms we're currently phased through. Tracked so the state is only pushed into the
        /// physics engine when it actually changes — re-asserting an unchanged ignore every step
        /// churns the contact pair, which can jitter a resting contact.
        /// </summary>
        readonly HashSet<OneWayPlatform> _ignored = new HashSet<OneWayPlatform>();

        void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            _col = player != null ? player.Col : GetComponent<Collider2D>();
        }

        void OnDisable()
        {
            // Never leave platforms permanently phased out for us.
            if (_col != null)
                foreach (var p in _ignored)
                    if (p != null && p.Collider != null)
                        Physics2D.IgnoreCollision(_col, p.Collider, false);
            _ignored.Clear();
        }

        void FixedUpdate()
        {
            if (_col == null && player != null) _col = player.Col;
            if (_col == null) return;

            foreach (var platform in OneWayPlatform.All)
            {
                if (platform == null || platform.Collider == null) continue;

                bool solid = platform.IsSolidFor(_col);

                // Landing assist runs before the collision state is committed, so a snap this
                // frame also turns the platform solid this frame.
                if (!solid && TrySnapOnTop(platform)) solid = true;

                SetSolid(platform, solid);
            }
        }

        void SetSolid(OneWayPlatform platform, bool solid)
        {
            bool ignoredNow = _ignored.Contains(platform);
            if (solid == !ignoredNow) return;   // already in the desired state

            Physics2D.IgnoreCollision(_col, platform.Collider, !solid);
            if (solid) _ignored.Remove(platform);
            else _ignored.Add(platform);
        }

        /// <summary>
        /// Descending, horizontally over the platform, and feet just barely below its surface:
        /// lift the player up onto it and kill the descent. Deliberately skipped while rising (a
        /// jump on its way up through the platform must not be yanked onto it) and while dropping
        /// through on purpose.
        /// </summary>
        bool TrySnapOnTop(OneWayPlatform platform)
        {
            if (snapDistance <= 0f) return false;
            if (player == null || player.Velocity.y > 0f) return false;
            if (platform.IsDroppingFor(_col)) return false;

            Bounds me = _col.bounds;
            Bounds it = platform.Collider.bounds;

            // Must actually be over the platform, not beside it.
            if (me.max.x <= it.min.x || me.min.x >= it.max.x) return false;

            float top = platform.Top;
            float feet = me.min.y;
            if (feet >= top || feet <= top - snapDistance) return false;

            // Move the body, not the transform — a transform write on an interpolated Rigidbody2D
            // gets stomped by interpolation and reads as a jitter.
            player.Rb.position += new Vector2(0f, top - feet);
            player.Velocity = new Vector2(player.Velocity.x, 0f);
            return true;
        }
    }
}
