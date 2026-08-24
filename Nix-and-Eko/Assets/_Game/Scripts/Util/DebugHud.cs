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

            GUILayout.BeginArea(new Rect(10, 10, 380, 210), GUI.skin.box);
            if (health != null) GUILayout.Label($"HP: {health.Current}", style);
            if (player != null) GUILayout.Label($"State: {player.currentState}", style);
            if (bow != null)
                GUILayout.Label(bow.CanFire
                    ? (bow.IsDrawing ? $"Draw: {(bow.Charge * 100f):0}%" : "Bow: ready")
                    : "Bow: spent - land to reload", style);
            GUILayout.Label("Keyboard: A/D move  Space jump  C crouch", style);
            GUILayout.Label("  Shoot: hold LMB, drag to aim, release", style);
            GUILayout.Label("Gamepad: stick/d-pad move  X jump  O crouch", style);
            GUILayout.Label("  Shoot: push right stick to aim, let go to fire", style);
            GUILayout.Label("  Hold R2 to keep aim, release R2 to fire", style);
            GUILayout.EndArea();
        }
    }
}
