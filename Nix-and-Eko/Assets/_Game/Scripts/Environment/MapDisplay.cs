using NixAndEko.Level;
using NixAndEko.Player;
using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Environment
{
    /// <summary>
    /// Hollow-Knight-style world map: press Select (PS controller), M or Tab to toggle a full-
    /// screen overlay that draws every level block at scale and pins a pulsing dot on the player's
    /// current position. Runs on IMGUI so it needs no Canvas/prefab wiring — just a
    /// <see cref="LevelData"/> to read the geometry from and a transform to follow.
    ///
    /// The map is not a discovered/fog-of-war map (yet); it always shows the full level. Cost is a
    /// handful of GUI.DrawTexture calls per block per frame the map is open, all sharing four cached
    /// 1x1 textures — cheap enough that the pause-menu-style overlay doesn't hitch on rebuild.
    /// </summary>
    [DefaultExecutionOrder(50)]   // input is sampled at -100; toggle after it this frame
    public class MapDisplay : MonoBehaviour
    {
        [Tooltip("Legacy: draw the map from this Level asset. Leave empty to read every " +
                 "LevelBlockObject placed in the scene instead (the new scene-first flow).")]
        public LevelData level;
        [Tooltip("Scene-first: where the spawn marker goes. Ignored when a Level asset is assigned.")]
        public Vector2 sceneSpawn = new Vector2(0f, 2f);
        [Tooltip("Scene-first: kill-Y line shown at the bottom of the frame.")]
        public float sceneKillY = -80f;
        public PlayerInputReader input;
        [Tooltip("Transform the 'you are here' marker follows — usually the Player root.")]
        public Transform player;
        [Tooltip("Optional second marker (Eko), drawn hollow if provided.")]
        public Transform secondary;

        [Header("Layout")]
        [Range(0.5f, 0.98f)] public float screenFill = 0.9f;
        [Range(0f, 40f)] public float paddingWorldUnits = 8f;

        [Header("Style")]
        public Color backgroundTint = new Color(0.02f, 0.03f, 0.09f, 0.92f);
        public Color frameColor = new Color(0.9f, 0.95f, 1f, 0.7f);
        public Color playerColor = new Color(1f, 0.85f, 0.25f, 1f);

        bool _open;
        Rect _worldBounds;
        // Scene-first snapshot: rects and their types, refreshed each time the map opens so live
        // edits to the scene reflect the next time you look at it. Avoids scanning the scene on
        // every OnGUI frame (of which there are several per Unity frame).
        readonly System.Collections.Generic.List<(Rect rect, BlockType type)> _sceneBlocks = new();
        Vector2 _sceneSpawnCached;
        float _sceneKillYCached;
        // Cached 1-pixel textures keyed by RGB so we can DrawTexture rectangles in any tint without
        // building one texture per block. IMGUI's textures don't survive assembly reload, so lazy-
        // init on first paint rather than in Awake.
        Texture2D _white;

        public bool IsOpen => _open;

        void Awake()
        {
            if (level == null) RefreshSceneSnapshot();
            RecomputeBounds();
        }

        void OnValidate() => RecomputeBounds();

        void Update()
        {
            if (input != null && input.MapPressed)
            {
                _open = !_open;
                // Refresh the scene snapshot each time the map opens (scene-first mode) — the
                // world may have changed since the map was last shown (a gate opened, a
                // breakable wall broke). Cheap: only runs at toggle-on.
                if (_open && level == null) RefreshSceneSnapshot();
                RecomputeBounds();
            }
        }

        void OnGUI()
        {
            if (!_open) return;
            bool sceneMode = level == null;
            int blockCount = sceneMode ? _sceneBlocks.Count : (level.blocks?.Count ?? 0);
            if (blockCount == 0) return;
            EnsureTextures();

            // Screen panel (letterboxed to preserve the level's aspect ratio).
            Rect panel = FitAspect(new Rect(0, 0, Screen.width, Screen.height),
                                   _worldBounds.width, _worldBounds.height, screenFill);

            // Backdrop + frame.
            DrawRect(panel, backgroundTint);
            DrawFrame(panel, frameColor, 2f);

            // Blocks.
            if (sceneMode)
            {
                foreach (var (rect, t) in _sceneBlocks)
                    DrawRect(WorldRectToScreen(rect, panel), ColorFor(t));
            }
            else
            {
                foreach (LevelBlock b in level.blocks)
                {
                    if (b == null) continue;
                    DrawRect(WorldRectToScreen(b.Rect, panel), ColorFor(b.type));
                }
            }

            // Kill-Y line — the actual bottom of the playable world.
            float ky = sceneMode ? _sceneKillYCached : level.killY;
            float killScreenY = WorldYToScreen(ky, panel);
            DrawRect(new Rect(panel.x, killScreenY - 1f, panel.width, 2f),
                     new Color(1f, 0.35f, 0.35f, 0.55f));

            // Spawn.
            Vector2 sp = sceneMode ? _sceneSpawnCached : level.playerSpawn;
            DrawMarker(WorldPointToScreen(sp, panel), 6f,
                       new Color(0.4f, 1f, 0.7f, 0.9f), hollow: true);

            // Secondary (Eko) — hollow so it reads distinct from the player.
            if (secondary != null && secondary.gameObject.activeInHierarchy)
                DrawMarker(WorldPointToScreen(secondary.position, panel), 6f,
                           new Color(0.55f, 0.75f, 1f, 0.95f), hollow: true);

            // Player: a pulsing filled dot so it always reads at a glance.
            if (player != null)
            {
                float pulse = 5f + Mathf.PingPong(Time.unscaledTime * 6f, 3f);
                DrawMarker(WorldPointToScreen(player.position, panel), pulse, playerColor, hollow: false);
            }

            DrawLegend(panel);
        }

        // ------------------------------------------------------------------ helpers
        void RecomputeBounds()
        {
            bool sceneMode = level == null;
            int count = sceneMode ? _sceneBlocks.Count : (level.blocks?.Count ?? 0);
            if (count == 0)
            {
                _worldBounds = new Rect(-10, -10, 20, 20);
                return;
            }

            float xmin, xmax, ymin, ymax;
            if (sceneMode)
            {
                Rect r0 = _sceneBlocks[0].rect;
                xmin = r0.xMin; xmax = r0.xMax; ymin = r0.yMin; ymax = r0.yMax;
                foreach (var b in _sceneBlocks)
                {
                    xmin = Mathf.Min(xmin, b.rect.xMin); xmax = Mathf.Max(xmax, b.rect.xMax);
                    ymin = Mathf.Min(ymin, b.rect.yMin); ymax = Mathf.Max(ymax, b.rect.yMax);
                }
                ymin = Mathf.Min(ymin, _sceneKillYCached);
            }
            else
            {
                LevelBlock first = level.blocks[0];
                xmin = first.position.x - first.size.x * 0.5f;
                xmax = first.position.x + first.size.x * 0.5f;
                ymin = first.position.y - first.size.y * 0.5f;
                ymax = first.position.y + first.size.y * 0.5f;
                foreach (LevelBlock b in level.blocks)
                {
                    xmin = Mathf.Min(xmin, b.position.x - b.size.x * 0.5f);
                    xmax = Mathf.Max(xmax, b.position.x + b.size.x * 0.5f);
                    ymin = Mathf.Min(ymin, b.position.y - b.size.y * 0.5f);
                    ymax = Mathf.Max(ymax, b.position.y + b.size.y * 0.5f);
                }
                ymin = Mathf.Min(ymin, level.killY);
            }

            xmin -= paddingWorldUnits; xmax += paddingWorldUnits;
            ymin -= paddingWorldUnits; ymax += paddingWorldUnits;
            _worldBounds = new Rect(xmin, ymin, xmax - xmin, ymax - ymin);
        }

        /// <summary>Snapshot every <see cref="LevelBlockObject"/> in the active scene. Called on
        /// map-open (and again when the map is closed and reopened) so live scene edits show up
        /// without a per-frame scan of the whole hierarchy.</summary>
        void RefreshSceneSnapshot()
        {
            _sceneBlocks.Clear();
            var all = FindObjectsByType<LevelBlockObject>(FindObjectsSortMode.None);
            foreach (var b in all)
                if (b != null) _sceneBlocks.Add((b.WorldRect, b.type));
            _sceneSpawnCached = sceneSpawn;
            _sceneKillYCached = sceneKillY;
        }

        static Rect FitAspect(Rect container, float w, float h, float fill)
        {
            float availW = container.width * fill;
            float availH = container.height * fill;
            float aspect = w / Mathf.Max(0.0001f, h);
            float rectW = availW, rectH = availW / aspect;
            if (rectH > availH) { rectH = availH; rectW = availH * aspect; }
            return new Rect(container.x + (container.width  - rectW) * 0.5f,
                            container.y + (container.height - rectH) * 0.5f,
                            rectW, rectH);
        }

        /// <summary>World-space (Y up) rect to GUI-space (Y down) pixels within <paramref name="panel"/>.</summary>
        Rect WorldRectToScreen(Rect world, Rect panel)
        {
            float sx = panel.width  / _worldBounds.width;
            float sy = panel.height / _worldBounds.height;
            float x = panel.x + (world.xMin - _worldBounds.xMin) * sx;
            float w = world.width * sx;
            // Flip Y: world +y is up, GUI +y is down. Use yMax so the top of the world block
            // becomes the top pixel of the screen rect.
            float y = panel.y + (_worldBounds.yMax - world.yMax) * sy;
            float h = world.height * sy;
            return new Rect(x, y, Mathf.Max(1f, w), Mathf.Max(1f, h));
        }

        Vector2 WorldPointToScreen(Vector2 world, Rect panel)
        {
            float sx = panel.width  / _worldBounds.width;
            float sy = panel.height / _worldBounds.height;
            return new Vector2(panel.x + (world.x - _worldBounds.xMin) * sx,
                               panel.y + (_worldBounds.yMax - world.y) * sy);
        }

        float WorldYToScreen(float y, Rect panel)
        {
            float sy = panel.height / _worldBounds.height;
            return panel.y + (_worldBounds.yMax - y) * sy;
        }

        void EnsureTextures()
        {
            if (_white != null) return;
            _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _white.SetPixel(0, 0, Color.white);
            _white.filterMode = FilterMode.Point;
            _white.Apply();
        }

        void DrawRect(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
        }

        void DrawFrame(Rect r, Color c, float t)
        {
            DrawRect(new Rect(r.x, r.y, r.width, t), c);
            DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            DrawRect(new Rect(r.x, r.y, t, r.height), c);
            DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        void DrawMarker(Vector2 centre, float radius, Color c, bool hollow)
        {
            Rect r = new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f);
            if (!hollow) { DrawRect(r, c); return; }
            // Cheap hollow ring: a solid rect masked by the background colour in the middle.
            DrawRect(r, c);
            Rect inner = new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f);
            if (inner.width > 0f && inner.height > 0f) DrawRect(inner, backgroundTint);
        }

        void DrawLegend(Rect panel)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(11, Mathf.RoundToInt(panel.height * 0.022f)),
                normal = { textColor = new Color(0.9f, 0.95f, 1f, 0.85f) },
            };
            string help = "MAP — press Select / M / Tab to close";
            GUI.Label(new Rect(panel.x + 12f, panel.y + 8f, panel.width, 24f), help, style);

            string coord = player != null
                ? $"({player.position.x:0.0}, {player.position.y:0.0})"
                : "";
            var right = new GUIStyle(style) { alignment = TextAnchor.UpperRight };
            GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width - 12f, 24f), coord, right);
        }

        static Color ColorFor(BlockType t) => t switch
        {
            BlockType.OneWay => Palette.White,
            BlockType.MovingPlatform => Palette.Moving,
            BlockType.Hazard => Palette.Hazard,
            BlockType.Checkpoint => Palette.Checkpoint,
            BlockType.Gate => Palette.Gate,
            BlockType.TargetSwitch => Palette.Switch,
            BlockType.BreakableWall => Palette.Breakable,
            BlockType.EnemyWalker => new Color(0.75f, 0.5f, 0.9f),
            BlockType.EnemySlammer => new Color(1f, 0.5f, 0.5f),
            BlockType.Sign => new Color(0f, 0f, 0f, 0f),   // signs are text-only; hide on the map
            BlockType.Door => new Color(0.35f, 0.75f, 1f, 1f),   // Eko-blue portal, matches the in-world tint
            _ => new Color(0.55f, 0.42f, 0.30f),   // ground: warm earth, readable on the dark panel
        };
    }
}
