using System.Collections.Generic;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// A League/Dota-style hit flash: on each hit the struck sprite blanks to a white silhouette
    /// that fades away over a fraction of a second. A plain colour tint can't whiten a multi-colour
    /// sprite (the sprite shader multiplies), so this overlays a generated all-white copy of the
    /// current sprite on a child renderer and fades its alpha. White masks are cached per source
    /// sprite so repeated hits don't rebuild textures.
    /// </summary>
    public class HitFlash : MonoBehaviour
    {
        public SpriteRenderer target;
        [Tooltip("Seconds the white flash takes to fade out.")]
        public float duration = 0.16f;

        SpriteRenderer _overlay;
        float _age;
        bool _flashing;

        static readonly Dictionary<Sprite, Sprite> MaskCache = new Dictionary<Sprite, Sprite>();

        void Awake()
        {
            if (target == null) target = GetComponent<SpriteRenderer>();
            if (target == null) { enabled = false; return; }

            var go = new GameObject("HitFlash");
            go.transform.SetParent(target.transform, false);
            _overlay = go.AddComponent<SpriteRenderer>();
            _overlay.sortingOrder = target.sortingOrder + 1;
            _overlay.enabled = false;
        }

        /// <summary>Trigger the white flash on the target's current sprite.</summary>
        public void Flash()
        {
            if (target == null || target.sprite == null || _overlay == null) return;
            _overlay.sprite = WhiteMask(target.sprite);
            _overlay.enabled = true;
            _age = 0f;
            _flashing = true;
        }

        void Update()
        {
            if (!_flashing) return;
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / Mathf.Max(0.01f, duration));
            var c = Color.white;
            c.a = 1f - t;
            _overlay.color = c;
            if (t >= 1f) { _overlay.enabled = false; _flashing = false; }
        }

        static Sprite WhiteMask(Sprite src)
        {
            if (MaskCache.TryGetValue(src, out var cached) && cached != null) return cached;

            var srcTex = src.texture;
            var r = src.rect;
            int x0 = (int)r.x, y0 = (int)r.y, w = (int)r.width, h = (int)r.height;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            Color[] pixels;
            try { pixels = srcTex.GetPixels(x0, y0, w, h); }
            catch { return src; }   // texture not readable — fall back to the source (no crash)

            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = pixels[i].a > 0.5f ? Color.white : Color.clear;
            tex.SetPixels(pixels);
            tex.Apply();

            var mask = Sprite.Create(tex, new Rect(0, 0, w, h), src.pivot / new Vector2(w, h),
                                     src.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            MaskCache[src] = mask;
            return mask;
        }
    }
}
