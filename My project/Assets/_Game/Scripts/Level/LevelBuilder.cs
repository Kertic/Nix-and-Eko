using System.Collections.Generic;
using NixAndEko.Environment;
using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Level
{
    /// <summary>
    /// Turns a <see cref="LevelData"/> asset into live GameObjects. Used by the level editor to
    /// preview edits and by <see cref="LevelLoader"/> to build a level at runtime, so what you
    /// author is exactly what you play.
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
                if (b.type == BlockType.Gate)
                {
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
                }
            }

            return root;
        }

        // ---------------------------------------------------------------- block factories
        public static GameObject CreateBlockObject(string name, Transform parent, Vector2 pos, Vector2 size,
                                                   Color fill, Color edge, int layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            if (layer > 0) go.layer = layer;

            go.AddComponent<SpriteRenderer>();
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

        static void CreateGround(LevelBlock b, Transform parent, int layer) =>
            CreateBlockObject("Ground", parent, b.position, b.size, Palette.Ground, Palette.GroundEdge, layer);

        static void CreateOneWay(LevelBlock b, Transform parent, int layer)
        {
            var go = CreateBlockObject("OneWayPlatform", parent, b.position, b.size,
                                       Palette.OneWay, Palette.OneWay * 0.6f, layer);
            go.GetComponent<BoxCollider2D>().usedByEffector = true;
            var eff = go.AddComponent<PlatformEffector2D>();
            eff.useOneWay = true;
            eff.surfaceArc = 140f;
            go.AddComponent<OneWayPlatform>().dropSeconds = 0.35f;
        }

        static void CreateMoving(LevelBlock b, Transform parent, int layer)
        {
            var go = CreateBlockObject("MovingPlatform", parent, b.position, b.size,
                                       Palette.Moving, Palette.Moving * 0.6f, layer);
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var mp = go.AddComponent<MovingPlatform>();
            mp.localOffset = b.patrolOffset;
            mp.speed = b.speed;
        }

        static void CreateHazard(LevelBlock b, Transform parent)
        {
            var go = CreateBlockObject("Hazard", parent, b.position, b.size,
                                       Palette.Hazard, Palette.Hazard * 0.6f, 0);
            go.GetComponent<BoxCollider2D>().isTrigger = true;
            go.AddComponent<Hazard>().damage = 1;
        }

        static void CreateCheckpoint(LevelBlock b, Transform parent)
        {
            var go = CreateBlockObject("Checkpoint", parent, b.position, b.size,
                                       Palette.Checkpoint, Palette.Checkpoint * 0.6f, 0);
            go.GetComponent<BoxCollider2D>().isTrigger = true;
            go.AddComponent<Checkpoint>();
        }

        static void CreateBreakable(LevelBlock b, Transform parent, int layer)
        {
            var go = CreateBlockObject("BreakableWall", parent, b.position, b.size,
                                       Palette.Breakable, Palette.Breakable * 0.6f, layer);
            var bw = go.AddComponent<BreakableWall>();
            bw.hitsToBreak = 1;
            bw.minCharge = b.minCharge;
        }

        static Gate CreateGate(LevelBlock b, Transform parent, int layer)
        {
            var go = CreateBlockObject($"Gate (link {b.linkId})", parent, b.position, b.size,
                                       Palette.Gate, Palette.Gate * 0.6f, layer);
            var gate = go.AddComponent<Gate>();
            gate.openOffset = b.openOffset;
            return gate;
        }

        static void CreateSwitch(LevelBlock b, Transform parent, List<Gate> gates)
        {
            var go = new GameObject($"TargetSwitch (link {b.linkId})");
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
    }
}
