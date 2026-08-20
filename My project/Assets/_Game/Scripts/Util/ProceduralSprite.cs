using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// Attaches to a SpriteRenderer and builds a placeholder sprite from parameters, so levels
    /// can be blocked out with crisp retro visuals without importing any art. Regenerates in the
    /// editor (ExecuteAlways) and at runtime, so nothing has to be saved as an asset.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    public class ProceduralSprite : MonoBehaviour
    {
        public enum Shape { Tile, Target, Arrow, Circle }

        public Shape shape = Shape.Tile;
        public Color primary = new Color(0.35f, 0.55f, 0.85f);
        public Color secondary = new Color(0.15f, 0.25f, 0.45f);
        public int pixelsX = 16;
        public int pixelsY = 16;

        [Range(0.05f, 1f)]
        [Tooltip("Stroke width of the Circle shape, as a fraction of its radius.")]
        public float circleThickness = 0.22f;

        [Tooltip("If both > 0, draws the tile in Tiled mode at this world size (repeats the sprite).")]
        public Vector2 tiledSize = Vector2.zero;

        SpriteRenderer _sr;
        Texture2D _owned;

        void OnEnable() { Rebuild(); }
        void OnValidate() { if (isActiveAndEnabled) Rebuild(); }

        public void Rebuild()
        {
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) return;

            Sprite sprite = shape switch
            {
                Shape.Target => SpriteFactory.Target(primary, secondary, Mathf.Max(4, pixelsX)),
                Shape.Circle => SpriteFactory.Circle(primary, Mathf.Max(4, pixelsX), circleThickness),
                Shape.Arrow => SpriteFactory.Arrow(primary, secondary, Mathf.Max(6, pixelsX), Mathf.Max(3, pixelsY)),
                _ => SpriteFactory.SolidRect(primary, Mathf.Max(2, pixelsX), Mathf.Max(2, pixelsY), secondary),
            };

            // Release the previously generated texture to avoid piling up leaks on rebuild.
            if (_owned != null)
            {
                if (Application.isPlaying) Destroy(_owned);
                else DestroyImmediate(_owned);
            }
            _owned = sprite.texture;

            _sr.sprite = sprite;

            if (shape == Shape.Tile && tiledSize.x > 0f && tiledSize.y > 0f)
            {
                _sr.drawMode = SpriteDrawMode.Tiled;
                _sr.size = tiledSize;
            }
            else
            {
                _sr.drawMode = SpriteDrawMode.Simple;
            }
        }
    }
}
