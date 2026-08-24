using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Player
{
    /// <summary>
    /// Trails little wind streaks behind the archer while she's gliding, so the glide reads as
    /// speed rather than just a slow fall. Streaks are born behind her, aligned to the direction
    /// of travel, and drift further backward as they fade — they sort under the player sprite so
    /// they stream out from behind her.
    /// </summary>
    public class WindStreaks : MonoBehaviour
    {
        public PlayerController player;

        [Header("Emission")]
        [Tooltip("Streaks spawned per second while gliding.")]
        public float perSecond = 22f;
        [Tooltip("Below this travel speed the glide isn't fast enough to show wind.")]
        public float minSpeed = 2f;
        [Tooltip("How far behind the archer streaks are born.")]
        public float trailDistance = 0.45f;
        [Tooltip("Random spread across the direction of travel.")]
        public float spread = 0.4f;

        [Header("Look")]
        [Tooltip("How fast streaks stream backward, on top of being left behind.")]
        public float streakSpeed = 5f;
        public float lifetime = 0.35f;
        [Tooltip("Streak length / thickness, as a multiple of the 4px particle quad.")]
        public float length = 1.6f;
        public float thickness = 0.18f;
        public Color color = new Color(1f, 1f, 1f, 0.55f);

        float _accum;

        void Update()
        {
            if (player == null || !player.IsGliding) { _accum = 0f; return; }

            Vector2 travel = player.Velocity;
            if (travel.magnitude < minSpeed) { _accum = 0f; return; }

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
            Vector2 side = new Vector2(-fwd.y, fwd.x);   // perpendicular, for the spawn spread

            Vector3 pos = player.transform.position
                        + (Vector3)(back * trailDistance)
                        + (Vector3)(side * Random.Range(-spread, spread));

            float angle = Mathf.Atan2(fwd.y, fwd.x) * Mathf.Rad2Deg;   // lie along the travel axis

            // sortingOrder 8 keeps streaks behind the player sprite (10).
            var p = Particle.Spawn(pos, Quaternion.Euler(0f, 0f, angle),
                                   new Vector3(length * Random.Range(0.6f, 1.3f), thickness, 1f), 8);
            p.velocity = back * (streakSpeed * Random.Range(0.7f, 1.3f));
            p.drag = 1.5f;
            p.lifetime = lifetime * Random.Range(0.7f, 1.2f);
            p.startColor = color;
            p.endColor = new Color(color.r, color.g, color.b, 0f);
            p.startScale = 1f;
            p.endScale = 0.5f;
            p.Play();
        }
    }
}
