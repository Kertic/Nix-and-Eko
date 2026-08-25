using System;
using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// A little blue orb that zips from one point to another and pops. Eko collapses into one of
    /// these whenever they travel without walking — returning to Nix, flying out to fetch a downed
    /// arrow, and on each end of a Nix/Eko swap. Runs on unscaled time so it still animates during a
    /// hitstop freeze. Built entirely in code, like the rest of the placeholder art.
    /// </summary>
    public class EkoOrb : MonoBehaviour
    {
        static readonly Color Blue = new Color(0.4f, 0.72f, 1f, 0.95f);

        SpriteRenderer _sr;
        Vector3 _from, _to;
        float _dur, _age;
        Action _onArrive;
        bool _burstOnArrive;

        /// <summary>Spawn an orb travelling <paramref name="from"/> → <paramref name="to"/> over <paramref name="duration"/> seconds.</summary>
        public static EkoOrb Fly(Vector3 from, Vector3 to, float duration = 0.18f,
                                 Action onArrive = null, bool burstOnArrive = true)
        {
            var go = new GameObject("EkoOrb");
            go.transform.position = from;
            go.transform.localScale = Vector3.one * 0.5f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 25;
            sr.sprite = SpriteFactory.SolidRect(Blue, 8, 8, Blue);
            sr.color = Blue;

            var orb = go.AddComponent<EkoOrb>();
            orb._sr = sr;
            orb._from = from;
            orb._to = to;
            orb._dur = Mathf.Max(0.01f, duration);
            orb._onArrive = onArrive;
            orb._burstOnArrive = burstOnArrive;
            Particle.Burst(from, Blue, 8, 5f, 0.3f, 0.5f);
            return orb;
        }

        void Update()
        {
            _age += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_age / _dur);
            // Ease-in-out so the hop reads as a deliberate zip, not a linear slide.
            float e = t * t * (3f - 2f * t);
            transform.position = Vector3.Lerp(_from, _to, e);
            transform.localScale = Vector3.one * Mathf.Lerp(0.35f, 0.6f, Mathf.Sin(t * Mathf.PI));

            if (t >= 1f)
            {
                if (_burstOnArrive) Particle.Burst(_to, Blue, 10, 6f, 0.35f, 0.6f);
                _onArrive?.Invoke();
                Destroy(gameObject);
            }
        }
    }
}
