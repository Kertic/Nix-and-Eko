using NixAndEko.Combat;
using NixAndEko.Environment;
using NixAndEko.Player;
using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// Tiny IMGUI overlay for playtesting: shows health, state and controls. Toggled from the
    /// pause menu's Debug submenu; the preference persists in <see cref="PlayerPrefs"/>. Default
    /// off so a fresh session doesn't have the readout pasted over the top of the game — turn it
    /// on when you need it.
    /// </summary>
    public class DebugHud : MonoBehaviour
    {
        const string PrefKey = "NixEko.ShowDebugHud.v1";

        /// <summary>Persisted user preference. Default off.</summary>
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefKey, 0) != 0;
            set { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public PlayerController player;
        public Health health;
        public Bow bow;

        // Snapshot of Enabled for the current frame. OnGUI must NOT read the live PlayerPrefs
        // value directly: Unity runs OnGUI in separate Layout and Repaint passes, and the pause
        // menu's Debug page can flip Enabled (via a button click) in between them within the same
        // frame — since this component has no explicit execution order it runs before PauseMenu,
        // so Layout would see the old value (0 controls laid out) and Repaint the new one (a full
        // BeginArea of controls), producing IMGUI's "control N's position in a group with only N
        // controls" mismatch. Caching once in Update keeps both passes consistent. See the same
        // class of bug documented on PauseMenu.GoToPage.
        bool _enabledThisFrame;

        void Awake()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (health == null && player != null) health = player.GetComponent<Health>();
            if (bow == null && player != null) bow = player.GetComponentInChildren<Bow>();
        }

        void Update()
        {
            _enabledThisFrame = Enabled;
        }

        void OnGUI()
        {
            if (!_enabledThisFrame) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            style.normal.textColor = Color.white;

            // Anchored to the right so the top-left stays clear for the always-on player HUD
            // (health bar + Eko-presence indicator). 10 px margin from the right edge.
            const float w = 500f, h = 290f;
            GUILayout.BeginArea(new Rect(Screen.width - w - 10f, 10f, w, h), GUI.skin.box);
            if (health != null) GUILayout.Label($"HP: {health.Current}", style);
            if (player != null) GUILayout.Label($"State: {player.currentState}", style);
            if (bow != null)
                GUILayout.Label(!bow.HasAnyArrow
                    ? "Bow: empty - walk over your arrow, or R2 to send Eko to fetch"
                    : bow.FiresBlueNext
                        ? (bow.HasNormalArrow ? "Bow: blue arrow ready (normal held too)" : "Bow: blue arrow ready")
                        : "Bow: ready", style);
            GUILayout.Label("Keyboard: A/D move  Space jump  C crouch", style);
            GUILayout.Label("  Mouse aims. LMB = Nix Bow, RMB = Nix Melee, Q = Eko", style);
            GUILayout.Label("Gamepad: stick/d-pad move  X jump  O crouch", style);
            GUILayout.Label("  Right stick aims. R2 = Nix Bow, R1 = Nix Melee, L1 = Eko", style);
            GUILayout.Label("Eko rides Nix's arrow. Bright blue = normal, pale = spectral.", style);
            GUILayout.Label("  L1 tap (stuck arrow, once per airtime): DASH along blue tether.", style);
            GUILayout.Label("  L1 hold: arrow MORPHS; camera focus; morph done -> TIME FREEZES.", style);
            GUILayout.Label("  Aim is full 360. Release L1: phantom fires spectral, holds spot.", style);
            GUILayout.Label("  Spectral catches Nix -> +spectral slot, momentum, +1 air jump.", style);
            GUILayout.Label("  Nix lands: phantom orbs home + your normal arrow returns.", style);
            GUILayout.Label("  Walk-over pickup is OFF. R2 (grounded, no arrow) = Eko fetch.", style);
            GUILayout.EndArea();
        }
    }
}
