using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// A primitive patroller: walks in a straight line and turns around at a wall or a ledge (no
    /// ground ahead). Moves by script against the ground layer rather than by physics, so it can
    /// wear a trigger collider that never shoves the player. Flips a child sprite to face travel.
    /// </summary>
    public class EnemyWalker : MonoBehaviour
    {
        public SpriteRenderer sprite;
        public LayerMask groundMask;

        [Header("Move")]
        public float speed = 2.5f;
        [Tooltip("Half-width used to probe for a wall / the ledge ahead (world units).")]
        public float halfWidth = 0.45f;
        [Tooltip("How far below the feet to look for ground before treating it as a ledge.")]
        public float groundProbe = 0.4f;
        public float feetOffset = 0.5f;

        int _dir = 1;
        float _walkTimer;
        int _frame;

        void Start()
        {
            if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
            ApplyFacing();
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            // Look for a wall dead ahead, or a ledge (no ground just ahead of the leading foot).
            Vector2 center = transform.position;
            Vector2 ahead = center + new Vector2(_dir * halfWidth, 0f);
            bool wall = Physics2D.Raycast(center, new Vector2(_dir, 0f), halfWidth + 0.1f, groundMask);
            bool groundAhead = Physics2D.Raycast(ahead, Vector2.down, feetOffset + groundProbe, groundMask);

            if (wall || !groundAhead) { _dir = -_dir; ApplyFacing(); }

            transform.position = center + new Vector2(_dir * speed * dt, 0f);

            // Simple two-frame shuffle.
            _walkTimer += dt * 6f;
            if (_walkTimer >= 1f)
            {
                _walkTimer -= 1f;
                _frame ^= 1;
                if (sprite != null) sprite.sprite = EnemySprites.WalkerFrames[_frame];
            }
        }

        void ApplyFacing()
        {
            if (sprite == null) return;
            Vector3 s = sprite.transform.localScale;
            s.x = Mathf.Abs(s.x) * (_dir >= 0 ? 1 : -1);
            sprite.transform.localScale = s;
        }
    }
}
