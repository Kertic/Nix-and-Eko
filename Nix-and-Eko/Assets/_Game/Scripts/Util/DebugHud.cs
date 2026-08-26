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

            GUILayout.BeginArea(new Rect(10, 10, 420, 230), GUI.skin.box);
            if (health != null) GUILayout.Label($"HP: {health.Current}", style);
            if (player != null) GUILayout.Label($"State: {player.currentState}", style);
            if (bow != null)
                GUILayout.Label(!bow.HasAnyArrow
                    ? "Bow: empty - walk over your arrow to reclaim it"
                    : bow.FiresBlueNext
                        ? (bow.HasNormalArrow ? "Bow: blue arrow ready (normal held too)" : "Bow: blue arrow ready")
                        : "Bow: ready", style);
            GUILayout.Label("Keyboard: A/D move  Space jump  C crouch", style);
            GUILayout.Label("  Mouse aims. LMB = Nix Bow, RMB = Nix Melee, Q = Eko", style);
            GUILayout.Label("Gamepad: stick/d-pad move  X jump  O crouch", style);
            GUILayout.Label("  Right stick aims. R2 = Nix Bow, R1 = Nix Melee, L1 = Eko", style);
            GUILayout.Label("L1 = possess Eko (walks/jumps, can't fire). Nix Bow button while " +
                             "possessed = return home (grounded) or vanish (airborne).", style);
            GUILayout.EndArea();
        }
    }
}
