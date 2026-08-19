using NixAndEko.Combat;
using NixAndEko.Environment;
using NixAndEko.Player;
using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>Tiny IMGUI overlay for playtesting: shows health, state and controls. Remove for shipping.</summary>
    public class DebugHud : MonoBehaviour
    {
        public PlayerController player;
        public Health health;
        public Bow bow;

        void Awake()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (health == null && player != null) health = player.GetComponent<Health>();
            if (bow == null && player != null) bow = player.GetComponentInChildren<Bow>();
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            style.normal.textColor = Color.white;

            GUILayout.BeginArea(new Rect(10, 10, 320, 150), GUI.skin.box);
            if (health != null) GUILayout.Label($"HP: {health.Current}", style);
            if (player != null) GUILayout.Label($"State: {player.currentState}", style);
            if (bow != null && bow.IsDrawing)
                GUILayout.Label($"Draw: {(bow.Charge * 100f):0}%", style);
            GUILayout.Label("Move: A/D  Jump: Space", style);
            GUILayout.Label("Crouch: Ctrl  Shoot: hold/release LMB", style);
            GUILayout.EndArea();
        }
    }
}
