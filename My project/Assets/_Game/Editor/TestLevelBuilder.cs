using System.Reflection;
using NixAndEko.Combat;
using NixAndEko.Environment;
using NixAndEko.Player;
using NixAndEko.Util;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NixAndEko.EditorTools
{
    /// <summary>
    /// One-click assembly of a wired, playable test level: player (state-machine controller + bow),
    /// plus every building block (ground, one-way & moving platforms, hazards, checkpoint,
    /// shootable switch + gate, breakable wall). Everything is generated in code so there are no
    /// GUID-sensitive prefab/scene YAML files to maintain and no art to import.
    /// </summary>
    public static class TestLevelBuilder
    {
        const string RootName = "— Nix&Eko Test Level —";
        const string InputAssetPath = "Assets/InputSystem_Actions.inputactions";
        const string ConfigPath = "Assets/_Game/Data/PlayerConfig.asset";

        // --- PICO-8 palette (Celeste-classic style) ---
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        static readonly Color P8Black = Hex("#000000");
        static readonly Color P8DarkBlue = Hex("#1D2B53");
        static readonly Color P8Brown = Hex("#AB5236");
        static readonly Color P8DarkGrey = Hex("#5F574F");
        static readonly Color P8LightGrey = Hex("#C2C3C7");
        static readonly Color P8White = Hex("#FFF1E8");
        static readonly Color P8Red = Hex("#FF004D");
        static readonly Color P8Orange = Hex("#FFA300");
        static readonly Color P8Green = Hex("#00E436");
        static readonly Color P8Blue = Hex("#29ADFF");

        // Role -> palette mapping.
        static readonly Color ColGround = P8Brown;
        static readonly Color ColGroundEdge = P8DarkBlue;
        static readonly Color ColOneWay = P8White;
        static readonly Color ColMoving = P8Blue;
        static readonly Color ColHazard = P8Red;
        static readonly Color ColGate = P8LightGrey;
        static readonly Color ColBreak = P8Orange;
        static readonly Color ColPlayer = P8Red;
        static readonly Color ColCheckpoint = P8Green;

        [MenuItem("Tools/Nix & Eko/Build Test Level", priority = 0)]
        public static void BuildTestLevel()
        {
            int groundLayer = EnsureLayer("Ground");
            PlayerConfig config = LoadOrCreateConfig();
            InputActionAsset inputAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            if (inputAsset == null)
                Debug.LogWarning($"[Builder] Input asset not found at {InputAssetPath}. Assign one on the Player's PlayerInputReader.");

            // Fresh root.
            var existing = GameObject.Find(RootName);
            if (existing != null) Object.DestroyImmediate(existing);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Test Level");

            ConfigureCamera();

            // ---- Terrain ----
            Ground(root, "Floor", new Vector2(0f, -4f), new Vector2(48f, 1.5f), groundLayer);
            Ground(root, "Wall-Left", new Vector2(-16f, 0f), new Vector2(1.5f, 9f), groundLayer);
            Ground(root, "Wall-RightTall", new Vector2(16f, 2f), new Vector2(1.5f, 13f), groundLayer);
            Ground(root, "Ledge-A", new Vector2(-9f, -1.5f), new Vector2(4f, 1f), groundLayer);
            Ground(root, "Ledge-B", new Vector2(9f, 0f), new Vector2(4f, 1f), groundLayer);
            Ground(root, "HighShelf", new Vector2(0f, 5f), new Vector2(5f, 1f), groundLayer);

            // Wall-slide shaft (two tall walls to cling to and slide down).
            Ground(root, "Shaft-L", new Vector2(-4.5f, 2.5f), new Vector2(1f, 6f), groundLayer);
            Ground(root, "Shaft-R", new Vector2(-1.5f, 2.5f), new Vector2(1f, 6f), groundLayer);

            // ---- Building blocks ----
            OneWay(root, "OneWayPlatform", new Vector2(-9f, 1.5f), new Vector2(4f, 0.4f), groundLayer);
            MovingH(root, "MovingPlatform", new Vector2(3f, 2.5f), new Vector2(3f, 0.5f), new Vector2(5f, 0f), groundLayer);
            Hazard(root, "Spikes", new Vector2(0f, -2.9f), new Vector2(3f, 0.6f));
            Checkpoint(root, "Checkpoint", new Vector2(9f, 1f));

            // Shoot-the-switch-to-open-the-gate puzzle.
            Gate gate = Gate(root, "Gate", new Vector2(13f, -2f), new Vector2(1f, 4f), groundLayer);
            Switch(root, "TargetSwitch", new Vector2(9f, 6.5f), gate);

            // Breakable wall hiding a path (needs a charged shot).
            Breakable(root, "BreakableWall", new Vector2(13.5f, 3.5f), new Vector2(1.2f, 3f), groundLayer);

            // ---- Player ----
            Arrow arrowTemplate = BuildArrowTemplate(root, groundLayer);
            BuildPlayer(root, config, inputAsset, groundLayer, arrowTemplate, new Vector2(-9f, 0f));

            Selection.activeGameObject = root;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[Builder] Test level built. Press Play. Controls: A/D move, Space jump (hold=higher), " +
                      "Ctrl crouch (Ctrl+Space to drop through the white platform), cling to the shaft " +
                      "walls to wall-slide (no wall-jump). Hold LMB to draw, then drag away from the click point to " +
                      "aim in one of 8 directions — the " +
                      "reticle and arc preview show aim & charge — release to fire.");
        }

        [MenuItem("Tools/Nix & Eko/Create Player Config", priority = 20)]
        public static void CreatePlayerConfigMenu() => LoadOrCreateConfig();

        // ================================================================= Player
        static void BuildPlayer(GameObject root, PlayerConfig config, InputActionAsset input,
                                int groundLayer, Arrow arrowTemplate, Vector2 pos)
        {
            var go = new GameObject("Player");
            go.transform.SetParent(root.transform);
            go.transform.position = pos;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;

            // Square body (Celeste-classic style), not a tall capsule.
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.9f, 0.9f);

            // Sprite child (flipped for facing).
            var spriteGo = new GameObject("Sprite");
            spriteGo.transform.SetParent(go.transform);
            spriteGo.transform.localPosition = Vector3.zero;
            var sr = spriteGo.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 10;
            var ps = spriteGo.AddComponent<ProceduralSprite>();
            ps.shape = ProceduralSprite.Shape.Tile;
            ps.primary = ColPlayer;
            ps.secondary = ColPlayer * 0.6f;
            ps.pixelsX = 16; ps.pixelsY = 16;   // square
            ps.Rebuild();

            var controller = go.AddComponent<PlayerController>();
            controller.config = config;
            controller.spriteRoot = spriteGo.transform;
            controller.groundMask = 1 << groundLayer;

            var reader = go.AddComponent<PlayerInputReader>();
            reader.actions = input;
            controller.input = reader;

            var health = go.AddComponent<Health>();
            health.player = controller;
            health.config = config;

            // Bow child + muzzle.
            var bowGo = new GameObject("Bow");
            bowGo.transform.SetParent(go.transform);
            bowGo.transform.localPosition = Vector3.zero;
            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(bowGo.transform);
            muzzle.transform.localPosition = Vector3.zero; // aim originates from the player's center
            var bow = bowGo.AddComponent<Bow>();
            bow.player = controller;
            bow.input = reader;
            bow.muzzle = muzzle.transform;
            bow.arrowPrefab = arrowTemplate;
            bow.eightDirectional = true;

            // Draw indicator: an arrow-shaped reticle that appears while drawing and
            // points along the (8-way snapped) aim, tinting from white to red with charge.
            var indicatorGo = new GameObject("AimIndicator");
            indicatorGo.transform.SetParent(bowGo.transform);
            indicatorGo.transform.localPosition = Vector3.zero;
            var indSr = indicatorGo.AddComponent<SpriteRenderer>();
            indSr.sortingOrder = 20;
            var indPs = indicatorGo.AddComponent<ProceduralSprite>();
            indPs.shape = ProceduralSprite.Shape.Arrow;
            indPs.primary = P8White;
            indPs.secondary = P8LightGrey;
            indPs.pixelsX = 12; indPs.pixelsY = 5;
            indPs.Rebuild();
            indicatorGo.SetActive(false); // hidden until drawing
            bow.aimIndicator = indicatorGo.transform;
            bow.aimIndicatorRenderer = indSr;

            // Trajectory preview: a dotted arc from the player's center along the predicted path.
            var trajGo = new GameObject("Trajectory");
            trajGo.transform.SetParent(bowGo.transform);
            trajGo.transform.localPosition = Vector3.zero;
            var lr = trajGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.12f;
            lr.numCapVertices = 2;
            lr.textureMode = LineTextureMode.Tile;
            lr.alignment = LineAlignment.View;
            lr.sortingOrder = 19;
            lr.positionCount = 0;
            lr.startColor = P8White;
            lr.endColor = new Color(1f, 1f, 1f, 0f);
            trajGo.AddComponent<ProceduralLine>(); // keeps the material valid across save/reload
            bow.trajectory = lr;

            // Drag anchor: a small ring marking the spot the drag started.
            var anchorGo = new GameObject("DragAnchor");
            anchorGo.transform.SetParent(bowGo.transform);
            anchorGo.transform.localScale = Vector3.one * 0.6f;
            var anchorSr = anchorGo.AddComponent<SpriteRenderer>();
            anchorSr.sortingOrder = 21;
            anchorSr.color = new Color(1f, 1f, 1f, 0.75f);
            var anchorPs = anchorGo.AddComponent<ProceduralSprite>();
            anchorPs.shape = ProceduralSprite.Shape.Circle;
            anchorPs.primary = P8White;
            anchorPs.circleThickness = 0.25f;
            anchorPs.pixelsX = 16; anchorPs.pixelsY = 16;
            anchorPs.Rebuild();
            anchorGo.SetActive(false);
            bow.dragAnchorIndicator = anchorGo.transform;

            // Debug HUD.
            var hudGo = new GameObject("DebugHUD");
            hudGo.transform.SetParent(root.transform);
            var hud = hudGo.AddComponent<DebugHud>();
            hud.player = controller;
            hud.health = health;
            hud.bow = bow;
        }

        static Arrow BuildArrowTemplate(GameObject root, int groundLayer)
        {
            var go = new GameObject("ArrowTemplate");
            go.transform.SetParent(root.transform);
            go.SetActive(false); // template only; Bow instantiates copies

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 9;
            var ps = go.AddComponent<ProceduralSprite>();
            ps.shape = ProceduralSprite.Shape.Arrow;
            ps.primary = new Color(0.9f, 0.85f, 0.6f);
            ps.secondary = new Color(0.7f, 0.7f, 0.7f);
            ps.pixelsX = 16; ps.pixelsY = 6;

            go.AddComponent<Rigidbody2D>();
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(1.0f, 0.35f);

            var arrow = go.AddComponent<Arrow>();
            return arrow;
        }

        // ================================================================= Blocks
        static GameObject Ground(GameObject root, string name, Vector2 pos, Vector2 size, int layer)
        {
            var go = NewBlock(root, name, pos, size, ColGround, ColGroundEdge, layer);
            return go;
        }

        static void OneWay(GameObject root, string name, Vector2 pos, Vector2 size, int layer)
        {
            var go = NewBlock(root, name, pos, size, ColOneWay, ColOneWay * 0.6f, layer);
            var col = go.GetComponent<BoxCollider2D>();
            col.usedByEffector = true;
            var eff = go.AddComponent<PlatformEffector2D>();
            eff.useOneWay = true;
            eff.surfaceArc = 140f;
            var owp = go.AddComponent<OneWayPlatform>();
            owp.dropSeconds = 0.35f;
        }

        static void MovingH(GameObject root, string name, Vector2 pos, Vector2 size, Vector2 offset, int layer)
        {
            var go = NewBlock(root, name, pos, size, ColMoving, ColMoving * 0.6f, layer);
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            var mp = go.AddComponent<MovingPlatform>();
            mp.localOffset = offset;
            mp.speed = 3f;
        }

        static void Hazard(GameObject root, string name, Vector2 pos, Vector2 size)
        {
            var go = NewBlock(root, name, pos, size, ColHazard, ColHazard * 0.6f, 0);
            var col = go.GetComponent<BoxCollider2D>();
            col.isTrigger = true;
            var hz = go.AddComponent<Hazard>();
            hz.damage = 1;
        }

        static void Checkpoint(GameObject root, string name, Vector2 pos)
        {
            var go = NewBlock(root, name, pos, new Vector2(1f, 2f), ColCheckpoint, ColCheckpoint * 0.6f, 0);
            var col = go.GetComponent<BoxCollider2D>();
            col.isTrigger = true;
            go.AddComponent<Checkpoint>();
        }

        static Gate Gate(GameObject root, string name, Vector2 pos, Vector2 size, int layer)
        {
            var go = NewBlock(root, name, pos, size, ColGate, ColGate * 0.6f, layer);
            var gate = go.AddComponent<Gate>();
            gate.openOffset = new Vector3(0f, size.y + 0.5f, 0f);
            return gate;
        }

        static void Switch(GameObject root, string name, Vector2 pos, Gate gate)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform);
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 5;
            var ps = go.AddComponent<ProceduralSprite>();
            ps.shape = ProceduralSprite.Shape.Target;
            ps.primary = new Color(0.7f, 0.3f, 0.3f);
            ps.secondary = new Color(0.95f, 0.9f, 0.8f);
            ps.pixelsX = 16; ps.pixelsY = 16;
            ps.Rebuild();

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;
            col.isTrigger = true;

            var sw = go.AddComponent<TargetSwitch>();
            sw.toggle = true;
            sw.gates = new[] { gate };
            sw.targetRenderer = sr;
        }

        static void Breakable(GameObject root, string name, Vector2 pos, Vector2 size, int layer)
        {
            var go = NewBlock(root, name, pos, size, ColBreak, ColBreak * 0.6f, layer);
            var bw = go.AddComponent<BreakableWall>();
            bw.hitsToBreak = 1;
            bw.minCharge = 0.5f; // needs a decent draw
        }

        // ================================================================= Helpers
        static GameObject NewBlock(GameObject root, string name, Vector2 pos, Vector2 size,
                                   Color fill, Color edge, int layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform);
            go.transform.position = pos;
            if (layer > 0) go.layer = layer;

            var sr = go.AddComponent<SpriteRenderer>();
            var ps = go.AddComponent<ProceduralSprite>();
            ps.shape = ProceduralSprite.Shape.Tile;
            ps.primary = fill;
            ps.secondary = edge;
            ps.pixelsX = 16; ps.pixelsY = 16;
            ps.tiledSize = size;
            ps.Rebuild();

            var col = go.AddComponent<BoxCollider2D>();
            col.size = size;
            return go;
        }

        static void ConfigureCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.orthographic = true;
            cam.orthographicSize = 8f;
            cam.backgroundColor = P8Black;               // PICO-8 black void
            cam.transform.position = new Vector3(0f, 1f, -10f);

            // Add a Pixel Perfect Camera via reflection so this compiles even if the package moves.
            var type = System.Type.GetType("UnityEngine.U2D.PixelPerfectCamera, Unity.2D.PixelPerfect.Runtime")
                       ?? System.Type.GetType("UnityEngine.U2D.PixelPerfectCamera, Unity.RenderPipelines.Universal.Runtime");
            if (type != null && cam.GetComponent(type) == null)
            {
                var ppc = cam.gameObject.AddComponent(type);
                // Chunky PICO-8-ish pixels: low reference resolution + our 16 PPU tiles.
                TrySet(ppc, "assetsPPU", SpriteFactory.PPU);
                TrySet(ppc, "refResolutionX", 256);
                TrySet(ppc, "refResolutionY", 256);
                TrySet(ppc, "upscaleRT", true);
                TrySet(ppc, "pixelSnapping", true);
            }
        }

        static void TrySet(Object target, string field, object value)
        {
            var t = target.GetType();
            var p = t.GetProperty(field, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite) { p.SetValue(target, value); return; }
            var f = t.GetField(field, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) f.SetValue(target, value);
        }

        static PlayerConfig LoadOrCreateConfig()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<PlayerConfig>(ConfigPath);
            if (cfg != null) return cfg;

            const string dir = "Assets/_Game/Data";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/_Game")) AssetDatabase.CreateFolder("Assets", "_Game");
                AssetDatabase.CreateFolder("Assets/_Game", "Data");
            }
            cfg = ScriptableObject.CreateInstance<PlayerConfig>();
            AssetDatabase.CreateAsset(cfg, ConfigPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Builder] Created {ConfigPath}");
            return cfg;
        }

        /// <summary>Ensure a user layer exists; return its index (falls back to Default=0).</summary>
        static int EnsureLayer(string layerName)
        {
            int existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0) return existing;

            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (asset.Length == 0) return 0;
            var tagManager = new SerializedObject(asset[0]);
            var layers = tagManager.FindProperty("layers");

            for (int i = 8; i < layers.arraySize; i++) // 0-7 are Unity built-ins
            {
                var sp = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(sp.stringValue))
                {
                    sp.stringValue = layerName;
                    tagManager.ApplyModifiedProperties();
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[Builder] Created layer '{layerName}' at index {i}.");
                    return i;
                }
            }
            Debug.LogWarning("[Builder] No free layer slots; using Default.");
            return 0;
        }
    }
}
