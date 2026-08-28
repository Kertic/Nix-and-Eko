using NixAndEko.Player;
using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// Floating debug label pinned above every live <see cref="PlayerController"/> in the scene,
    /// showing the current state name and the facing arrow — useful for verifying state-machine
    /// transitions in playtest, and for screenshots when reporting bugs (attaching a shot with
    /// visible state saves a round trip of "which state was that in?"). One instance handles
    /// every player, so both Nix and Eko get their own label without extra wiring.
    ///
    /// The toggle is exposed via <see cref="Enabled"/>, which persists to
    /// <see cref="PlayerPrefs"/> so preference sticks across sessions. The pause menu's
    /// State-Labels button flips it.
    /// </summary>
    public class PlayerStateLabel : MonoBehaviour
    {
        const string PrefKey = "NixEko.ShowStateLabels.v1";

        /// <summary>Persisted user preference. Default on so playtest gets it out of the box.</summary>
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefKey, 1) != 0;
            set { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        [Tooltip("World units above the player's centre to place the label.")]
        public float verticalOffset = 1.1f;
        [Tooltip("On-screen font size for the label.")]
        public int fontSize = 13;
        public Color textColor   = new Color(1f, 1f, 1f, 0.95f);
        public Color shadowColor = new Color(0f, 0f, 0f, 0.85f);
        public Color accentColor = new Color(1f, 0.85f, 0.35f, 1f);

        Camera _cam;
        GUIStyle _style;

        void OnGUI()
        {
            if (!Enabled) return;
            if (_cam == null || !_cam.isActiveAndEnabled)
                _cam = Camera.main != null ? Camera.main : Camera.allCameras.Length > 0 ? Camera.allCameras[0] : null;
            if (_cam == null) return;

            EnsureStyle();

            // Cheap enough — FindObjectsByType is O(n) over live scene objects, and the pause
            // menu / hitstop / pause path can't sneak an extra player past it. Called from OnGUI
            // which may run multiple times per frame; the query cost is trivial next to the layout
            // pass IMGUI is already doing.
            var players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            foreach (var p in players)
            {
                if (p == null || !p.isActiveAndEnabled) continue;
                DrawLabel(p);
            }
        }

        void EnsureStyle()
        {
            if (_style != null) return;
            // MUST clone GUI.skin.label rather than `new GUIStyle()` — a fresh style has no font
            // set, and IMGUI renders nothing when the font is null (the label just doesn't show
            // up). Copying the skin's label style inherits the built-in font, which is why every
            // other GUILayout.Label in the codebase works. GUI.skin is only valid inside an OnGUI
            // pass, so we build the style lazily on first paint.
            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
            };
        }

        void DrawLabel(PlayerController p)
        {
            Vector3 world  = p.transform.position + Vector3.up * verticalOffset;
            // Viewport coords (0..1) instead of WorldToScreenPoint's pixel coords: the
            // PixelPerfectCamera renders into a small internal RT (e.g. 384x216) and
            // WorldToScreenPoint returns pixels in that space, so the label was landing in a
            // tiny corner of the actual game view (which is Screen.width/height). Viewport is
            // resolution-independent so we can multiply by Screen.width/height directly and
            // land on the real pixel where the character is drawn.
            Vector3 vp = _cam.WorldToViewportPoint(world);
            if (vp.z < 0f) return;   // behind the camera

            string state = string.IsNullOrEmpty(p.currentState) ? "?" : p.currentState;
            string arrow = p.Facing < 0 ? "◀" : "▶";
            string text  = $"<color=#FFD860>{arrow}</color> {state}";

            _style.fontSize = fontSize;
            // CalcSize can trim the last glyph on some GUI skins (kerning / italic overhang), and
            // rich-text tags aren't accounted for. Pad a couple of pixels on width and a hair on
            // height so nothing gets clipped by the tight rect. Prefer overshoot to undershoot —
            // GUI.Label happily draws inside a slightly larger rect.
            var size = _style.CalcSize(new GUIContent(text)) + new Vector2(6f, 2f);
            float screenX = vp.x * Screen.width;
            float screenY = (1f - vp.y) * Screen.height;   // flip Y for GUI space
            float x = screenX - size.x * 0.5f;
            float y = screenY - size.y - 6f;

            // Clamp to the screen bounds. Player near the top of the frame would push the label
            // past y=0 and IMGUI clips whatever falls off the top edge — pinning it to a small
            // top margin keeps the readout visible even when the character is climbing the ceiling.
            const float margin = 2f;
            x = Mathf.Clamp(x, margin, Screen.width  - size.x - margin);
            y = Mathf.Clamp(y, margin, Screen.height - size.y - margin);
            var rect = new Rect(x, y, size.x, size.y);

            // 1px shadow pass for legibility on any backdrop, then the tinted label.
            _style.normal.textColor = shadowColor;
            _style.richText = false;
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height),
                      StripRichText(text), _style);

            _style.normal.textColor = textColor;
            _style.richText = true;
            GUI.Label(rect, text, _style);
        }

        // The shadow layer has richText off (so the color tag doesn't leak into it as literal
        // characters); strip the tags for that pass so the drop shadow lines up with the visible
        // text exactly.
        static string StripRichText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int a = s.IndexOf('<');
            if (a < 0) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            bool inTag = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<') inTag = true;
                else if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
