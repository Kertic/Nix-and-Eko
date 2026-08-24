using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Player
{
    /// <summary>
    /// A small two-part meter (dark backing + colored fill) that floats under the player and
    /// shows remaining glide fuel while airborne. Built from procedural sprites in code, like
    /// the rest of the placeholder art — no art asset to keep in sync. Parented directly under
    /// the player root (not the flipping <see cref="PlayerController.spriteRoot"/>), so it never
    /// mirrors with facing.
    /// </summary>
    public class GlideFuelBar : MonoBehaviour
    {
        public PlayerController player;

        [Header("Placement")]
        public Vector2 offset = new Vector2(0f, -0.55f);
        public int widthPixels = 16;
        public int heightPixels = 3;

        [Header("Color")]
        public Color backColor = new Color(0f, 0f, 0f, 0.55f);
        public Color fillColor = Palette.Blue;
        [Tooltip("Fill tints toward this color as fuel runs low.")]
        public Color lowFillColor = Palette.Red;

        SpriteRenderer _back, _fill;
        Transform _fillT;

        void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            Build();
        }

        void Build()
        {
            transform.localPosition = offset;

            var backGo = new GameObject("Back");
            backGo.transform.SetParent(transform, false);
            _back = backGo.AddComponent<SpriteRenderer>();
            _back.sortingOrder = 15;
            _back.sprite = SpriteFactory.SolidRect(backColor, widthPixels, heightPixels);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(transform, false);
            // Anchor to the backing's left edge so shrinking the fill drains it from the right.
            fillGo.transform.localPosition = new Vector3(-widthPixels * 0.5f / SpriteFactory.PPU, 0f, 0f);
            _fill = fillGo.AddComponent<SpriteRenderer>();
            _fill.sortingOrder = 16;
            _fill.sprite = SpriteFactory.Bar(fillColor, widthPixels, heightPixels);
            _fillT = fillGo.transform;
        }

        void LateUpdate()
        {
            if (player == null || player.Config == null) return;

            // Only meaningful in the air — hidden the moment you're back on the ground.
            bool show = !player.Grounded;
            if (_back != null) _back.enabled = show;
            if (_fill != null) _fill.enabled = show;
            if (!show) return;

            float duration = Mathf.Max(0.01f, player.Config.glideDuration);
            float frac = Mathf.Clamp01(player.GlideFuel / duration);
            _fillT.localScale = new Vector3(frac, 1f, 1f);
            _fill.color = Color.Lerp(lowFillColor, fillColor, frac);
        }
    }
}
