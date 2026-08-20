using System.Collections.Generic;
using UnityEngine;

namespace NixAndEko.Environment
{
    /// <summary>
    /// Kinematic platform that patrols between waypoints and carries anything riding on top.
    /// Riders are moved by the platform's per-step delta, which plays nicely with the player's
    /// manually-driven velocity (no reliance on physics friction).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class MovingPlatform : MonoBehaviour
    {
        [Tooltip("World-space waypoints. If empty, uses start position +/- localOffset.")]
        public Transform[] waypoints;
        [Tooltip("Used when no waypoints are assigned: patrol between start and start+offset.")]
        public Vector2 localOffset = new Vector2(4f, 0f);
        public float speed = 3f;
        [Tooltip("Seconds paused at each endpoint.")]
        public float waitTime = 0.4f;
        public bool pingPong = true;

        Rigidbody2D _rb;
        Vector2[] _points;
        int _index;
        int _dir = 1;
        float _waitTimer;
        Vector2 _prevPos;
        readonly HashSet<Rigidbody2D> _riders = new HashSet<Rigidbody2D>();

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            if (waypoints != null && waypoints.Length >= 2)
            {
                _points = new Vector2[waypoints.Length];
                for (int i = 0; i < waypoints.Length; i++) _points[i] = waypoints[i].position;
            }
            else
            {
                Vector2 start = transform.position;
                _points = new[] { start, start + localOffset };
            }
            _prevPos = _rb.position;
        }

        void FixedUpdate()
        {
            if (_points == null || _points.Length < 2) return;

            Vector2 target = _points[_index];
            Vector2 next = _rb.position;

            if (_waitTimer > 0f)
            {
                _waitTimer -= Time.fixedDeltaTime;
            }
            else
            {
                next = Vector2.MoveTowards(_rb.position, target, speed * Time.fixedDeltaTime);
                if (Vector2.Distance(next, target) < 0.001f)
                    Advance();
            }

            _rb.MovePosition(next);

            // Carry riders by our delta.
            Vector2 delta = next - _prevPos;
            if (delta.sqrMagnitude > 0f)
                foreach (var r in _riders)
                    if (r != null) r.position += delta;

            _prevPos = next;
        }

        void Advance()
        {
            if (pingPong)
            {
                if (_index + _dir >= _points.Length || _index + _dir < 0) _dir = -_dir;
                _index += _dir;
            }
            else
            {
                _index = (_index + 1) % _points.Length;
            }
            _waitTimer = waitTime;
        }

        void OnCollisionEnter2D(Collision2D c) => TryAddRider(c);
        void OnCollisionStay2D(Collision2D c) => TryAddRider(c);
        void OnCollisionExit2D(Collision2D c)
        {
            var rb = c.rigidbody;
            if (rb != null) _riders.Remove(rb);
        }

        void TryAddRider(Collision2D c)
        {
            if (c.rigidbody == null) return;
            // Only carry things resting on top: the rider's base is at/above our top surface.
            float ourTop = _rb != null ? _rb.position.y + GetComponent<Collider2D>().bounds.extents.y
                                        : transform.position.y;
            if (c.collider.bounds.min.y >= ourTop - 0.15f)
                _riders.Add(c.rigidbody);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            if (waypoints != null && waypoints.Length >= 2)
            {
                for (int i = 0; i < waypoints.Length - 1; i++)
                    if (waypoints[i] && waypoints[i + 1])
                        Gizmos.DrawLine(waypoints[i].position, waypoints[i + 1].position);
            }
            else
            {
                Vector3 s = Application.isPlaying ? (Vector3)_points[0] : transform.position;
                Gizmos.DrawLine(s, s + (Vector3)localOffset);
            }
        }
    }
}
