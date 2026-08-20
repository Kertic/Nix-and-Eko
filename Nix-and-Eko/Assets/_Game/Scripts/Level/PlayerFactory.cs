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
            controller.input = reader;

            var health = go.AddComponent<Health>();
            health.player = controller;
            health.config = config;
            health.killY = killY;

            BuildBow(go, controller, reader, arrowTemplate);
            return controller;
        }

        static void BuildBow(GameObject playerGo, PlayerController controller,
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
