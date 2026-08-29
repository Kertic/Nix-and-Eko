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

        // Curved-path parameters. Each orb pushes perpendicular to its straight travel line by a
        // small randomised amount, so a stream of retrieval orbs traces distinct arcs rather than
        // marching in file. `_curveSign` flips left/right per-orb, `_curveAmount` is a fraction of
        // the current straight distance (so short hops stay tight, long hops arc wide),
        // `_wobbleFreq` / `_wobbleAmp` add a slow second-order sway on top so the arc doesn't read
        // as a perfect parabola. Offset always eases to zero at the endpoints (sin(pi*t)) so the
        // orb still lands exactly on the target.
        float _curveAmount;
        float _curveSign;
        float _wobbleFreq;
        float _wobbleAmp;
        float _wobblePhase;

        static GameObject NewOrb(Vector3 from)
        {
            var go = new GameObject("EkoOrb");
            Transform parent = Particle.FxContainer();
            if (parent != null) go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = from;
            go.transform.localScale = Vector3.one * 0.5f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 25;
            sr.sprite = SpriteFactory.SolidCircle(Blue, 8, Blue);
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
            orb.PickCurve();
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
            orb.PickCurve();
            return orb;
        }

        /// <summary>Pick this orb's curve — direction, magnitude, and a light wobble on top —
        /// once at spawn. Kept per-orb so a stream of retrieval orbs traces distinct arcs rather
        /// than a repeatable path; a straight line is a valid outcome (small `_curveAmount`).</summary>
        void PickCurve()
        {
            _curveSign  = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            _curveAmount = UnityEngine.Random.Range(0.18f, 0.45f);   // as a fraction of travel distance
            _wobbleFreq  = UnityEngine.Random.Range(2.0f, 4.0f);
            _wobbleAmp   = UnityEngine.Random.Range(0.05f, 0.18f);
            _wobblePhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        }

        void Update()
        {
            // Orbs run on unscaled time (so they still animate through a hitstop freeze), which
            // means they'd also fly through a full pause menu — arriving mid-pause and calling
            // onArrive to hand an arrow back or reform a phantom. Gate on the pause flag so a
            // retrieval that was in flight when the player paused actually holds its position
            // until they resume, matching what the rest of the world is doing.
            if (NixAndEko.Environment.PauseMenu.IsGameplayPaused) return;

            float dt = Time.unscaledDeltaTime;
            _age += dt;

            if (_chase)
            {
                Vector3 goal = _target != null ? _target() : transform.position;
                float ct = Mathf.Clamp01(_age / _dur);
                float ce = ct * ct * (3f - 2f * ct);
                // Re-aimed at the live target every frame: remaining distance shrinks to zero by
                // t=1 regardless of how far it started, and a moving target still gets hit exactly.
                Vector3 baseP = Vector3.Lerp(_from, goal, ce);
                transform.position = baseP + CurveOffset(_from, goal, ct);
                transform.localScale = Vector3.one * (0.45f + 0.1f * Mathf.Sin(Time.unscaledTime * 20f));

                if (ct >= 1f) Arrive(goal);
                return;
            }

            float t = Mathf.Clamp01(_age / _dur);
            // Ease-in-out so the hop reads as a deliberate zip, not a linear slide.
            float e = t * t * (3f - 2f * t);
            Vector3 basePos = Vector3.Lerp(_from, _to, e);
            transform.position = basePos + CurveOffset(_from, _to, t);
            transform.localScale = Vector3.one * Mathf.Lerp(0.35f, 0.6f, Mathf.Sin(t * Mathf.PI));

            if (t >= 1f) Arrive(_to);
        }

        /// <summary>
        /// Perpendicular offset that arcs the path smoothly out and back to zero at both ends
        /// (sin(pi*t) envelope), scaled by the current straight distance so the curve size tracks
        /// the shot length. A small wobble on top breaks the arc's symmetry so retrieval streams
        /// look organic rather than mechanical. Purely 2D — the perpendicular is the (-y, x) rotation
        /// of the travel vector in the XY plane, so Z stays 0.
        /// </summary>
        Vector3 CurveOffset(Vector3 from, Vector3 to, float t)
        {
            Vector3 delta = to - from;
            float distance = new Vector2(delta.x, delta.y).magnitude;
            if (distance < 0.01f) return Vector3.zero;

            Vector3 dir = delta / distance;
            Vector3 perp = new Vector3(-dir.y, dir.x, 0f) * _curveSign;

            float envelope = Mathf.Sin(Mathf.PI * t);   // 0 at ends, 1 at midpoint
            float arc = envelope * _curveAmount * distance;
            float wobble = envelope * _wobbleAmp * Mathf.Sin(_wobblePhase + t * Mathf.PI * _wobbleFreq);
            return perp * (arc + wobble);
        }

        void Arrive(Vector3 at)
        {
            if (_burstOnArrive) Particle.Burst(at, Blue, 10, 6f, 0.35f, 0.6f);
            _onArrive?.Invoke();
            Destroy(gameObject);
        }
    }
}
