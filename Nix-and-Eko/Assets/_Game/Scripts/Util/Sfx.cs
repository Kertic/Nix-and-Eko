using System;
using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// Tiny procedural sound-effect synth: every clip is generated in code (sine/square sweeps,
    /// filtered noise, tones with exponential decay) and cached, so there are no audio assets to
    /// import — the same "everything is generated" approach the sprites use. Clips play through a
    /// small pool of 2D AudioSources on a hidden, DontDestroyOnLoad runner, so callers just do
    /// <c>Sfx.Play(Sfx.Id.Bow)</c>. Needs an AudioListener in the scene (the main camera has one).
    /// </summary>
    public static class Sfx
    {
        public enum Id { Jump, Melee, Bow, EnemyHit, Land, EkoCatch, EkoSpawn, EkoZip }

        const int SampleRate = 44100;

        /// <summary>Global multiplier for all effects (0..1).</summary>
        public static float MasterVolume = 0.45f;

        class Runner : MonoBehaviour { }

        static Runner _runner;
        static AudioSource[] _sources;
        static int _next;
        static readonly AudioClip[] _clips = new AudioClip[8];

        static void EnsureRunner()
        {
            if (_runner != null) return;
            var go = new GameObject("~Sfx");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _runner = go.AddComponent<Runner>();

            _sources = new AudioSource[12];
            for (int i = 0; i < _sources.Length; i++)
            {
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = false;
                s.spatialBlend = 0f;   // 2D
                _sources[i] = s;
            }
        }

        /// <summary>Play effect <paramref name="id"/> once. <paramref name="pitch"/> and <paramref name="volume"/> scale it.</summary>
        public static void Play(Id id, float pitch = 1f, float volume = 1f)
        {
            if (MasterVolume <= 0f) return;
            EnsureRunner();

            int i = (int)id;
            if (_clips[i] == null) _clips[i] = Build(id);

            var src = _sources[_next];
            _next = (_next + 1) % _sources.Length;
            src.pitch = Mathf.Clamp(pitch, 0.25f, 3f);
            src.volume = Mathf.Clamp01(volume) * MasterVolume;
            src.clip = _clips[i];
            src.Play();
        }

        // ------------------------------------------------------------------ clip definitions
        static AudioClip Build(Id id)
        {
            float[] s = id switch
            {
                // Rising whistle — the bow-recoil launch.
                Id.Jump => Sweep(320f, 720f, 0.14f, 5f, 0.5f),
                // Airy swish with a downward body — a blade sweep.
                Id.Melee => Mix(Noise(0.11f, 26f, 0.55f), Sweep(700f, 260f, 0.10f, 16f, 0.25f)),
                // Sharp "thwip" plus a click of noise.
                Id.Bow => Mix(Sweep(1000f, 380f, 0.09f, 20f, 0.4f, square: true), Noise(0.06f, 40f, 0.3f)),
                // Low punchy "thunk".
                Id.EnemyHit => Mix(Sweep(240f, 90f, 0.12f, 13f, 0.5f, square: true), Noise(0.08f, 30f, 0.4f)),
                // Soft low thud.
                Id.Land => Mix(Sweep(200f, 70f, 0.12f, 12f, 0.45f), Noise(0.07f, 26f, 0.25f)),
                // Bright two-tone chime — Nix caught Eko's arrow (reloaded).
                Id.EkoCatch => Mix(Tone(880f, 0.20f, 5f, 0.4f), Tone(1320f, 0.20f, 5f, 0.25f)),
                // Shimmery rise — a phantom appears.
                Id.EkoSpawn => Mix(Sweep(520f, 940f, 0.24f, 3.5f, 0.35f), Tone(1400f, 0.18f, 6f, 0.2f)),
                // Quick falling whoosh — an Eko orb zips.
                Id.EkoZip => Sweep(1300f, 430f, 0.16f, 9f, 0.4f),
                _ => Tone(440f, 0.1f, 8f, 0.3f),
            };

            FadeEdges(s);
            var clip = AudioClip.Create(id.ToString(), s.Length, 1, SampleRate, false);
            clip.SetData(s, 0);
            return clip;
        }

        // ------------------------------------------------------------------ generators
        static float[] Tone(float freq, float dur, float decay, float amp)
        {
            int n = Mathf.Max(1, (int)(SampleRate * dur));
            var s = new float[n];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                ph += 2.0 * Math.PI * freq / SampleRate;
                s[i] = (float)Math.Sin(ph) * Mathf.Exp(-decay * t) * amp;
            }
            return s;
        }

        static float[] Sweep(float f0, float f1, float dur, float decay, float amp, bool square = false)
        {
            int n = Mathf.Max(1, (int)(SampleRate * dur));
            var s = new float[n];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f = Mathf.Lerp(f0, f1, t);
                ph += 2.0 * Math.PI * f / SampleRate;
                float w = square ? (Math.Sin(ph) >= 0 ? 1f : -1f) : (float)Math.Sin(ph);
                s[i] = w * Mathf.Exp(-decay * t) * amp;
            }
            return s;
        }

        static float[] Noise(float dur, float decay, float amp)
        {
            int n = Mathf.Max(1, (int)(SampleRate * dur));
            var s = new float[n];
            var rng = new System.Random(1234);
            float last = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                last = Mathf.Lerp(last, white, 0.4f);   // gentle low-pass so it's a whoosh, not a hiss
                s[i] = last * Mathf.Exp(-decay * t) * amp;
            }
            return s;
        }

        static float[] Mix(float[] a, float[] b)
        {
            int n = Mathf.Max(a.Length, b.Length);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float v = 0f;
                if (i < a.Length) v += a[i];
                if (i < b.Length) v += b[i];
                s[i] = Mathf.Clamp(v, -1f, 1f);
            }
            return s;
        }

        /// <summary>Ramp the first/last few ms to zero so clips don't click on start/stop.</summary>
        static void FadeEdges(float[] s)
        {
            int fin = Mathf.Min(64, s.Length / 8);
            for (int i = 0; i < fin; i++) s[i] *= i / (float)fin;

            int fout = Mathf.Min(400, s.Length / 3);
            for (int i = 0; i < fout; i++) s[s.Length - 1 - i] *= i / (float)fout;
        }
    }
}
