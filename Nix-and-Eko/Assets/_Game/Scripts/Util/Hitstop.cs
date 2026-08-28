using System.Collections;
using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// A brief global freeze frame ("hitstop") — sets <see cref="Time.timeScale"/> to 0 for a short
    /// span of real time, then restores it to 1. Used for the Nix &lt;-&gt; Eko swap so the exchange
    /// lands with a punch. Hosted on a lazily-created, hidden, DontDestroyOnLoad runner so callers
    /// don't need their own MonoBehaviour. The game never uses a timeScale other than 1, so restore
    /// is unconditional — that keeps a stale static from a prior editor Play session (with domain
    /// reload disabled) from ever leaving the game stuck frozen.
    /// </summary>
    public static class Hitstop
    {
        class Runner : MonoBehaviour { }

        static Runner _runner;
        static Coroutine _active;

        static Runner GetRunner()
        {
            if (_runner != null) return _runner;
            // Recreating means any prior runner (and its coroutine handle) is dead — clear it.
            _active = null;
            var go = new GameObject("~Hitstop");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _runner = go.AddComponent<Runner>();
            return _runner;
        }

        /// <summary>Freeze for <paramref name="realSeconds"/> of unscaled time, then restore to 1.</summary>
        public static void Freeze(float realSeconds)
        {
            if (realSeconds <= 0f) return;
            var runner = GetRunner();
            if (_active != null) runner.StopCoroutine(_active);
            _active = runner.StartCoroutine(FreezeRoutine(realSeconds));
        }

        static IEnumerator FreezeRoutine(float realSeconds)
        {
            // Snapshot the scale we're stealing so we hand it back on release rather than
            // hard-setting 1 — otherwise a hitstop that happens to overlap the pause menu (which
            // also drives timeScale to 0) would silently unpause the game the instant the hitstop
            // ended. And when the pause opens *during* a hitstop, we still hand back to 1 on
            // resume because pause captures its own snapshot at open time (which will be 0 here);
            // pause's own restore stays authoritative for the paused-then-resumed case.
            float prior = Time.timeScale;
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(realSeconds);
            Time.timeScale = prior > 0f ? prior : 1f;
            _active = null;
        }
    }
}
