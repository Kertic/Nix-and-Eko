using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// A world-anchored floating text label — used by tutorial rooms to hint at mechanics
    /// ("Hold LMB, drag, release to fire") without a manual. Drawn via IMGUI so it needs no
    /// TextMesh Pro dependency and always renders on top of the scene, sharing the styling
    /// convention of <see cref="PlayerStateLabel"/> (bold, small, 1px drop shadow, richText on).
    ///
    /// The label is pinned to <see cref="transform"/>'s world position; place the block in
    /// the LevelData where you want the text to hover.
    /// </summary>
    public class RoomSign : MonoBehaviour
    {
        [TextArea] public string text = "";
        [Tooltip("World units above the transform position to draw the label.")]
        public float verticalOffset = 0.2f;
        [Tooltip("On-screen font size (matches PlayerStateLabel by default).")]
        public int fontSize = 13;
        public Color textColor   = new Color(1f, 0.94f, 0.72f, 1f);
        public Color shadowColor = new Color(0f, 0f, 0f, 0.85f);
        [Tooltip("Beyond this world-space distance from the camera the sign fades out, so a " +
                 "sign in a distant room doesn't shout over the one Nix is standing next to.")]
        public float visibilityRange = 22f;

        Camera _cam;
        GUIStyle _style;

        void OnGUI()
        {
            if (string.IsNullOrEmpty(text)) return;

            if (_cam == null || !_cam.isActiveAndEnabled)
                _cam = Camera.main != null ? Camera.main : Camera.allCameras.Length > 0 ? Camera.allCameras[0] : null;
            if (_cam == null) return;

            EnsureStyle();

            Vector3 world = transform.position + Vector3.up * verticalOffset;
            Vector3 vp    = _cam.WorldToViewportPoint(world);
            if (vp.z < 0f) return;                                  // behind camera
            if (vp.x < -0.05f || vp.x > 1.05f) return;              // well off-screen horizontally
            if (vp.y < -0.05f || vp.y > 1.05f) return;

            // Distance fade: fully opaque within half the range, linear ramp to zero at the edge.
            float dist = Vector3.Distance(_cam.transform.position, world);
            float fade = 1f - Mathf.InverseLerp(visibilityRange * 0.5f, visibilityRange, dist);
            if (fade <= 0f) return;

            _style.fontSize = fontSize;
            var content = new GUIContent(text);
            var size    = _style.CalcSize(content) + new Vector2(8f, 4f);
            float screenX = vp.x * Screen.width;
            float screenY = (1f - vp.y) * Screen.height;    // flip Y for GUI space
            float x = screenX - size.x * 0.5f;
            float y = screenY - size.y - 4f;

            // Keep the sign on-screen with a small margin even when the camera nearly leaves it.
            const float margin = 2f;
            x = Mathf.Clamp(x, margin, Screen.width  - size.x - margin);
            y = Mathf.Clamp(y, margin, Screen.height - size.y - margin);
            var rect = new Rect(x, y, size.x, size.y);

            // 1px shadow first (rich-text off so any tags don't leak as characters), then the tint.
            var shadow = shadowColor; shadow.a *= fade;
            _style.normal.textColor = shadow;
            _style.richText = false;
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height),
                      StripRichText(text), _style);

            var tint = textColor; tint.a *= fade;
            _style.normal.textColor = tint;
            _style.richText = true;
            GUI.Label(rect, text, _style);
        }

        void EnsureStyle()
        {
            if (_style != null) return;
            // Clone GUI.skin.label so we inherit the built-in font — same reason as PlayerStateLabel.
            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                wordWrap = true,
            };
        }

        static string StripRichText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.IndexOf('<') < 0) return s;
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
