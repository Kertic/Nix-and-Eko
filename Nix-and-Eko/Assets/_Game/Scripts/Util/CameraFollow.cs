using NixAndEko.Player;
using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// Smoothly tracks the archer. The level is much larger than one screen, so the camera
    /// follows rather than sitting still. Finds the player automatically if no target is set.
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        public Transform target;
        [Tooltip("Seconds for the camera to catch up (0 = instant).")]
        public float smoothTime = 0.18f;
        public Vector2 offset = new Vector2(0f, 1.5f);
        [Tooltip("Look ahead in the direction of travel, in units per unit/sec of speed.")]
        public float lookAhead = 0.12f;

        Vector3 _velocity;

        void Start()
        {
            if (target == null)
            {
                var player = FindAnyObjectByType<PlayerController>();
                if (player != null) target = player.transform;
            }
            if (target != null) transform.position = Desired(Vector2.zero);
        }

        /// <summary>Point at <paramref name="newTarget"/> and jump the camera onto it immediately —
        /// no smoothing. Used by <see cref="NixAndEko.Combat.EkoSummoner"/> to shift focus to the
        /// phantom when the morph starts (so the bullet-time aim opens up already framed on Eko),
        /// then back to Nix on release.</summary>
        public void SnapToTarget(Transform newTarget)
        {
            target = newTarget;
            if (target == null) return;
            transform.position = Desired(Vector2.zero);
            _velocity = Vector3.zero;   // wipe SmoothDamp's residual so the next LateUpdate settles cleanly
        }

        void LateUpdate()
        {
            if (target == null) return;

            Vector2 vel = Vector2.zero;
            var rb = target.GetComponent<Rigidbody2D>();
            if (rb != null) vel = rb.linearVelocity;

            transform.position = Vector3.SmoothDamp(
                transform.position, Desired(vel), ref _velocity, smoothTime);
        }

        Vector3 Desired(Vector2 targetVelocity)
        {
            Vector2 ahead = new Vector2(targetVelocity.x * lookAhead, 0f);
            Vector2 p = (Vector2)target.position + offset + ahead;
            return new Vector3(p.x, p.y, transform.position.z);
        }
    }
}
