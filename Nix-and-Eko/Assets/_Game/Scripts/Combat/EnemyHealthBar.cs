using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// A small red health bar that floats above an enemy's head. Hidden until the enemy first takes
    /// damage, then it stays up. Built from procedural sprites in code; the fill uses a left-pivoted
    /// <see cref="SpriteFactory.Bar"/> so scaling its transform on X drains it from the right without
    /// repositioning. Parented under the enemy root (not the flipping sprite) so it never mirrors.
    /// </summary>
    public class EnemyHealthBar : MonoBehaviour
    {
        [Header("Placement")]
        public Vector2 offset = new Vector2(0f, 0.8f);
        public int widthPixels = 20;
        public int thicknessPixels = 3;

        public Color backColor = new Color(0f, 0f, 0f, 0.85f);
        public Color fillColor = new Color(0.85f, 0.22f, 0.22f, 1f);

        SpriteRenderer _back, _fill;
        Transform _fillT;
        bool _shown;

        void Awake()
        {
            transform.localPosition = offset;

            var backGo = new GameObject("Back");
            backGo.transform.SetParent(transform, false);
            _back = backGo.AddComponent<SpriteRenderer>();
            _back.sortingOrder = 15;
            _back.sprite = SpriteFactory.SolidRect(backColor, widthPixels + 2, thicknessPixels + 2,
                                                   new Color(0f, 0f, 0f, backColor.a));

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(transform, false);
            // Left edge of the fill aligns with the left edge of the backing.
            fillGo.transform.localPosition = new Vector3(-widthPixels / (2f * SpriteFactory.PPU), 0f, 0f);
            _fill = fillGo.AddComponent<SpriteRenderer>();
            _fill.sortingOrder = 16;
            _fill.sprite = SpriteFactory.Bar(fillColor, widthPixels, thicknessPixels);
            _fillT = fillGo.transform;

            SetVisible(false);
        }

        void SetVisible(bool v)
        {
            _shown = v;
            if (_back != null) _back.enabled = v;
            if (_fill != null) _fill.enabled = v;
        }

        /// <summary>Show the bar (once damaged) and set its fill to <paramref name="frac"/> (0-1).</summary>
        public void SetFraction(float frac)
        {
            if (!_shown) SetVisible(true);
            if (_fillT != null)
                _fillT.localScale = new Vector3(Mathf.Clamp01(frac), 1f, 1f);
        }
    }
}
