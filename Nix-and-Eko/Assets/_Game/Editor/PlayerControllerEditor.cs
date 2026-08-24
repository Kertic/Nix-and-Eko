using NixAndEko.Player;
using UnityEditor;
using UnityEngine;

namespace NixAndEko.EditorTools
{
    /// <summary>
    /// Custom inspector for <see cref="PlayerController"/> that embeds the assigned
    /// <see cref="PlayerConfig"/>'s fields (gravity, max fall speed, move speed, jump height, …)
    /// directly on the player, so they can be tuned right here instead of hunting down the asset.
    ///
    /// In Play mode <see cref="PlayerController.Config"/> is the per-instance runtime copy made in
    /// Awake, so scrubbing these values is live and never writes back to the shared asset that
    /// holds the defaults. At edit time it's the shared asset itself, so a help box says so.
    /// </summary>
    [CustomEditor(typeof(PlayerController))]
    public class PlayerControllerEditor : Editor
    {
        const string FoldoutKey = "NixAndEko.PlayerControllerEditor.ConfigFoldout";

        Editor _configEditor;

        void OnDisable()
        {
            if (_configEditor != null) DestroyImmediate(_configEditor);
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var controller = (PlayerController)target;
            PlayerConfig config = controller.Config;

            EditorGUILayout.Space();
            if (config == null)
            {
                EditorGUILayout.HelpBox("Assign a Player Config to tune it here.", MessageType.Info);
                return;
            }

            bool open = SessionState.GetBool(FoldoutKey, true);
            open = EditorGUILayout.BeginFoldoutHeaderGroup(open, "Config Values (live tuning)");
            SessionState.SetBool(FoldoutKey, open);

            if (open)
            {
                if (Application.isPlaying)
                    EditorGUILayout.HelpBox(
                        "Editing the runtime copy: changes take effect immediately and are NOT " +
                        "saved back to the PlayerConfig asset. Stop play to keep the defaults.",
                        MessageType.Info);
                else
                    EditorGUILayout.HelpBox(
                        "Editing the shared PlayerConfig asset (the defaults). Enter Play mode to " +
                        "tune this instance live without touching the defaults.",
                        MessageType.Warning);

                using (new EditorGUI.IndentLevelScope())
                {
                    CreateCachedEditor(config, null, ref _configEditor);
                    if (_configEditor != null) _configEditor.OnInspectorGUI();
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }
    }
}
