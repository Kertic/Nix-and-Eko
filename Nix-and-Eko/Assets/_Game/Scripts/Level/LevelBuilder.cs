using System.Collections.Generic;
using NixAndEko.Combat;
using NixAndEko.Environment;
using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Level
{
    /// <summary>
    /// Turns a <see cref="LevelData"/> asset into live GameObjects, styled as an enchanted
    /// forest: mossy earth with grass-and-vine caps, weathered planks, bramble spikes, carved
    /// runestone gates and cracked crystal. Used by the level editor to preview edits and by
    /// <see cref="LevelLoader"/> at runtime, so what you author is exactly what you play.
    /// </summary>
    public static class LevelBuilder
    {
        public const string GeometryRootName = "Level Geometry";

        /// <summary>Rebuild every block of <paramref name="data"/> under a fresh child of <paramref name="parent"/>.</summary>
        public static Transform Build(LevelData data, Transform parent, int groundLayer)
        {
            if (data == null) return null;

            var rootGo = new GameObject(GeometryRootName);
            rootGo.transform.SetParent(parent, false);
            Transform root = rootGo.transform;

            // Gates are created first so switches can wire themselves to them by link id.
            var gatesByLink = new Dictionary<int, List<Gate>>();
            foreach (var b in data.blocks)
            {
                if (b.type != BlockType.Gate) continue;
                Gate gate = CreateGate(b, root, groundLayer);
                if (!gatesByLink.TryGetValue(b.linkId, out var list))
                    gatesByLink[b.linkId] = list = new List<Gate>();
                list.Add(gate);
            }

            foreach (var b in data.blocks)
            {
                switch (b.type)
                {
                    case BlockType.Gate: break; // already built above
                    case BlockType.Ground: CreateGround(b, root, groundLayer); break;
                    case BlockType.OneWay: CreateOneWay(b, root, groundLayer); break;
                    case BlockType.MovingPlatform: CreateMoving(b, root, groundLayer); break;
                    case BlockType.Hazard: CreateHazard(b, root); break;
                    case BlockType.Checkpoint: CreateCheckpoint(b, root); break;
                    case BlockType.BreakableWall: CreateBreakable(b, root, groundLayer); break;
                    case BlockType.TargetSwitch:
                        gatesByLink.TryGetValue(b.linkId, out var linked);
                        CreateSwitch(b, root, linked);
                        break;
                    case BlockType.EnemyWalker: CreateEnemyWalker(b, root, groundLayer); break;
                    case BlockType.EnemySlammer: CreateEnemySlammer(b, root); break;
                }
            }

            CreateBackdrop(data, root);
            return root;
        }

        // ---------------------------------------------------------------- block factories
        /// <summary>A tiled sprite + box collider sized to the block.</summary>
        public static GameObject CreateBlockObject(string name, Transform parent, Vector2 pos, Vector2 size,
                                                   ProceduralSprite.Shape shape, int layer,
                                                   int sortingOrder = 0, int seed = 7)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            if (layer > 0) go.layer = layer;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = sortingOrder;

            var ps = go.AddComponent<ProceduralSprite>();
            ps.shape = shape;
            ps.seed = seed;
            ps.tiledSize = size;
            ps.Rebuild();

            var col = go.AddComponent<BoxCollider2D>();
            col.size = size;
            return go;
        }

        /// <summary>Grass, vines and the odd glowing spore along the top edge of solid ground.</summary>
        static void AddGrassCap(GameObject parent, Vector2 size, int seed)
        {
            var cap = new GameObject("GrassCap");
            cap.transform.SetParent(parent.transform, false);
            cap.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);

            var sr = cap.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 2;   // draws over the earth body

            var ps = cap.AddComponent<ProceduralSprite>();
            ps.shape = ProceduralSprite.Shape.GrassCap;
            ps.seed = seed;
            ps.tiledSize = new Vector2(size.x, 0.5f);
            ps.Rebuild();
        }

        static int SeedFor(LevelBlock b) => Mathf.Abs(
            Mathf.RoundToInt(b.position.x * 7.13f + b.position.y * 3.71f)) % 997;

        static void CreateGround(LevelBlock b, Transform parent, int layer)
        {
            int seed = SeedFor(b);
            var go = CreateBlockObject("Ground", parent, b.position, b.size,
                                       ProceduralSprite.Shape.Earth, layer, 0, seed);
            AddGrassCap(go, b.size, seed);
        }

        static void CreateOneWay(LevelBlock b, Transform parent, int layer)
        {
            var go = CreateBlockObject("OneWayPlatform", parent, b.position, b.size,
                                       ProceduralSprite.Shape.Plank, layer, 1, SeedFor(b));
            var col = go.GetComponent<BoxCollider2D>();
            col.usedByEffector = true;
            // No edge radius: it inflates the collider past the visible plank, so resting on top
            // would float above the sprite and the surface Y wouldn't match what's drawn. It was
            // there to stop edges snagging, which OneWayPassenger's feet-above-top rule now
            // prevents outright.
            col.edgeRadius = 0f;
            var eff = go.AddComponent<PlatformEffector2D>();
            eff.useOneWay = true;
            eff.surfaceArc = 100f;    // arrows: only near-vertical contacts count as the surface
            eff.useSideFriction = false;
            eff.useSideBounce = false;
            go.AddComponent<OneWayPlatform>().dropSeconds = 0.35f;
        }

        static void CreateMoving(LevelBlock b, Transform parent, int layer)
        {
            var go = CreateBlockObject("MovingPlatform", parent, b.position, b.size,
                                       ProceduralSprite.Shape.Plank, layer, 1, SeedFor(b));
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var mp = go.AddComponent<MovingPlatform>();
            mp.localOffset = b.patrolOffset;
            mp.speed = b.speed;
        }

        static void CreateHazard(LevelBlock b, Transform parent)
        {
            var go = CreateBlockObject("Brambles", parent, b.position, b.size,
                                       ProceduralSprite.Shape.Thorn, 0, 3, SeedFor(b));
            go.GetComponent<BoxCollider2D>().isTrigger = true;
            go.AddComponent<Hazard>().damage = 1;
        }

        static void CreateCheckpoint(LevelBlock b, Transform parent)
        {
            var go = CreateBlockObject("Shrine", parent, b.position, b.size,
                                       ProceduralSprite.Shape.Shrine, 0, 3, SeedFor(b));
            go.GetComponent<BoxCollider2D>().isTrigger = true;
            go.AddComponent<Checkpoint>();
        }

        static void CreateBreakable(LevelBlock b, Transform parent, int layer)
        {
            var go = CreateBlockObject("CrystalWall", parent, b.position, b.size,
                                       ProceduralSprite.Shape.CrystalWall, layer, 1, SeedFor(b));
            var bw = go.AddComponent<BreakableWall>();
            bw.hitsToBreak = 1;
            bw.minCharge = b.minCharge;
        }

        static Gate CreateGate(LevelBlock b, Transform parent, int layer)
        {
            var go = CreateBlockObject("Gate (link " + b.linkId + ")", parent, b.position, b.size,
                                       ProceduralSprite.Shape.Runestone, layer, 1, SeedFor(b));
            var gate = go.AddComponent<Gate>();
            gate.openOffset = b.openOffset;
            return gate;
        }

        static void CreateSwitch(LevelBlock b, Transform parent, List<Gate> gates)
        {
            var go = new GameObject("TargetSwitch (link " + b.linkId + ")");
            go.transform.SetParent(parent, false);
            go.transform.position = b.position;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 5;

            var ps = go.AddComponent<ProceduralSprite>();
            ps.shape = ProceduralSprite.Shape.Target;
            ps.primary = Palette.Switch;
            ps.secondary = Palette.White;
            ps.pixelsX = 16; ps.pixelsY = 16;
            ps.Rebuild();

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;
            col.isTrigger = true;

            var sw = go.AddComponent<TargetSwitch>();
            sw.toggle = true;
            sw.targetRenderer = sr;
            sw.gates = gates != null ? gates.ToArray() : new Gate[0];
        }

        // ---------------------------------------------------------------- enemies
        /// <summary>
        /// Shared enemy rig: a kinematic root with a trigger collider (never shoves the player) plus
        /// <see cref="EnemyHealth"/>, a flipping sprite child with a <see cref="HitFlash"/>, and a
        /// hidden-until-damaged <see cref="EnemyHealthBar"/>. Returns (root, sprite renderer).
        /// </summary>
        static (GameObject root, SpriteRenderer sprite) CreateEnemyBase(
            LevelBlock b, Transform parent, string name, Sprite first, Vector2 colSize, int maxHp)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = b.position;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.freezeRotation = true;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = colSize;
            col.isTrigger = true;

            var spriteGo = new GameObject("Sprite");
            spriteGo.transform.SetParent(go.transform, false);
            var sr = spriteGo.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 8;
            sr.sprite = first;
            spriteGo.AddComponent<HitFlash>().target = sr;

            var health = go.AddComponent<EnemyHealth>();
            health.maxHealth = maxHp;
            health.sprite = sr;

            var barGo = new GameObject("HealthBar");
            barGo.transform.SetParent(go.transform, false);
            health.bar = barGo.AddComponent<EnemyHealthBar>();

            return (go, sr);
        }

        static void CreateEnemyWalker(LevelBlock b, Transform parent, int groundLayer)
        {
            var (go, sr) = CreateEnemyBase(b, parent, "EnemyWalker",
                                           EnemySprites.WalkerFrames[0], new Vector2(0.8f, 0.95f), 3);
            var walker = go.AddComponent<EnemyWalker>();
            walker.sprite = sr;
            walker.groundMask = 1 << groundLayer;
        }

        static void CreateEnemySlammer(LevelBlock b, Transform parent)
        {
            var (go, sr) = CreateEnemyBase(b, parent, "EnemySlammer",
                                           EnemySprites.SlammerIdle, new Vector2(0.9f, 0.85f), 4);
            var slammer = go.AddComponent<EnemySlammer>();
            slammer.sprite = sr;
        }

        // ---------------------------------------------------------------- backdrop
        /// <summary>
        /// Two layers of tree silhouettes behind the level, dark to light with distance, so the
        /// play space reads as a forest interior rather than a void.
        /// </summary>
        static void CreateBackdrop(LevelData data, Transform parent)
        {
            if (data.blocks.Count == 0) return;

            Bounds bounds = new Bounds(data.blocks[0].position, Vector3.zero);
            foreach (var b in data.blocks) bounds.Encapsulate(new Bounds(b.position, b.size));

            var backdrop = new GameObject("Backdrop");
            backdrop.transform.SetParent(parent, false);

            Color[] layerColors = { new Color(0.05f, 0.10f, 0.10f), new Color(0.08f, 0.17f, 0.15f) };
            float[] layerY = { bounds.min.y + 2f, bounds.min.y + 0.5f };
            float[] layerScale = { 1.6f, 1.0f };
            int[] layerOrder = { -30, -20 };
            float[] spacing = { 13f, 8f };

            for (int layer = 0; layer < 2; layer++)
            {
                int i = 0;
                for (float x = bounds.min.x - 10f; x < bounds.max.x + 10f; x += spacing[layer], i++)
                {
                    int seed = layer * 131 + i * 17;
                    float jitter = (PixelArt.Noise(i, layer, 5) - 0.5f) * spacing[layer] * 0.6f;
                    float h = Mathf.Lerp(18f, 34f, PixelArt.Noise(i, layer, 9)) * layerScale[layer];

                    var tree = new GameObject("Tree");
                    tree.transform.SetParent(backdrop.transform, false);
                    tree.transform.position = new Vector3(x + jitter, layerY[layer] + h * 0.5f, 0f);

                    var sr = tree.AddComponent<SpriteRenderer>();
                    sr.sortingOrder = layerOrder[layer];
                    sr.sprite = TileArt.TreeSilhouette(
                        Mathf.RoundToInt(Mathf.Lerp(40f, 70f, PixelArt.Noise(i, layer, 3))),
                        Mathf.RoundToInt(h * SpriteFactory.PPU / 2f),
                        layerColors[layer], seed);
                }
            }
        }
    }
}
