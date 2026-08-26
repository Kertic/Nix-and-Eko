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

        // Chase mode: home onto a live target over a fixed duration, regardless of distance — a
        // far target doesn't take forever and a near one doesn't snap instantly. Each frame it's
        // eased toward wherever the target currently is, so at t=1 it's exactly on it (a moving
        // target — Nix on the return leg — still gets a smooth, guaranteed-arrival curve).
        bool _chase;
        Func<Vector3> _target;

        static GameObject NewOrb(Vector3 from)
        {
            var go = new GameObject("EkoOrb");
            go.transform.position = from;
            go.transform.localScale = Vector3.one * 0.5f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 25;
            sr.sprite = SpriteFactory.SolidRect(Blue, 8, 8, Blue);
            sr.color = Blue;
            Particle.Burst(from, Blue, 8, 5f, 0.3f, 0.5f);
            Sfx.Play(Sfx.Id.EkoZip, 1f, 0.7f);
            return go;
        }

        /// <summary>Spawn an orb travelling <paramref name="from"/> → <paramref name="to"/> over <paramref name="duration"/> seconds.</summary>
        public static EkoOrb Fly(Vector3 from, Vector3 to, float duration = 0.18f,
                                 Action onArrive = null, bool burstOnArrive = true)
        {
            var go = NewOrb(from);
            var orb = go.AddComponent<EkoOrb>();
            orb._sr = go.GetComponent<SpriteRenderer>();
            orb._from = from;
            orb._to = to;
            orb._dur = Mathf.Max(0.01f, duration);
            orb._onArrive = onArrive;
            orb._burstOnArrive = burstOnArrive;
            return orb;
        }

        /// <summary>
        /// Spawn an orb that homes onto a <em>live</em> target (re-read each frame) over a fixed
        /// <paramref name="duration"/> — a distance-independent travel time, so a far arrow doesn't
        /// take forever and a near one doesn't arrive instantly. Guaranteed to land exactly on the
        /// target when it fires <paramref name="onArrive"/>, even if the target moves meanwhile (the
        /// return leg homes on Nix).
        /// </summary>
        public static EkoOrb Chase(Vector3 from, Func<Vector3> target, float duration = 0.35f,
                                   Action onArrive = null)
        {
            var go = NewOrb(from);
            var orb = go.AddComponent<EkoOrb>();
            orb._sr = go.GetComponent<SpriteRenderer>();
            orb._chase = true;
            orb._from = from;
            orb._target = target;
            orb._dur = Mathf.Max(0.01f, duration);
            orb._onArrive = onArrive;
            orb._burstOnArrive = true;
            return orb;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _age += dt;

            if (_chase)
            {
                Vector3 goal = _target != null ? _target() : transform.position;
                float ct = Mathf.Clamp01(_age / _dur);
                float ce = ct * ct * (3f - 2f * ct);
                // Re-aimed at the live target every frame: remaining distance shrinks to zero by
                // t=1 regardless of how far it started, and a moving target still gets hit exactly.
                transform.position = Vector3.Lerp(_from, goal, ce);
                transform.localScale = Vector3.one * (0.45f + 0.1f * Mathf.Sin(Time.unscaledTime * 20f));

                if (ct >= 1f) Arrive(goal);
                return;
            }

            float t = Mathf.Clamp01(_age / _dur);
            // Ease-in-out so the hop reads as a deliberate zip, not a linear slide.
            float e = t * t * (3f - 2f * t);
            transform.position = Vector3.Lerp(_from, _to, e);
            transform.localScale = Vector3.one * Mathf.Lerp(0.35f, 0.6f, Mathf.Sin(t * Mathf.PI));

            if (t >= 1f) Arrive(_to);
        }

        void Arrive(Vector3 at)
        {
            if (_burstOnArrive) Particle.Burst(at, Blue, 10, 6f, 0.35f, 0.6f);
            _onArrive?.Invoke();
            Destroy(gameObject);
        }
    }
}
