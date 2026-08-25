using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Player
{
    /// <summary>
    /// Streaks flashed behind the archer for the brief window a bow burst — recoil or Eko's
    /// catch launch — is locking out steering input (see
    /// <see cref="PlayerController.InputLockTimer"/>). Reads as the kick itself: denser than
    /// <see cref="WindStreaks"/> and fired dead straight along the burst's travel line rather
    /// than spread across it, since a burst has one direction, not a wide wake.
    /// </summary>
    public class BurstStreaks : MonoBehaviour
    {
        public PlayerController player;

        [Header("Emission")]
        [Tooltip("Streaks spawned per second while the burst input-lock is active.")]
        public float perSecond = 90f;
        [Tooltip("How far behind the archer streaks are born.")]
        public float trailDistance = 0.35f;

        [Header("Look")]
        [Tooltip("How fast streaks stream backward, on top of being left behind.")]
        public float streakSpeed = 9f;
        public float lifetime = 0.22f;
        [Tooltip("Streak length / thickness, as a multiple of the 4px particle quad.")]
        public float length = 2.2f;
        public float thickness = 0.1f;
        public Color color = new Color(1f, 1f, 1f, 0.8f);

        float _accum;

        void Update()
        {
            // Only while a burst is actively holding steering input off — matches the exact
            // window PlayerController.LockInput opens for recoil / EkoLaunch.
            if (player == null || player.InputLockTimer <= 0f) { _accum = 0f; return; }

            Vector2 travel = player.Velocity;
            if (travel.sqrMagnitude < 0.0001f) { _accum = 0f; return; }

            _accum += perSecond * Time.deltaTime;
            while (_accum >= 1f)
            {
                _accum -= 1f;
                Emit(travel);
            }
        }

        void Emit(Vector2 travel)
        {
            Vector2 fwd = travel.normalized;
            Vector2 back = -fwd;

            // No side spread (unlike WindStreaks) — the burst is one straight line, not a wake.
            Vector3 pos = player.transform.position + (Vector3)(back * trailDistance);

            float angle = Mathf.Atan2(fwd.y, fwd.x) * Mathf.Rad2Deg;   // lie along the travel axis

            // sortingOrder 8 keeps streaks behind the player sprite (10), matching WindStreaks.
            var p = Particle.Spawn(pos, Quaternion.Euler(0f, 0f, angle),
                                   new Vector3(length * Random.Range(0.85f, 1.2f), thickness, 1f), 8);
            p.velocity = back * (streakSpeed * Random.Range(0.85f, 1.15f));
            p.drag = 1.5f;
            p.lifetime = lifetime * Random.Range(0.8f, 1.1f);
            p.startColor = color;
            p.endColor = new Color(color.r, color.g, color.b, 0f);
            p.startScale = 1f;
            p.endScale = 0.4f;
            p.Play();
        }
    }
}
