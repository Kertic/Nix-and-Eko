using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// A single short-lived sprite puff: drifts, fades and shrinks, then destroys itself.
    /// Spawned entirely in code (see <see cref="Spawn"/> / <see cref="Burst"/>) so effects stay
    /// generated like the rest of the placeholder art — no ParticleSystem assets or materials to
    /// keep in sync.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class Particle : MonoBehaviour
    {
        public Vector2 velocity;
        [Tooltip("Fraction of the remaining velocity shed per second.")]
        public float drag = 2f;
        [Tooltip("Downward acceleration, units/sec^2. Usually 0 — these are puffs, not debris.")]
        public float gravity;
        public float lifetime = 0.4f;
        public Color startColor = Color.white;
        public Color endColor = new Color(1f, 1f, 1f, 0f);
        [Tooltip("Scale multiplier at birth / at death, applied on top of the spawn scale.")]
        public float startScale = 1f;
        public float endScale = 0.3f;

        SpriteRenderer _sr;
        Vector3 _baseScale;
        float _age;

        // One shared white quad for every particle; tint and non-uniform scale do the rest.
        static Sprite _quad;
        static Sprite Quad => _quad != null ? _quad : (_quad = SpriteFactory.SolidRect(Color.white, 4, 4, Color.white));

        /// <summary>Start animating. Call once the fields and transform are configured.</summary>
        public void Play()
        {
            _sr = GetComponent<SpriteRenderer>();
            _baseScale = transform.localScale;
            _sr.color = startColor;
            _age = 0f;
        }

        void Update()
        {
            if (_sr == null) return;

            float dt = Time.deltaTime;
            _age += dt;
            float t = Mathf.Clamp01(_age / Mathf.Max(0.01f, lifetime));

            if (gravity != 0f) velocity += Vector2.down * (gravity * dt);
            velocity -= velocity * Mathf.Clamp01(drag * dt);
            transform.position += (Vector3)(velocity * dt);

            _sr.color = Color.Lerp(startColor, endColor, t);
            transform.localScale = _baseScale * Mathf.Lerp(startScale, endScale, t);

            if (t >= 1f) Destroy(gameObject);
        }

        /// <summary>
        /// Create one particle, configured but not yet running — set the fields you care about
        /// on the result, then call <see cref="Play"/>.
        /// </summary>
        public static Particle Spawn(Vector3 position, Quaternion rotation, Vector3 scale, int sortingOrder = 12)
        {
            var go = new GameObject("Particle");
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Quad;
            sr.sortingOrder = sortingOrder;

            return go.AddComponent<Particle>();
        }

        /// <summary>A radial pop of <paramref name="count"/> particles — an impact flourish.</summary>
        public static void Burst(Vector3 center, Color color, int count = 12, float speed = 6f,
                                 float lifetime = 0.45f, float size = 0.9f, int sortingOrder = 22)
        {
            float offset = Random.value * 360f;
            for (int i = 0; i < count; i++)
            {
                float angle = offset + 360f / count * i + Random.Range(-12f, 12f);
                var dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));

                var p = Spawn(center + (Vector3)(dir * 0.1f), Quaternion.identity,
                              Vector3.one * (size * Random.Range(0.6f, 1.3f)), sortingOrder);
                p.velocity = dir * (speed * Random.Range(0.5f, 1.2f));
                p.drag = 4f;
                p.lifetime = lifetime * Random.Range(0.7f, 1.2f);
                p.startColor = color;
                p.endColor = new Color(color.r, color.g, color.b, 0f);
                p.startScale = 1f;
                p.endScale = 0.15f;
                p.Play();
            }
        }
    }
}
