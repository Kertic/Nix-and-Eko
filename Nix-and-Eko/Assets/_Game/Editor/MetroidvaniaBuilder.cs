using System.Collections.Generic;
using NixAndEko.Level;
using UnityEditor;
using UnityEngine;

namespace NixAndEko.EditorTools
{
    /// <summary>
    /// Regenerates <c>Assets/_Game/Data/SampleMetroidvania.asset</c> — a large, hand-authored
    /// Hollow-Knight-style map made entirely from blocks the in-game Level Editor could place.
    ///
    /// The layout lives here as code (a sequence of typed helper calls) rather than in the asset
    /// itself so it's diff-friendly and quick to iterate: tweak a corridor width, run
    /// <b>Tools ▸ Nix &amp; Eko ▸ Build Sample Metroidvania</b>, and the asset is rewritten in
    /// place. Existing hand edits to the asset are replaced (a confirm dialog warns first).
    /// </summary>
    public static class MetroidvaniaBuilder
    {
        public const string AssetPath = "Assets/_Game/Data/SampleMetroidvania.asset";

        [MenuItem("Tools/Nix & Eko/Build Sample Metroidvania", priority = 20)]
        public static void BuildAndOpen()
        {
            LevelData generated = Generate();

            LevelData existing = AssetDatabase.LoadAssetAtPath<LevelData>(AssetPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, AssetPath);
            }
            else
            {
                if (!EditorUtility.DisplayDialog("Regenerate metroidvania level?",
                        "Replace every block in SampleMetroidvania.asset with the freshly generated layout? " +
                        "Any hand-made edits are lost.", "Regenerate", "Cancel"))
                    return;
                Undo.RecordObject(existing, "Regenerate Metroidvania");
                existing.blocks = generated.blocks;
                existing.playerSpawn = generated.playerSpawn;
                existing.killY = generated.killY;
                EditorUtility.SetDirty(existing);
            }

            AssetDatabase.SaveAssets();
            var loaded = AssetDatabase.LoadAssetAtPath<LevelData>(AssetPath);
            Selection.activeObject = loaded;
            EditorGUIUtility.PingObject(loaded);
            TestLevelBuilder.BuildScene(loaded);
        }

        /// <summary>Build the <see cref="LevelData"/> in memory — used by the menu item and shared
        /// with anyone else who needs a fresh copy of the sample metroidvania (e.g. tests).</summary>
        public static LevelData Generate()
        {
            var level = ScriptableObject.CreateInstance<LevelData>();
            level.playerSpawn = new Vector2(0f, 2f);
            level.killY = -80f;
            level.blocks = new List<LevelBlock>();

            var b = new Builder(level.blocks);

            // ---------------------------------------------------------- Whispering Grove (hub)
            b.Floor(0, 0, 80);
            b.Ceiling(-22, 22, 36);
            b.Ceiling( 22, 22, 36);       // gap at x=-4..+4 opens into the Crystalspire shaft
            // West/east hub walls, split to leave a 5-tall doorway (y=1..6) for the region gates.
            b.Wall(-40, 14.5f, 17);
            b.Wall( 40, 14.5f, 17);
            b.Platform(-14, 4, 6);
            b.Platform(  0, 6, 6);
            b.Platform( 14, 4, 6);
            b.Platform( -8, 10, 4);
            b.Platform(  8, 10, 4);
            b.Checkpoint(0, 1.5f);
            b.Walker(-20, 1.5f);
            b.Walker( 20, 1.5f);

            // ---------------------------------------------------------- Hub -> Sunken Cistern (west)
            b.Gate(-40, 3.5f, 2, 5, link: 1, openY: 5.5f);
            b.Switch(-34, 1.5f, link: 1);
            b.Floor(-56, 0, 30);
            b.Ceiling(-56, 8, 30);

            // ---------------------------------------------------------- Sunken Cistern
            b.Wall(-72, -8, 32);
            b.Wall(-56, -8, 32);
            b.Platform(-62, -2, 5);
            b.Platform(-66, -7, 4);
            b.Platform(-62, -12, 4);
            b.Platform(-66, -17, 4);
            b.Hazard(-64, -4.7f, 3);
            b.Hazard(-60, -14.7f, 3);
            b.Floor(-64, -24, 18);
            b.Checkpoint(-70, -22.5f);
            b.Slammer(-60, -23);
            b.Breakable(-55, -22.5f, 1.2f, 3, minCharge: 0.4f);
            b.Floor(-50, -24, 10);
            b.Checkpoint(-47, -22.5f);   // hidden reward alcove

            // ---------------------------------------------------------- Crystalspire (north)
            b.Wall(-6, 46, 46);
            b.Wall( 6, 46, 46);
            b.Platform(-3, 28, 4);
            b.Platform( 3, 34, 4);
            b.Platform(-3, 40, 4);
            b.Platform( 3, 46, 4);
            b.Platform(-3, 52, 4);
            b.Platform( 3, 58, 4);
            b.Platform(-3, 64, 4);
            b.Breakable(-6, 36, 1.2f, 3, minCharge: 0.6f);
            b.Breakable( 6, 54, 1.2f, 3, minCharge: 0.6f);
            b.Floor(-11, 34, 10);
            b.Checkpoint(-13, 35.5f);
            b.Floor(11, 52, 10);
            b.Checkpoint(13, 53.5f);
            // Summit chamber. The floor is two halves either side of the shaft (x=-6..+6),
            // so the shaft's top opens into the chamber rather than being sealed off.
            b.Floor(-15, 70, 18);       // x -24..-6
            b.Floor( 15, 70, 18);       // x  +6..+24
            b.Ceiling(0, 78, 24);
            b.Checkpoint(0, 71.5f);
            b.Slammer(-6, 71);
            b.Slammer( 6, 71);

            // ---------------------------------------------------------- Ember Foundry (east)
            b.Gate(40, 3.5f, 2, 5, link: 2, openY: 5.5f);
            b.Switch(8, 18, link: 2);   // switch high on a hub ledge
            b.Floor(76, -2, 68);
            b.Ceiling(76, 14, 68);
            b.Wall(110, 6, 16);
            b.Hazard(55, -1.7f, 8);
            b.Hazard(70, -1.7f, 8);
            b.Hazard(85, -1.7f, 8);
            b.Hazard(100, -1.7f, 8);
            b.Moving(50, 2, 3, patrol: new Vector2(0, 4), speed: 2.0f);
            b.Moving(62, 2, 3, patrol: new Vector2(0, 5), speed: 2.5f);
            b.Moving(78, 2, 3, patrol: new Vector2(8, 0), speed: 3.5f);
            b.Moving(92, 3, 3, patrol: new Vector2(-6, 0), speed: 3.0f);
            b.Moving(104, 4, 3, patrol: new Vector2(0, 6), speed: 2.5f);
            b.Slammer(60, 7);
            b.Slammer(90, 7);
            b.Checkpoint(108, -0.5f);

            // ---------------------------------------------------------- Hub floor notch -> Rootways
            // Rewrite the hub floor as two halves either side of a 4-wide trap-gate at x=-24..-20.
            b.RemoveWhere(x => x.type == BlockType.Ground && Mathf.Abs(x.position.y + 1f) < 0.6f
                               && Mathf.Approximately(x.size.x, 80f));
            b.Floor(-32, 0, 16);        // -40..-24
            b.Floor( 10, 0, 60);        // -20..40
            b.Gate(-22, -1, 4, 2, link: 3, openY: 2.5f);
            b.Switch(-30, 6, link: 3);

            // ---------------------------------------------------------- Rootways (south)
            // Left / right walls sit above the gates so the only way through is the gate itself
            // (a 4-tall doorway at y=-22..-18, exactly the gate size).
            b.Wall(-30, -10, 16);
            b.Wall( 30, -10, 16);
            b.Floor(0, -22, 60);
            b.Platform(-22, -6, 6);
            b.Platform(-10, -9, 6);
            b.Platform(  4, -12, 6);
            b.Platform( 18, -15, 6);
            b.Platform( 26, -18, 4);
            b.Gate(-30, -20, 2, 4, link: 4, openY: 4.5f);
            b.Gate( 30, -20, 2, 4, link: 4, openY: 4.5f);
            b.Switch(-6, -8, link: 4);
            b.Switch(22, -14, link: 4);
            b.Walker(-14, -21.5f);
            b.Walker( 10, -21.5f);
            b.Walker( 22, -21.5f);
            b.Hazard(0, -21.7f, 4);
            b.Checkpoint(-28, -20.5f);
            b.Checkpoint( 28, -20.5f);

            // Eastern reward tunnel out of the Rootways.
            b.Floor(46, -22, 30);
            b.Ceiling(46, -14, 30);
            b.Wall(60, -18, 8);
            b.Breakable(45, -20.5f, 1.2f, 3, minCharge: 0.7f);
            b.Checkpoint(55, -20.5f);

            return level;
        }

        /// <summary>
        /// Small typed builder over a block list — lets the layout above read like ASCII art
        /// rather than repeated struct-init boilerplate. Floors anchor by their top edge, ceilings
        /// by their bottom, matching the Level Editor's grid-snap conventions.
        /// </summary>
        class Builder
        {
            readonly List<LevelBlock> _list;
            public Builder(List<LevelBlock> list) { _list = list; }

            public void Floor(float x, float topY, float w, float t = 2f)  => Ground(x, topY - t * 0.5f, w, t);
            public void Ceiling(float x, float baseY, float w, float t = 2f) => Ground(x, baseY + t * 0.5f, w, t);
            public void Wall(float x, float y, float h, float t = 2f) => Ground(x, y, t, h);

            public void Ground(float x, float y, float w, float h)
                => Add(BlockType.Ground, x, y, w, h);
            public void Platform(float x, float y, float w)
                => Add(BlockType.OneWay, x, y, w, 0.4f);
            public void Hazard(float x, float y, float w)
                => Add(BlockType.Hazard, x, y, w, 0.6f);
            public void Checkpoint(float x, float y)
                => Add(BlockType.Checkpoint, x, y, 1f, 2f);
            public void Walker(float x, float y)
                => Add(BlockType.EnemyWalker, x, y, 0.9f, 1f);
            public void Slammer(float x, float y)
                => Add(BlockType.EnemySlammer, x, y, 1f, 0.9f);

            public void Breakable(float x, float y, float w, float h, float minCharge)
            {
                var blk = Add(BlockType.BreakableWall, x, y, w, h);
                blk.minCharge = minCharge;
            }
            public void Moving(float x, float y, float w, Vector2 patrol, float speed)
            {
                var blk = Add(BlockType.MovingPlatform, x, y, w, 0.5f);
                blk.patrolOffset = patrol;
                blk.speed = speed;
            }
            public void Gate(float x, float y, float w, float h, int link, float openY)
            {
                var blk = Add(BlockType.Gate, x, y, w, h);
                blk.linkId = link;
                blk.openOffset = new Vector2(0f, openY);
            }
            public void Switch(float x, float y, int link)
            {
                var blk = Add(BlockType.TargetSwitch, x, y, 1f, 1f);
                blk.linkId = link;
            }

            public void RemoveWhere(System.Predicate<LevelBlock> pred) => _list.RemoveAll(pred);

            LevelBlock Add(BlockType t, float x, float y, float w, float h)
            {
                var blk = new LevelBlock
                {
                    type = t,
                    position = new Vector2(x, y),
                    size = new Vector2(w, h),
                };
                _list.Add(blk);
                return blk;
            }
        }
    }
}
