using UnityEditor;
using UnityEngine;

namespace NixAndEko.EditorTools
{
    /// <summary>
    /// Turns on Unity's auto-refresh so saving a script recompiles it without needing focus
    /// tricks or a manual Ctrl+R, and lets scripts recompile while in Play mode.
    ///
    /// These live in EditorPrefs (per machine, not in the project), so this applies them once
    /// and records that it did — after that your own Preferences changes are left alone.
    /// Use the Tools menu items below to re-apply or check the current state.
    /// </summary>
    [InitializeOnLoad]
    public static class EditorAutoRefresh
    {
        // Preferences > Asset Pipeline > Auto Refresh (0 = disabled, 1 = enabled, 2 = outside play mode).
        const string AutoRefreshKey = "kAutoRefreshMode";
        // Preferences > General > Script Changes While Playing (0 = recompile and continue).
        const string ScriptChangesKey = "ScriptCompilationDuringPlay";
        // Our own marker so we only force the defaults once per machine.
        const string AppliedKey = "NixAndEko.AutoRefreshApplied";

        static EditorAutoRefresh()
        {
            if (EditorPrefs.GetBool(AppliedKey, false)) return;
            Apply();
            EditorPrefs.SetBool(AppliedKey, true);
        }

        [MenuItem("Tools/Nix & Eko/Enable Auto Recompile", priority = 40)]
        public static void Apply()
        {
            EditorPrefs.SetInt(AutoRefreshKey, 1);   // refresh assets as soon as they change
            EditorPrefs.SetInt(ScriptChangesKey, 0); // recompile and keep playing
            AssetDatabase.Refresh();
            Debug.Log("[Nix & Eko] Auto refresh enabled: scripts recompile on save, including during Play mode.");
        }

        [MenuItem("Tools/Nix & Eko/Log Auto Recompile State", priority = 41)]
        public static void LogState()
        {
            Debug.Log($"[Nix & Eko] Auto Refresh mode = {EditorPrefs.GetInt(AutoRefreshKey, -1)} " +
                      $"(1 = enabled), Script Changes While Playing = {EditorPrefs.GetInt(ScriptChangesKey, -1)} " +
                      "(0 = recompile and continue).");
        }
    }
}
