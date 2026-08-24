using NixAndEko.Player;
using UnityEditor;
using UnityEngine;

namespace NixAndEko.EditorTools
{
    /// <summary>
    /// Custom inspector for <see cref="PlayerConfig"/>. There's no button-jump any more — the
    /// closest thing to a "jump" is a full-charge shot fired straight down (or down-left /
    /// down-right), which recoils the player upward. Rather than authoring jump height directly,
    /// it falls out of recoilMax / gravityUp / gravityDown / moveSpeed, so this box surfaces the
    /// resulting numbers read-only wherever the config is edited (this asset, or the live
    /// "Config Values" foldout on <see cref="PlayerController"/>) so the effect of a tweak is
    /// visible immediately.
    /// </summary>
    [CustomEditor(typeof(PlayerConfig))]
    public class PlayerConfigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var config = (PlayerConfig)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Derived \"Jump\" Stats", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "There's no button-jump — a \"jump\" is a full-charge shot fired straight down " +
                "(or down-left / down-right), which recoils the player upward at recoilMax. " +
                "These are computed from recoilMax, gravityUp, gravityDown, maxFallSpeed and " +
                "moveSpeed above — tune those to change them.",
                MessageType.None);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.FloatField("Max Jump Height", config.MaxJumpHeight);
                EditorGUILayout.FloatField("Max Air Time", config.MaxAirTime);
                EditorGUILayout.FloatField("Max Jump Distance (flat ground)", config.MaxJumpDistance);
            }
        }
    }
}
