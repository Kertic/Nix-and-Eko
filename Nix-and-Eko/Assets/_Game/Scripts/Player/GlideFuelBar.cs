using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Player
{
    /// <summary>
    /// A small two-part meter (dark backing + colored fill) that floats under the player and
    /// shows remaining glide fuel. Built from procedural sprites in code, like the rest of the
    /// placeholder art — no art asset to keep in sync. Parented directly under the player root
    /// (not the flipping <see cref="PlayerController.spriteRoot"/>), so it never mirrors with
    /// facing.
    ///
    /// The fill sprite itself is never scaled — scaling a curved/bowed sprite on X would squash
    /// its arc and look wrong at fractional values. Instead a plain rectangular
    /// <see cref="SpriteMask"/> is scaled in from both sides, clipping the fill down to the
    /// remaining fraction while the fill's own pixels stay crisp and undistorted.
    /// </summary>
    public class GlideFuelBar : MonoBehaviour
    {
        public PlayerController player;

        [Header("Placement")]
        public Vector2 offset = new Vector2(0f, -0.6f);
        public int widthPixels = 26;
        [Tooltip("Vertical thickness of the bar strip, in pixels.")]
        public int thicknessPixels = 3;
        [Tooltip("How many pixels the ends bow up above the center (Skyrim-style arc).")]
        public int arcPixels = 3;

        [Header("Color")]
        public Color backColor = new Color(0f, 0f, 0f, 0.8f);
        public Color fillColor = Palette.Blue;
        [Tooltip("Fill tints toward this color as fuel runs low.")]
        public Color lowFillColor = Palette.Red;

        SpriteRenderer _back, _fill;
        SpriteMask _mask;
        Transform _maskT;

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
            _back.sprite = SpriteFactory.CurvedBar(backColor, widthPixels, thicknessPixels, arcPixels,
                                                   new Color(0f, 0f, 0f, backColor.a));

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(transform, false);
            _fill = fillGo.AddComponent<SpriteRenderer>();
            _fill.sortingOrder = 16;
            _fill.sprite = SpriteFactory.CurvedBar(fillColor, widthPixels, thicknessPixels, arcPixels);
            // Only the mask clips the fill — the fill sprite itself always renders at full,
            // undistorted size.
            _fill.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

            // A plain opaque rectangle, tall enough to cover the bar's full bowed height. Being
            // featureless, scaling it on X to reveal/hide the ends causes no visual distortion.
            var maskGo = new GameObject("Mask");
            maskGo.transform.SetParent(transform, false);
            _mask = maskGo.AddComponent<SpriteMask>();
            _mask.sprite = SpriteFactory.SolidRect(Color.white, widthPixels, thicknessPixels + arcPixels + 4, Color.white);
            _mask.isCustomRangeActive = true;
            _mask.backSortingOrder = _fill.sortingOrder;
            _mask.frontSortingOrder = _fill.sortingOrder;
            _maskT = maskGo.transform;
        }

        void LateUpdate()
        {
            if (player == null || player.Config == null) return;

            float duration = Mathf.Max(0.01f, player.Config.glideDuration);
            float frac = Mathf.Clamp01(player.GlideFuel / duration);

            // Stay hidden unless there's a reason to care: actively gliding, or fuel isn't full
            // (grounded refills it instantly, so this also naturally hides on the ground).
            bool show = player.IsGliding || frac < 1f;
            if (_back != null) _back.enabled = show;
            if (_fill != null) _fill.enabled = show;
            if (_mask != null) _mask.enabled = show;
            if (!show) return;

            // Mask (not the fill sprite) shrinks toward the center from both sides as fuel drains.
            _maskT.localScale = new Vector3(frac, 1f, 1f);
            _fill.color = Color.Lerp(lowFillColor, fillColor, frac);
        }
    }
}
