using NixAndEko.Combat;
using NixAndEko.Environment;
using NixAndEko.Player;
using NixAndEko.Util;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NixAndEko.Level
{
    /// <summary>
    /// Assembles the archer — controller, input, health, bow and all of the bow's indicators —
    /// in code, so there is no player prefab to keep in sync with the scripts.
    /// </summary>
    public static class PlayerFactory
    {
        public static PlayerController Build(PlayerConfig config, InputActionAsset inputAsset, int groundLayer,
                                             Arrow arrowTemplate, Vector2 pos, Transform parent, float killY = -40f)
        {
            var go = new GameObject("Player");
            // Assemble while inactive: AddComponent runs Awake immediately on a live object,
            // which would wake components before their references are wired up.
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;

            // Square body (Celeste-classic style), not a tall capsule.
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.9f, 0.9f);

            // Sprite child (flipped for facing).
            var spriteGo = new GameObject("Sprite");
            spriteGo.transform.SetParent(go.transform, false);
            var sr = spriteGo.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 10;
            sr.sprite = ArcherSprites.IdleFrames[0];

            var controller = go.AddComponent<PlayerController>();
            controller.config = config;
            controller.spriteRoot = spriteGo.transform;
            controller.groundMask = 1 << groundLayer;

            var animator = spriteGo.AddComponent<PlayerAnimator>();
            animator.player = controller;

            var reader = go.AddComponent<PlayerInputReader>();
            reader.actions = inputAsset;
            reader.config = config;
            controller.input = reader;

            var health = go.AddComponent<Health>();
            health.player = controller;
            health.config = config;
            health.killY = killY;

            Bow bow = BuildBow(go, controller, reader, arrowTemplate);
            BuildGlideFuelBar(go, controller);

            // Wind streaks trailing behind her while gliding.
            var wind = go.AddComponent<WindStreaks>();
            wind.player = controller;

            // Dense straight-line streaks flashed out during a bow burst's input-lock window.
            var burst = go.AddComponent<BurstStreaks>();
            burst.player = controller;

            // Decides, each physics step, which one-way platforms are solid for her — so she can
            // never snag on one's side or end up standing partway inside it.
            var oneWay = go.AddComponent<OneWayPassenger>();
            oneWay.player = controller;

            // Eko lives in world space (under the level root, not the player) so the phantom
            // stays frozen where it was planted while Nix flies off.
            Eko eko = BuildEko(parent, arrowTemplate, controller);

            var summoner = go.AddComponent<EkoSummoner>();
            summoner.player = controller;
            summoner.input = reader;
            summoner.bow = bow;
            summoner.eko = eko;

            // Lets Eko's arrows catch Nix and reload her air shot + glide meter.
            var ekoTarget = go.AddComponent<EkoArrowTarget>();
            ekoTarget.bow = bow;
            ekoTarget.player = controller;

            go.SetActive(true);   // everything is wired; let the components wake
            return controller;
        }

        static Bow BuildBow(GameObject playerGo, PlayerController controller,
                            PlayerInputReader reader, Arrow arrowTemplate)
        {
            var bowGo = new GameObject("Bow");
            bowGo.transform.SetParent(playerGo.transform, false);

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(bowGo.transform, false); // aim originates from the player's centre

            var bow = bowGo.AddComponent<Bow>();
            bow.player = controller;
            bow.input = reader;
            bow.muzzle = muzzle.transform;
            bow.arrowPrefab = arrowTemplate;
            bow.eightDirectional = true;

            // Reticle: points along the snapped aim, tinting white -> red with charge.
            var indicatorGo = new GameObject("AimIndicator");
            indicatorGo.transform.SetParent(bowGo.transform, false);
            var indSr = indicatorGo.AddComponent<SpriteRenderer>();
            indSr.sortingOrder = 20;
            var indPs = indicatorGo.AddComponent<ProceduralSprite>();
            indPs.shape = ProceduralSprite.Shape.Arrow;
            indPs.primary = Palette.White;
            indPs.secondary = Palette.LightGrey;
            indPs.pixelsX = 12; indPs.pixelsY = 5;
            indPs.Rebuild();
            indicatorGo.SetActive(false);
            bow.aimIndicator = indicatorGo.transform;
            bow.aimIndicatorRenderer = indSr;

            // Trajectory preview.
            var trajGo = new GameObject("Trajectory");
            trajGo.transform.SetParent(bowGo.transform, false);
            var lr = trajGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.12f;
            lr.numCapVertices = 2;
            lr.textureMode = LineTextureMode.Tile;
            lr.alignment = LineAlignment.View;
            lr.sortingOrder = 19;
            lr.positionCount = 0;
            lr.startColor = Palette.White;
            lr.endColor = new Color(1f, 1f, 1f, 0f);
            trajGo.AddComponent<ProceduralLine>(); // keeps the material valid across save/reload
            bow.trajectory = lr;

            // Drag anchor: the circle marking where the drag started.
            var anchorGo = new GameObject("DragAnchor");
            anchorGo.transform.SetParent(bowGo.transform, false);
            anchorGo.transform.localScale = Vector3.one * 0.6f;
            var anchorSr = anchorGo.AddComponent<SpriteRenderer>();
            anchorSr.sortingOrder = 21;
            anchorSr.color = new Color(1f, 1f, 1f, 0.75f);
            var anchorPs = anchorGo.AddComponent<ProceduralSprite>();
            anchorPs.shape = ProceduralSprite.Shape.Circle;
            anchorPs.primary = Palette.White;
            anchorPs.circleThickness = 0.25f;
            anchorPs.pixelsX = 16; anchorPs.pixelsY = 16;
            anchorPs.Rebuild();
            anchorGo.SetActive(false);
            bow.dragAnchorIndicator = anchorGo.transform;

            bow.passThroughMarkers = BuildPassThroughMarkers(bowGo.transform);

            return bow;
        }

        /// <summary>
        /// Small dot markers a trajectory preview drops wherever it crosses clean through a
        /// one-way platform instead of stopping there. Pre-built and toggled on/off each frame
        /// rather than spawned per-crossing.
        /// </summary>
        static Transform[] BuildPassThroughMarkers(Transform parent, int count = 4)
        {
            var markers = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("PassThroughMarker");
                go.transform.SetParent(parent, false);
                go.transform.localScale = Vector3.one * 0.35f;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = 20;

                var ps = go.AddComponent<ProceduralSprite>();
                ps.shape = ProceduralSprite.Shape.Circle;
                ps.primary = Palette.Yellow;
                ps.circleThickness = 1f;   // filled dot, not a ring
                ps.pixelsX = 10; ps.pixelsY = 10;
                ps.Rebuild();

                go.SetActive(false);
                markers[i] = go.transform;
            }
            return markers;
        }

        /// <summary>
        /// The Eko phantom: a translucent blue echo of Nix that gets planted in the world, plus
        /// its straight-line shot preview. Built once and kept inactive between summons.
        /// </summary>
        static Eko BuildEko(Transform parent, Arrow arrowTemplate, PlayerController player)
        {
            var go = new GameObject("Eko");
            go.transform.SetParent(parent, false);
            go.SetActive(false);

            // Trigger so one of Nix's arrows can strike the phantom and swap their places. Sized to
            // the silhouette; a trigger never blocks or shoves anything.
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.8f, 0.9f);

            var spriteGo = new GameObject("Sprite");
            spriteGo.transform.SetParent(go.transform, false);
            var sr = spriteGo.AddComponent<SpriteRenderer>();
            // In front of Nix (11 > her 10) so the translucent echo is always visible, even when
            // it's planted right on top of her before she moves off.
            sr.sortingOrder = 11;
            sr.sprite = ArcherSprites.IdleFrames[0];

            // Straight shot preview — two points, so no arc simulation needed.
            var trajGo = new GameObject("Trajectory");
            trajGo.transform.SetParent(go.transform, false);
            var lr = trajGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.1f;
            lr.numCapVertices = 2;
            lr.textureMode = LineTextureMode.Tile;
            lr.alignment = LineAlignment.View;
            lr.sortingOrder = 19;
            lr.positionCount = 0;
            trajGo.AddComponent<ProceduralLine>(); // keeps the material valid across save/reload

            var eko = go.AddComponent<Eko>();
            eko.sprite = sr;
            eko.trajectory = lr;
            eko.arrowPrefab = arrowTemplate;
            eko.player = player;
            eko.passThroughMarkers = BuildPassThroughMarkers(go.transform);
            return eko;
        }

        /// <summary>
        /// The glide-fuel meter, parented under the player root (not the flipping sprite child)
        /// so it never mirrors with facing.
        /// </summary>
        static void BuildGlideFuelBar(GameObject playerGo, PlayerController controller)
        {
            var barGo = new GameObject("GlideFuelBar");
            barGo.transform.SetParent(playerGo.transform, false);
            var bar = barGo.AddComponent<GlideFuelBar>();
            bar.player = controller;
        }

        /// <summary>An inactive arrow the Bow clones for each shot.</summary>
        public static Arrow BuildArrowTemplate(Transform parent)
        {
            var go = new GameObject("ArrowTemplate");
            go.transform.SetParent(parent, false);
            go.SetActive(false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 9;
            var ps = go.AddComponent<ProceduralSprite>();
            ps.shape = ProceduralSprite.Shape.Arrow;
            ps.primary = new Color(0.9f, 0.85f, 0.6f);
            ps.secondary = Palette.LightGrey;
            ps.pixelsX = 16; ps.pixelsY = 6;

            go.AddComponent<Rigidbody2D>();
            go.AddComponent<BoxCollider2D>().size = new Vector2(1.0f, 0.35f);
            return go.AddComponent<Arrow>();
        }
    }
}
