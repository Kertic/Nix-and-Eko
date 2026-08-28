using NixAndEko.Combat;
using NixAndEko.Environment;
using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Level
{
    /// <summary>
    /// A single level block that lives in the scene as its own GameObject. Replaces the old
    /// data-list workflow: you author the level by adding these to the scene (via
    /// <b>Tools ▸ Nix &amp; Eko ▸ Level Palette</b>), duplicating them with Ctrl+D, moving them
    /// with the standard translate handle, and deleting them with Delete. Unity's hierarchy and
    /// undo system own the state.
    ///
    /// The component self-configures at edit time (via <see cref="ExecuteAlways"/>) — its sprite
    /// and collider always mirror the current <see cref="type"/> and <see cref="size"/>. Gameplay
    /// components (Hazard, Checkpoint, Gate, TargetSwitch, BreakableWall, MovingPlatform,
    /// enemies) are added at runtime in <see cref="Start"/> so the scene file stays clean and
    /// doesn't need per-type prefabs.
    ///
    /// Switches wire to gates by <see cref="linkId"/>: at runtime a switch scans the scene for
    /// every gate block sharing its link id and hands them to its <see cref="TargetSwitch"/>.
    ///
    /// A <see cref="BlockGroup"/> parent can override the per-block sprite so a run of tiled
    /// blocks reads as a single mass — see that component and <see cref="suppressVisual"/>.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class LevelBlockObject : MonoBehaviour
    {
        public BlockType type = BlockType.Ground;
        [Tooltip("Block footprint in world units. Snapped to the sprite + collider.")]
        public Vector2 size = new Vector2(4f, 1f);

        [Header("Type-specific")]
        [Tooltip("MovingPlatform: patrol offset from its start position.")]
        public Vector2 patrolOffset = new Vector2(4f, 0f);
        [Tooltip("MovingPlatform: travel speed, units/sec.")]
        public float speed = 3f;
        [Tooltip("Gate / TargetSwitch: switches drive every gate sharing their link id.")]
        public int linkId;
        [Tooltip("BreakableWall: minimum draw charge (0-1) that can break it.")]
        [Range(0f, 1f)] public float minCharge = 0.5f;
        [Tooltip("Gate: local offset applied when open.")]
        public Vector2 openOffset = new Vector2(0f, 4.5f);

        [Header("Layers")]
        [Tooltip("Layer solid blocks live on; must match the player's ground mask.")]
        public string groundLayerName = "Ground";

        /// <summary>Set by an enclosing <see cref="BlockGroup"/> that renders the union of its
        /// children instead: the per-block sprite is hidden so tiling reads as one continuous
        /// mass. The collider is kept — collision still runs per-block so gameplay stays correct.</summary>
        [HideInInspector] public bool suppressVisual;

        SpriteRenderer _sr;
        ProceduralSprite _ps;
        BoxCollider2D _col;
        // The grass tuft that Ground blocks pin along their top edge. Kept alongside so it can be
        // resized in step with the block and torn down when the type changes away from Ground.
        Transform _grassCap;
        bool _runtimeBuilt;

        /// <summary>World-space rect this block covers — used by <see cref="BlockGroup"/> to compute the group's union.</summary>
        public Rect WorldRect
        {
            get
            {
                Vector2 c = transform.position;
                return new Rect(c - size * 0.5f, size);
            }
        }

        void OnEnable() => RebuildVisual();

        void OnValidate()
        {
#if UNITY_EDITOR
            // Rebuild on the next editor tick — Unity forbids some rendering calls (like setting
            // SpriteRenderer.sprite) during OnValidate itself. See ProceduralSprite for the same
            // dance.
            if (!isActiveAndEnabled) return;
            UnityEditor.EditorApplication.delayCall += DelayedRebuild;
#else
            if (isActiveAndEnabled) RebuildVisual();
#endif
        }

#if UNITY_EDITOR
        void DelayedRebuild()
        {
            UnityEditor.EditorApplication.delayCall -= DelayedRebuild;
            if (this == null) return;
            RebuildVisual();
        }
#endif

        void Start()
        {
            if (!Application.isPlaying || _runtimeBuilt) return;
            _runtimeBuilt = true;
            BuildRuntimeGameplay();
        }

        // ------------------------------------------------------------------ edit-time visuals
        /// <summary>Reset the SpriteRenderer, ProceduralSprite and BoxCollider2D to match the
        /// current <see cref="type"/> and <see cref="size"/>. Safe to call any time, edit or
        /// runtime — a no-op if nothing changed. Idempotent.</summary>
        public void RebuildVisual()
        {
            EnsureComponents();

            // Solid types live on the ground layer so the player treats them as terrain.
            gameObject.layer = LevelData.IsSolid(type) && LayerMask.NameToLayer(groundLayerName) >= 0
                ? LayerMask.NameToLayer(groundLayerName)
                : 0;

            // Visual (unless a group has taken it over).
            if (_sr != null && _ps != null)
            {
                _sr.enabled = !suppressVisual;
                _sr.sortingOrder = SortingOrderFor(type);
                _ps.shape = ShapeFor(type);
                _ps.seed = SeedFromPosition();
                _ps.tiledSize = size;
                _ps.Rebuild();
            }

            // Collider.
            if (_col != null)
            {
                _col.size = size;
                _col.isTrigger = TypeIsTrigger(type);
                _col.usedByEffector = type == BlockType.OneWay;
                _col.edgeRadius = 0f;
            }

            UpdateGrassCap();
        }

        void EnsureComponents()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) _sr = gameObject.AddComponent<SpriteRenderer>();

            _ps = GetComponent<ProceduralSprite>();
            if (_ps == null) _ps = gameObject.AddComponent<ProceduralSprite>();

            _col = GetComponent<BoxCollider2D>();
            if (_col == null) _col = gameObject.AddComponent<BoxCollider2D>();
        }

        void UpdateGrassCap()
        {
            // Ground gets a grass strip along its top; other types shed it (and the child GO too).
            bool wantCap = type == BlockType.Ground && !suppressVisual;
            if (!wantCap)
            {
                if (_grassCap != null)
                {
                    if (Application.isPlaying) Destroy(_grassCap.gameObject);
                    else DestroyImmediate(_grassCap.gameObject);
                    _grassCap = null;
                }
                return;
            }

            if (_grassCap == null)
            {
                var found = transform.Find("GrassCap");
                _grassCap = found != null ? found : new GameObject("GrassCap").transform;
                _grassCap.SetParent(transform, worldPositionStays: false);
            }
            _grassCap.localPosition = new Vector3(0f, size.y * 0.5f, 0f);

            var sr = GetOrAdd<SpriteRenderer>(_grassCap.gameObject);
            sr.sortingOrder = SortingOrderFor(type) + 2;

            var ps = GetOrAdd<ProceduralSprite>(_grassCap.gameObject);
            ps.shape = ProceduralSprite.Shape.GrassCap;
            ps.seed = SeedFromPosition();
            ps.tiledSize = new Vector2(size.x, 0.5f);
            ps.Rebuild();
        }

        // ------------------------------------------------------------------ runtime gameplay
        /// <summary>Add the type-specific gameplay components — matches LevelBuilder's old factories
        /// but operates on this GameObject instead of building one from scratch.</summary>
        void BuildRuntimeGameplay()
        {
            switch (type)
            {
                case BlockType.OneWay:
                    if (GetComponent<PlatformEffector2D>() == null)
                    {
                        var eff = gameObject.AddComponent<PlatformEffector2D>();
                        eff.useOneWay = true;
                        eff.surfaceArc = 100f;
                        eff.useSideFriction = false;
                        eff.useSideBounce = false;
                    }
                    if (GetComponent<OneWayPlatform>() == null)
                        gameObject.AddComponent<OneWayPlatform>().dropSeconds = 0.35f;
                    break;

                case BlockType.MovingPlatform:
                    if (GetComponent<Rigidbody2D>() == null)
                        gameObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                    var mp = GetOrAdd<MovingPlatform>(gameObject);
                    mp.localOffset = patrolOffset;
                    mp.speed = speed;
                    break;

                case BlockType.Hazard:
                    if (GetComponent<Hazard>() == null)
                        gameObject.AddComponent<Hazard>().damage = 1;
                    break;

                case BlockType.Checkpoint:
                    if (GetComponent<Checkpoint>() == null)
                        gameObject.AddComponent<Checkpoint>();
                    break;

                case BlockType.Gate:
                    var gate = GetOrAdd<Gate>(gameObject);
                    gate.openOffset = openOffset;
                    break;

                case BlockType.TargetSwitch:
                    var sw = GetOrAdd<TargetSwitch>(gameObject);
                    sw.toggle = true;
                    sw.targetRenderer = _sr;
                    sw.gates = FindLinkedGates(linkId);
                    // The switch is a small round contact, not a full 1x1 block — replace the box
                    // collider with a circle at runtime so the shootable area matches the visual.
                    if (_col != null) _col.isTrigger = true;
                    break;

                case BlockType.BreakableWall:
                    var bw = GetOrAdd<BreakableWall>(gameObject);
                    bw.hitsToBreak = 1;
                    bw.minCharge = minCharge;
                    break;

                case BlockType.EnemyWalker:
                case BlockType.EnemySlammer:
                    // Enemies aren't shipped as scene-block subtypes yet — placeholder support
                    // via a small tinted rect + no AI is fine for level blockout. Wire in the
                    // full enemy factory when we're ready to author encounters this way too.
                    break;
            }
        }

        Gate[] FindLinkedGates(int link)
        {
            var all = FindObjectsByType<LevelBlockObject>(FindObjectsSortMode.None);
            var list = new System.Collections.Generic.List<Gate>();
            foreach (var b in all)
            {
                if (b == null || b.type != BlockType.Gate || b.linkId != link) continue;
                var g = GetOrAdd<Gate>(b.gameObject);
                g.openOffset = b.openOffset;
                list.Add(g);
            }
            return list.ToArray();
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>
        /// GetComponent-or-AddComponent that respects Unity's fake-null semantics. The C# `??`
        /// operator sees a missing UnityEngine.Object as a live reference (Unity wraps the missing
        /// native side in a managed shell whose == override reads as null but ?? sees as a valid
        /// object). That silently returns a placeholder, which then throws MissingComponentException
        /// the next time you touch it. This uses `== null` (which Unity handles correctly) so a
        /// truly-missing component gets added, and an existing one is returned as-is.
        /// </summary>
        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        int SeedFromPosition() => Mathf.Abs(
            Mathf.RoundToInt(transform.position.x * 7.13f + transform.position.y * 3.71f)) % 997;

        static ProceduralSprite.Shape ShapeFor(BlockType t) => t switch
        {
            BlockType.Ground         => ProceduralSprite.Shape.Earth,
            BlockType.OneWay         => ProceduralSprite.Shape.Plank,
            BlockType.MovingPlatform => ProceduralSprite.Shape.Plank,
            BlockType.Hazard         => ProceduralSprite.Shape.Thorn,
            BlockType.Checkpoint     => ProceduralSprite.Shape.Shrine,
            BlockType.Gate           => ProceduralSprite.Shape.Runestone,
            BlockType.TargetSwitch   => ProceduralSprite.Shape.Target,
            BlockType.BreakableWall  => ProceduralSprite.Shape.CrystalWall,
            _                        => ProceduralSprite.Shape.Tile,
        };

        static int SortingOrderFor(BlockType t) => t switch
        {
            BlockType.Hazard        => 3,
            BlockType.Checkpoint    => 3,
            BlockType.TargetSwitch  => 5,
            BlockType.OneWay        => 1,
            BlockType.Gate          => 1,
            BlockType.BreakableWall => 1,
            BlockType.MovingPlatform=> 1,
            _                       => 0,
        };

        static bool TypeIsTrigger(BlockType t) =>
            t == BlockType.Hazard || t == BlockType.Checkpoint || t == BlockType.TargetSwitch;

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            // Gate open offset / MovingPlatform patrol shown as dotted intents, matching the old
            // scene-view overlays from the deprecated data-driven editor.
            Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
            Vector3 p = transform.position;
            if (type == BlockType.Gate)
                Gizmos.DrawLine(p, p + (Vector3)openOffset);
            if (type == BlockType.MovingPlatform)
                Gizmos.DrawLine(p, p + (Vector3)patrolOffset);
        }
#endif
    }
}
