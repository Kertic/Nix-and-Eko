using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Level
{
    /// <summary>
    /// Groups a run of same-typed <see cref="LevelBlockObject"/>s so their sprites tile as one
    /// continuous mass rather than each showing its own seamed patch. The group parent computes
    /// the union rect of its direct children, hides each child's sprite (via
    /// <see cref="LevelBlockObject.suppressVisual"/>), and renders one big tiled sprite over the
    /// union — the tiling boundary is the group's rect, not the individual blocks'.
    ///
    /// Collisions still run per-block: gates stay individually openable, one-way platforms keep
    /// their own effectors, breakable walls take hits on their own colliders. The group is a
    /// visual-only overlay.
    ///
    /// Mixed types (Ground + OneWay in one group, for example) are allowed but only the
    /// dominant type's tile is rendered — mixing is usually a mistake, and a warning is logged
    /// via <see cref="LogMixedTypeWarning"/>. Group same-typed blocks; add a second
    /// <see cref="BlockGroup"/> alongside for a different type.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class BlockGroup : MonoBehaviour
    {
        [Tooltip("Sorting order for the group's unified sprite. Behind the children's own overlays.")]
        public int sortingOrder = 0;

        SpriteRenderer _sr;
        ProceduralSprite _ps;
        Rect _cachedUnion;
        BlockType _cachedType;
        int _cachedChildCount;

        void OnEnable() { EnsureVisual(); RebuildFromChildren(); }
        void OnDisable() { ReleaseChildren(); }

        void OnTransformChildrenChanged() => RebuildFromChildren();

        void Update()
        {
            // Track child moves / resizes in the editor scene view so the union stays tight even
            // as the level is being authored. Cheap: skips work when nothing changed.
            if (!DirtyCheck()) return;
            RebuildFromChildren();
        }

        /// <summary>Rebuild the group's union sprite from the current child blocks. Called
        /// automatically by <see cref="Update"/> and when children are added/removed, and safe to
        /// call by hand after a batch edit.</summary>
        public void RebuildFromChildren()
        {
            EnsureVisual();

            LevelBlockObject[] children = GetComponentsInChildren<LevelBlockObject>(includeInactive: false);
            if (children.Length == 0)
            {
                // No members yet — hide the group sprite so a fresh, empty group isn't a blank slab
                // squatting on the world.
                if (_sr != null) _sr.enabled = false;
                foreach (var c in children) c.suppressVisual = false;
                return;
            }

            // Union rect.
            Rect u = children[0].WorldRect;
            for (int i = 1; i < children.Length; i++)
            {
                Rect r = children[i].WorldRect;
                float xMin = Mathf.Min(u.xMin, r.xMin);
                float yMin = Mathf.Min(u.yMin, r.yMin);
                float xMax = Mathf.Max(u.xMax, r.xMax);
                float yMax = Mathf.Max(u.yMax, r.yMax);
                u = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
            }
            _cachedUnion = u;

            // Dominant type wins.
            BlockType dom = DominantType(children);
            _cachedType = dom;
            _cachedChildCount = children.Length;

            // Tell children to hide their own sprite; the group draws the tiled surface for them.
            foreach (var c in children)
            {
                c.suppressVisual = true;
                c.RebuildVisual();
            }

            // Group sprite covers the union rect at that type's shape.
            _sr.enabled = true;
            _sr.sortingOrder = sortingOrder;
            transform.position = new Vector3(u.center.x, u.center.y, transform.position.z);
            _ps.shape = ShapeFor(dom);
            _ps.seed = Mathf.Abs(Mathf.RoundToInt(u.xMin * 7.13f + u.yMin * 3.71f)) % 997;
            _ps.tiledSize = u.size;
            _ps.Rebuild();

            LogMixedTypeWarning(children, dom);
        }

        void ReleaseChildren()
        {
            foreach (var c in GetComponentsInChildren<LevelBlockObject>(includeInactive: true))
            {
                if (c == null) continue;
                c.suppressVisual = false;
                c.RebuildVisual();
            }
        }

        void EnsureVisual()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) _sr = gameObject.AddComponent<SpriteRenderer>();
            _ps = GetComponent<ProceduralSprite>();
            if (_ps == null) _ps = gameObject.AddComponent<ProceduralSprite>();
        }

        bool DirtyCheck()
        {
            LevelBlockObject[] children = GetComponentsInChildren<LevelBlockObject>(includeInactive: false);
            if (children.Length != _cachedChildCount) return true;
            if (children.Length == 0) return false;

            Rect u = children[0].WorldRect;
            BlockType dom = children[0].type;
            for (int i = 1; i < children.Length; i++)
            {
                Rect r = children[i].WorldRect;
                float xMin = Mathf.Min(u.xMin, r.xMin);
                float yMin = Mathf.Min(u.yMin, r.yMin);
                float xMax = Mathf.Max(u.xMax, r.xMax);
                float yMax = Mathf.Max(u.yMax, r.yMax);
                u = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
            }
            dom = DominantType(children);
            return u != _cachedUnion || dom != _cachedType;
        }

        static BlockType DominantType(LevelBlockObject[] children)
        {
            // The tallest count wins; ties break to Ground (the most common blockout type).
            var tally = new System.Collections.Generic.Dictionary<BlockType, int>();
            foreach (var c in children)
            {
                tally.TryGetValue(c.type, out int n);
                tally[c.type] = n + 1;
            }
            int best = -1;
            BlockType winner = BlockType.Ground;
            foreach (var kv in tally)
            {
                if (kv.Value > best || (kv.Value == best && kv.Key == BlockType.Ground))
                {
                    best = kv.Value;
                    winner = kv.Key;
                }
            }
            return winner;
        }

        static ProceduralSprite.Shape ShapeFor(BlockType t) => t switch
        {
            BlockType.Ground         => ProceduralSprite.Shape.Earth,
            BlockType.OneWay         => ProceduralSprite.Shape.Plank,
            BlockType.MovingPlatform => ProceduralSprite.Shape.Plank,
            BlockType.Hazard         => ProceduralSprite.Shape.Thorn,
            BlockType.Checkpoint     => ProceduralSprite.Shape.Shrine,
            BlockType.Gate           => ProceduralSprite.Shape.Runestone,
            BlockType.BreakableWall  => ProceduralSprite.Shape.CrystalWall,
            _                        => ProceduralSprite.Shape.Tile,
        };

        void LogMixedTypeWarning(LevelBlockObject[] children, BlockType dom)
        {
            foreach (var c in children)
            {
                if (c.type != dom)
                {
                    Debug.LogWarning($"[BlockGroup] '{name}' has mixed block types " +
                                     $"(rendering as {dom}). Group same-typed blocks separately for clean tiling.", this);
                    return;
                }
            }
        }
    }
}
