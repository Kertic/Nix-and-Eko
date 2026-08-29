using NixAndEko.Combat;
using NixAndEko.Environment;
using NixAndEko.Player;
using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// Always-on top-left HUD: Nix's health bar and a small "Eko is riding with me" indicator.
    /// A little dark grey hole fills with a soft blue light while Eko is dormant (not summoned)
    /// so at a glance the player can tell whether the phantom is currently deployed. Kept as
    /// IMGUI to match the pause menu / debug HUD stack — no Canvas to wire up.
    /// </summary>
    public class PlayerHud : MonoBehaviour
    {
        public PlayerController player;
        public Health health;
        public EkoSummoner summoner;
        public Eko eko;

        [Header("Health bar")]
        public Vector2 origin = new Vector2(16f, 16f);
        public Vector2 barSize = new Vector2(220f, 18f);
        public Color barBack = new Color(0.05f, 0.06f, 0.1f, 0.9f);
        public Color barFrame = new Color(0f, 0f, 0f, 0.9f);
        public Color barFill = new Color(0.92f, 0.28f, 0.32f, 1f);
        public Color barFillLow = new Color(1f, 0.55f, 0.4f, 1f);

        [Header("Eko indicator")]
        public float holeDiameter = 24f;
        public float holeGap = 8f;
        public Color holeRim = new Color(0f, 0f, 0f, 0.9f);
        public Color holeInside = new Color(0.18f, 0.19f, 0.24f, 1f);
        public Color glowCore = new Color(0.95f, 0.99f, 1f, 1f);
        public Color glowMid = new Color(0.55f, 0.85f, 1f, 0.85f);
        public Color glowOuter = new Color(0.3f, 0.7f, 1f, 0.45f);

        Texture2D _fill;
        int _curHp, _maxHp;

        void Awake()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (health == null && player != null) health = player.GetComponent<Health>();
            if (summoner == null && player != null) summoner = player.GetComponent<EkoSummoner>();
            if (eko == null && summoner != null) eko = summoner.eko;
        }

        void Update()
        {
            // Poll rather than subscribe — matches DebugHud and side-steps the AddComponent
            // ordering where OnEnable runs before the loader assigns `health`. Cheap: one
            // property read and a config lookup per frame.
            if (health != null)
            {
                _curHp = health.Current;
                _maxHp = health.config != null ? health.config.maxHealth : Mathf.Max(_maxHp, 1);
                if (_maxHp <= 0) _maxHp = 5;
            }
        }

        void EnsureFill()
        {
            if (_fill != null) return;
            _fill = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _fill.SetPixel(0, 0, Color.white);
            _fill.filterMode = FilterMode.Point;
            _fill.Apply();
        }

        void OnGUI()
        {
            if (PauseMenu.IsGameplayPaused) return;   // hide while the pause menu is up
            if (health == null) return;
            EnsureFill();

            DrawHealthBar();
            DrawEkoIndicator();
        }

        void DrawHealthBar()
        {
            if (_maxHp <= 0) return;   // Health hasn't reported yet; skip a frame

            float x = origin.x, y = origin.y;
            float w = barSize.x, h = barSize.y;
            float frac = Mathf.Clamp01((float)_curHp / _maxHp);

            // frame + backdrop
            DrawRect(new Rect(x - 3f, y - 3f, w + 6f, h + 6f), barFrame);
            DrawRect(new Rect(x, y, w, h), barBack);

            // fill (colour tilts warmer as health drops so a low bar reads at a glance)
            Color fill = Color.Lerp(barFillLow, barFill, frac);
            DrawRect(new Rect(x, y, w * frac, h), fill);

            // segment ticks so each hit is a visible notch, not a smooth drain
            if (_maxHp > 1)
            {
                Color tick = new Color(0f, 0f, 0f, 0.55f);
                for (int i = 1; i < _maxHp; i++)
                {
                    float tx = x + (w / _maxHp) * i;
                    DrawRect(new Rect(tx - 1f, y, 1f, h), tick);
                }
            }

            var style = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            style.normal.textColor = new Color(1f, 1f, 1f, 0.95f);
            GUI.Label(new Rect(x + w + 8f, y + 1f, 80f, h), $"{_curHp}/{_maxHp}", style);
        }

        void DrawEkoIndicator()
        {
            float d = holeDiameter;
            float x = origin.x + d * 0.5f;
            float y = origin.y + barSize.y + holeGap + d * 0.5f;
            Vector2 c = new Vector2(x, y);

            // Dark rim + inside hole — always drawn, so the empty state still reads as a socket.
            DrawCircle(c, d * 0.5f + 2f, holeRim);
            DrawCircle(c, d * 0.5f, holeInside);

            bool ekoWithNix = eko == null || !eko.Active;
            if (!ekoWithNix) return;

            // Gentle pulse so the blue light reads as alive, not painted-on. Uses unscaledTime so
            // hitstop / pause-menu freeze doesn't stop the breath (pause is short-circuited above
            // anyway, so this really just matters during hitstop).
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 3.2f);
            Color outer = glowOuter; outer.a *= pulse;
            Color mid   = glowMid;   mid.a   *= pulse;
            Color core  = glowCore;  core.a  *= pulse;

            DrawCircle(c, d * 0.5f,           outer);
            DrawCircle(c, d * 0.36f,          mid);
            DrawCircle(c, d * 0.22f * pulse,  core);
        }

        void DrawRect(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _fill);
            GUI.color = prev;
        }

        /// <summary>Rough IMGUI filled circle — one horizontal strip per row. Cheap enough at
        /// ~50 rows per frame for a 25-radius disc, and no external mesh needed.</summary>
        void DrawCircle(Vector2 center, float radius, Color color)
        {
            if (radius <= 0.5f) return;
            Color prev = GUI.color;
            GUI.color = color;
            int r = Mathf.CeilToInt(radius);
            float r2 = radius * radius;
            for (int dy = -r; dy <= r; dy++)
            {
                float dxF = Mathf.Sqrt(Mathf.Max(0f, r2 - dy * dy));
                int dx = Mathf.RoundToInt(dxF);
                if (dx <= 0) continue;
                GUI.DrawTexture(new Rect(center.x - dx, center.y + dy, dx * 2f, 1f), _fill);
            }
            GUI.color = prev;
        }
    }
}
