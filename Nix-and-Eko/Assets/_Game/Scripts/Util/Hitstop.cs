using System.Collections;
using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// A brief global freeze frame ("hitstop") — sets <see cref="Time.timeScale"/> to 0 for a short
    /// span of real time, then restores it. Used for the Nix &lt;-&gt; Eko swap so the exchange lands
    /// with a punch. Hosted on a lazily-created, hidden, DontDestroyOnLoad runner so callers don't
    /// need their own MonoBehaviour, and overlapping requests coalesce onto the longest one.
    /// </summary>
    public static class Hitstop
    {
        class Runner : MonoBehaviour { }

        static Runner _runner;
        static Coroutine _active;
        static float _restoreScale = 1f;

        static Runner GetRunner()
        {
            if (_runner != null) return _runner;
            var go = new GameObject("~Hitstop");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _runner = go.AddComponent<Runner>();
            return _runner;
        }

        /// <summary>Freeze for <paramref name="realSeconds"/> of unscaled time, then restore.</summary>
        public static void Freeze(float realSeconds)
        {
            if (realSeconds <= 0f) return;
            var runner = GetRunner();

            if (_active != null) runner.StopCoroutine(_active);
            else _restoreScale = Time.timeScale;   // remember the pre-freeze scale only on a fresh freeze

            _active = runner.StartCoroutine(FreezeRoutine(realSeconds));
        }

        static IEnumerator FreezeRoutine(float realSeconds)
        {
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(realSeconds);
            Time.timeScale = _restoreScale;
            _active = null;
        }
    }
}
