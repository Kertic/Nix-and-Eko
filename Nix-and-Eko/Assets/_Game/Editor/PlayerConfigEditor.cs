using NixAndEko.Player;
using UnityEditor;
using UnityEngine;

namespace NixAndEko.EditorTools
{
    /// <summary>
    /// Custom inspector for <see cref="PlayerConfig"/>. There's no button-jump any more — the
    /// closest thing to a "jump" is a full-charge shot fired straight down (or down-left /
    /// down-right), which recoils the player upward. Rather than authoring jump height directly,
    /// it falls out of recoilMax / gravityUp / gravityDown / moveSpeed — this box surfaces the
    /// resulting numbers (this asset, or the live "Config Values" foldout on
    /// <see cref="PlayerController"/>) so the effect of a tweak is visible immediately.
    ///
    /// Each derived stat also has a Lock toggle. Locking one makes it the thing you author — type
    /// a height, distance or air time directly — and the one physics field it governs (gravityUp,
    /// gravityDown or moveSpeed respectively) becomes read-only and gets solved instead, so
    /// tweaking anything else that feeds the formula can't drift the locked number. The three
    /// locks each own a different field, so any combination of them can be active at once without
    /// fighting each other — see <see cref="PlayerConfig.ResolveJumpLocks"/>.
    /// </summary>
    [CustomEditor(typeof(PlayerConfig))]
    public class PlayerConfigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var config = (PlayerConfig)target;
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            DrawFieldsWithLockedOverrides(config);
            bool basePhysicsChanged = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();

            if (basePhysicsChanged)
            {
                Undo.RecordObject(config, "Edit Player Config");
                config.ResolveJumpLocks();
                EditorUtility.SetDirty(config);
            }

            DrawDerivedStats(config);
        }

        /// <summary>
        /// Every ordinary serialized field, in its normal declared order/headers — except
        /// gravityUp / gravityDown / moveSpeed are disabled whenever the lock that solves them is
        /// active, since they're computed in that state rather than authored.
        /// </summary>
        void DrawFieldsWithLockedOverrides(PlayerConfig config)
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                bool isScript = iterator.propertyPath == "m_Script";
                bool solvedField = IsSolvedField(iterator.name, config);

                using (new EditorGUI.DisabledScope(isScript || solvedField))
                    EditorGUILayout.PropertyField(iterator, true);
            }
        }

        static bool IsSolvedField(string fieldName, PlayerConfig config)
        {
            if (fieldName == nameof(PlayerConfig.gravityUp)) return config.lockJumpHeight;
            if (fieldName == nameof(PlayerConfig.gravityDown)) return config.lockAirTime;
            if (fieldName == nameof(PlayerConfig.moveSpeed)) return config.lockJumpDistance;
            return false;
        }

        void DrawDerivedStats(PlayerConfig config)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Derived \"Jump\" Stats", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "There's no button-jump — a \"jump\" is a full-charge shot fired straight down " +
                "(or down-left / down-right), which recoils the player upward at recoilMax. " +
                "Tick Lock next to a stat to author it directly instead of just reading it off — " +
                "the physics field it governs (grayed out above) is solved to match, so tweaking " +
                "anything else that feeds the formula keeps this number exact.",
                MessageType.None);

            // Draw against local copies (not the target directly) so Undo.RecordObject can run
            // *before* anything is actually written to the asset — same pattern LevelEditorWindow
            // uses for its own manually-drawn (non-SerializedProperty) fields.
            bool lockHeight = config.lockJumpHeight; float targetHeight = config.targetJumpHeight;
            bool lockTime = config.lockAirTime; float targetTime = config.targetAirTime;
            bool lockDist = config.lockJumpDistance; float targetDist = config.targetJumpDistance;

            EditorGUI.BeginChangeCheck();
            (lockHeight, targetHeight) = DrawLockedStat(
                "Max Jump Height (m)", lockHeight, targetHeight, config.MaxJumpHeight, "solves gravityUp");
            (lockTime, targetTime) = DrawLockedStat(
                "Max Air Time (s)", lockTime, targetTime, config.MaxAirTime, "solves gravityDown");
            (lockDist, targetDist) = DrawLockedStat(
                "Max Jump Distance (m)", lockDist, targetDist, config.MaxJumpDistance, "solves moveSpeed");

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(config, "Edit Jump Lock");
                config.lockJumpHeight = lockHeight; config.targetJumpHeight = targetHeight;
                config.lockAirTime = lockTime; config.targetAirTime = targetTime;
                config.lockJumpDistance = lockDist; config.targetJumpDistance = targetDist;
                config.ResolveJumpLocks();
                EditorUtility.SetDirty(config);
            }

            if (config.lockAirTime)
            {
                float upTime = config.JumpLaunchSpeed / Mathf.Max(0.01f, config.gravityUp);
                float minFeasible = upTime + config.MaxJumpHeight / Mathf.Max(0.01f, config.maxFallSpeed);
                if (config.targetAirTime < minFeasible - 0.005f)
                    EditorGUILayout.HelpBox(
                        $"{config.targetAirTime:0.00}s is below the fastest this jump can physically " +
                        $"land ({minFeasible:0.00}s, falling the whole way at maxFallSpeed) — clamped " +
                        "to that floor.", MessageType.Warning);
            }
        }

        /// <summary>
        /// One row: a Lock toggle plus the stat's value — read-only and live while unlocked,
        /// editable (and authoritative) while locked. Locking seeds the target with the current
        /// live value so the number doesn't jump the moment the box is ticked.
        /// </summary>
        static (bool locked, float target) DrawLockedStat(
            string label, bool locked, float target, float liveValue, string tooltip)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool newLocked = EditorGUILayout.ToggleLeft(
                    new GUIContent("Lock", tooltip), locked, GUILayout.Width(50f));
                if (newLocked != locked && newLocked)
                    target = liveValue;
                locked = newLocked;

                using (new EditorGUI.DisabledScope(!locked))
                {
                    float shown = EditorGUILayout.FloatField(label, locked ? target : liveValue);
                    if (locked) target = Mathf.Max(0.01f, shown);
                }
            }
            return (locked, target);
        }
    }
}
