using UnityEngine;
using NixAndEko.Util;

namespace NixAndEko.Environment
{
    /// <summary>
    /// Faerie-forest backdrop and parallax scenery, all built in code. Spawns:
    ///
    /// <list type="bullet">
    /// <item>A vertical sky gradient that always fills the camera view — deep midnight indigo at
    /// top fading to a warmer violet toward the bottom, with pinprick stars scattered through.</item>
    /// <item>Two silhouette tree layers at different depths, scrolling at different parallax
    /// speeds so distant trees drift by slowly and closer ones sweep past faster than the level.</item>
    /// <item>A field of drifting bioluminescent motes (fireflies) that float and blink in the mid
    /// distance, so the world has visible life beyond the platforms and enemies.</item>
    /// </list>
    ///
    /// Everything sits behind the level's sprites via sorting order. The camera background
    /// (<see cref="Camera.backgroundColor"/>) is expected to be a dark blue already — the sky
    /// layer covers most of the frame anyway, but the bg fills whatever slivers show through at
    /// odd aspect ratios.
    /// </summary>
    public class Scenery : MonoBehaviour
    {
        public Camera cam;
        public Transform followTarget;   // usually the camera or the player

        [Header("Sky")]
        [Tooltip("Colour at the top of the sky (near-black midnight).")]
        public Color skyTop = new Color(0.02f, 0.03f, 0.12f);
        [Tooltip("Colour at the horizon (rich indigo with a hint of violet).")]
        public Color skyBottom = new Color(0.10f, 0.09f, 0.28f);
        [Tooltip("Colour of the pinprick stars scattered through the gradient.")]
        public Color starColor = new Color(0.85f, 0.92f, 1f, 0.9f);
        [Tooltip("Stars per 128×128 tile, roughly.")]
        [Range(0, 200)] public int starDensity = 55;

        [Header("Parallax trees")]
        [Tooltip("Silhouette colour of the far tree layer — deepest plum, low contrast.")]
        public Color farTreeColor = new Color(0.10f, 0.06f, 0.22f);
        [Tooltip("Silhouette colour of the near tree layer — a hair lighter to peek forward.")]
        public Color nearTreeColor = new Color(0.16f, 0.10f, 0.32f);
        [Tooltip("Glowing bioluminescent dot colour scattered through the trees.")]
        public Color treeGlow = new Color(0.35f, 0.88f, 1f, 1f);

        [Tooltip("Parallax factor for the far layer (0 = static, 1 = same speed as world).")]
        [Range(0f, 1f)] public float farParallax = 0.12f;
        [Tooltip("Parallax factor for the near layer.")]
        [Range(0f, 1f)] public float nearParallax = 0.35f;

        [Header("Fireflies")]
        [Tooltip("Number of drifting motes spawned in the play area.")]
        public int fireflyCount = 40;
        [Tooltip("Half-width of the box around the camera the motes drift within, world units.")]
        public float fireflyHalfWidth = 28f;
        [Tooltip("Half-height of the box around the camera the motes drift within, world units.")]
        public float fireflyHalfHeight = 15f;

        SpriteRenderer _sky;
        Transform _farLayer;
        Transform _nearLayer;
        Vector3 _startFollow;

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (followTarget == null && cam != null) followTarget = cam.transform;
        }

        void Start()
        {
            if (cam == null || followTarget == null) { enabled = false; return; }
            _startFollow = followTarget.position;

            BuildSky();
            _farLayer = BuildTreeLayer("Trees_Far", farTreeColor, sortingOrder: -80,
                                       tileWidth: 32f, treeCount: 8, treeMinHeight: 6f, treeMaxHeight: 12f);
            _nearLayer = BuildTreeLayer("Trees_Near", nearTreeColor, sortingOrder: -60,
                                        tileWidth: 24f, treeCount: 6, treeMinHeight: 9f, treeMaxHeight: 18f);
            BuildFireflies();
        }

        void LateUpdate()
        {
            if (cam == null) return;

            // Sky glues to the camera so it always fills the frame regardless of pan.
            if (_sky != null)
            {
                Vector3 p = cam.transform.position;
                _sky.transform.position = new Vector3(p.x, p.y, 20f);   // behind everything
            }

            // Tree layers scroll at a fraction of the world speed for parallax. Base offset is the
            // starting follow point so a level built around (0,0) still tiles cleanly.
            if (followTarget != null)
            {
                Vector3 delta = followTarget.position - _startFollow;
                if (_farLayer != null)
                    _farLayer.position = new Vector3(delta.x * farParallax, delta.y * farParallax * 0.4f, 15f);
                if (_nearLayer != null)
                    _nearLayer.position = new Vector3(delta.x * nearParallax, delta.y * nearParallax * 0.4f, 10f);
            }
        }

        // ------------------------------------------------------------------ sky
        void BuildSky()
        {
            var go = new GameObject("Scenery_Sky");
            go.transform.SetParent(transform, false);
            _sky = go.AddComponent<SpriteRenderer>();
            _sky.sortingOrder = -100;
            _sky.sprite = BuildSkyGradientSprite(256, 256);
            _sky.drawMode = SpriteDrawMode.Sliced;
            // Big enough to blanket any reasonable orthographic view; the sky is redrawn each frame
            // to the camera so this only has to be larger than the view frustum.
            _sky.size = new Vector2(160f, 90f);
        }

        Sprite BuildSkyGradientSprite(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1);   // 0 bottom, 1 top
                Color c = Color.Lerp(skyBottom, skyTop, t);
                for (int x = 0; x < w; x++) pixels[y * w + x] = c;
            }

            // Pinprick stars — small alpha-blended dots painted straight into the gradient. A
            // deterministic seed so the sky matches from run to run.
            var rng = new System.Random(1337);
            int stars = Mathf.RoundToInt(starDensity * (w * h) / (128f * 128f));
            for (int i = 0; i < stars; i++)
            {
                int sx = rng.Next(w);
                int sy = rng.Next(h / 2, h);   // stars only in the top half — bottom is horizon
                float bright = (float)(0.4 + rng.NextDouble() * 0.6);
                Color s = starColor; s.a = bright;
                Color under = pixels[sy * w + sx];
                pixels[sy * w + sx] = Color.Lerp(under, s, s.a);
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f,
                                 0, SpriteMeshType.FullRect);
        }

        // ------------------------------------------------------------------ trees
        /// <summary>Build one horizontally-tiling row of silhouette trees, generated as a single
        /// wide sprite that repeats. The row is duplicated a few times either side of centre so
        /// scrolling never runs out of forest.</summary>
        Transform BuildTreeLayer(string name, Color trunk, int sortingOrder, float tileWidth,
                                 int treeCount, float treeMinHeight, float treeMaxHeight)
        {
            var root = new GameObject(name).transform;
            root.SetParent(transform, false);

            Sprite tile = BuildTreeTileSprite(trunk, treeCount, treeMinHeight, treeMaxHeight, tileWidth);

            const int tileRepeats = 9;   // 9 copies side by side = huge horizontal reach
            float w = tile.rect.width / tile.pixelsPerUnit;
            float h = tile.rect.height / tile.pixelsPerUnit;
            float startY = -h * 0.15f;   // sink the trees so their base is roughly at the horizon

            for (int i = 0; i < tileRepeats; i++)
            {
                var go = new GameObject($"{name}_{i}");
                go.transform.SetParent(root, false);
                float xOff = (i - tileRepeats / 2) * w;
                go.transform.localPosition = new Vector3(xOff, startY, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = tile;
                sr.sortingOrder = sortingOrder;
            }
            return root;
        }

        Sprite BuildTreeTileSprite(Color trunk, int treeCount, float minHeight, float maxHeight, float worldWidth)
        {
            int ppu = 16;
            int w = Mathf.RoundToInt(worldWidth * ppu);
            int h = Mathf.RoundToInt(maxHeight * ppu) + 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

            // Deterministic layout so re-running the game doesn't reshuffle the forest each launch.
            var rng = new System.Random(9001 + Mathf.RoundToInt(trunk.r * 100f + trunk.g * 200f + trunk.b * 300f));

            for (int i = 0; i < treeCount; i++)
            {
                int centerX = Mathf.RoundToInt((i + 0.5f) / treeCount * w + (float)(rng.NextDouble() - 0.5) * w / treeCount * 0.7f);
                float treeH = Mathf.Lerp(minHeight, maxHeight, (float)rng.NextDouble()) * ppu;
                int trunkHalfW = Mathf.Max(1, Mathf.RoundToInt(treeH * 0.05f));   // slim trunk

                // Trunk: a rectangle that narrows toward the top a hair, tallest in the middle.
                for (int y = 0; y < treeH * 0.55f; y++)
                {
                    if (y >= h) break;
                    for (int x = -trunkHalfW; x <= trunkHalfW; x++)
                    {
                        int px = centerX + x;
                        if (px < 0 || px >= w) continue;
                        pixels[y * w + px] = trunk;
                    }
                }

                // Crown: a stack of tapering triangles giving a rough coniferous silhouette.
                int layers = 4 + rng.Next(2);
                float crownTop = treeH;
                float crownBase = treeH * 0.35f;
                for (int layer = 0; layer < layers; layer++)
                {
                    float lt = layer / (float)Mathf.Max(1, layers - 1);   // 0 bottom → 1 top
                    float yBottom = Mathf.Lerp(crownBase, crownTop - 6f, lt);
                    float yTop = Mathf.Lerp(crownBase + (crownTop - crownBase) / layers, crownTop, lt);
                    float halfWide = Mathf.Lerp(treeH * 0.22f, treeH * 0.07f, lt);

                    for (int y = Mathf.RoundToInt(yBottom); y < Mathf.RoundToInt(yTop); y++)
                    {
                        if (y < 0 || y >= h) continue;
                        float tt = (y - yBottom) / Mathf.Max(1f, yTop - yBottom);
                        int half = Mathf.RoundToInt(Mathf.Lerp(halfWide, halfWide * 0.15f, tt));
                        for (int x = -half; x <= half; x++)
                        {
                            int px = centerX + x;
                            if (px < 0 || px >= w) continue;
                            pixels[y * w + px] = trunk;
                        }
                    }
                }

                // Bioluminescent glow dots scattered through the crown — the Ardenweald signature.
                int dots = 4 + rng.Next(6);
                for (int d = 0; d < dots; d++)
                {
                    int dx = centerX + (int)((rng.NextDouble() - 0.5) * treeH * 0.35);
                    int dy = (int)Mathf.Lerp(crownBase, crownTop, (float)rng.NextDouble());
                    if (dx < 0 || dx >= w || dy < 0 || dy >= h) continue;
                    if (pixels[dy * w + dx].a < 0.1f) continue;   // only glow where the tree exists
                    pixels[dy * w + dx] = treeGlow;
                    // A soft 1px halo — additive over whatever's already there.
                    for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int nx = dx + ox, ny = dy + oy;
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                        if (ox == 0 && oy == 0) continue;
                        var under = pixels[ny * w + nx];
                        if (under.a < 0.05f) continue;
                        pixels[ny * w + nx] = Color.Lerp(under, treeGlow, 0.35f);
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), ppu,
                                 0, SpriteMeshType.FullRect);
        }

        // ------------------------------------------------------------------ fireflies
        void BuildFireflies()
        {
            var root = new GameObject("Fireflies").transform;
            root.SetParent(transform, false);

            for (int i = 0; i < fireflyCount; i++)
            {
                var go = new GameObject("Firefly");
                go.transform.SetParent(root, false);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = SpriteFactory.SolidRect(Color.white, 3, 3, Color.white);
                sr.sortingOrder = -40;
                sr.color = Color.Lerp(treeGlow, new Color(0.7f, 0.5f, 1f, 1f), Random.value * 0.5f);

                var f = go.AddComponent<Firefly>();
                f.target = followTarget;
                f.halfWidth = fireflyHalfWidth;
                f.halfHeight = fireflyHalfHeight;
                f.Randomize();
            }
        }
    }

    /// <summary>A single drifting glow mote. Loops within a box around the camera so wandering
    /// off screen recycles it to a fresh spawn on the opposite side — the swarm feels persistent
    /// without needing to grow with the level.</summary>
    public class Firefly : MonoBehaviour
    {
        public Transform target;
        public float halfWidth = 20f;
        public float halfHeight = 12f;
        public float driftSpeed = 0.6f;
        public float blinkSpeed = 1.4f;

        Vector2 _velocity;
        Color _baseColor;
        SpriteRenderer _sr;
        float _phase;

        public void Randomize()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (_sr != null) _baseColor = _sr.color;
            transform.localPosition = new Vector3(
                Random.Range(-halfWidth, halfWidth),
                Random.Range(-halfHeight, halfHeight),
                5f);
            _velocity = Random.insideUnitCircle * driftSpeed;
            _phase = Random.value * Mathf.PI * 2f;
            transform.localScale = Vector3.one * Random.Range(0.4f, 1.1f);
        }

        void Update()
        {
            if (target == null) return;

            float dt = Time.deltaTime;
            _phase += dt * blinkSpeed;

            // Slow drift with a gentle noise-y curve; occasionally re-roll direction so no mote
            // travels the whole play area in one straight line.
            _velocity += (Vector2)(Random.insideUnitSphere * dt * 0.3f);
            _velocity = Vector2.ClampMagnitude(_velocity, driftSpeed);
            Vector3 p = transform.position + (Vector3)(_velocity * dt);

            // Wrap around the camera-centred box so the swarm follows the player through the level.
            Vector3 t = target.position;
            if (p.x < t.x - halfWidth) p.x = t.x + halfWidth;
            if (p.x > t.x + halfWidth) p.x = t.x - halfWidth;
            if (p.y < t.y - halfHeight) p.y = t.y + halfHeight;
            if (p.y > t.y + halfHeight) p.y = t.y - halfHeight;
            transform.position = p;

            if (_sr != null)
            {
                Color c = _baseColor;
                c.a = 0.4f + 0.6f * (0.5f + 0.5f * Mathf.Sin(_phase));
                _sr.color = c;
            }
        }
    }
}
