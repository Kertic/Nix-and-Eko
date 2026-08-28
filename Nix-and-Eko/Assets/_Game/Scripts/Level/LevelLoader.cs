using System.Reflection;
using NixAndEko.Combat;
using NixAndEko.Player;
using NixAndEko.Util;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NixAndEko.Level
{
    /// <summary>
    /// Builds a <see cref="LevelData"/> asset at runtime, so a level authored in the Level Editor is
    /// assembled fresh from code every time the game launches — no baked scene objects to fall out of
    /// sync with the scripts. Put one of these on a GameObject in an otherwise empty scene, point it
    /// at the level/config/input assets, and press Play.
    /// </summary>
    public class LevelLoader : MonoBehaviour
    {
        [Tooltip("Legacy: assign a Level data asset to build geometry from it. Leave empty to " +
                 "use LevelBlockObjects already present in the scene (the new scene-first flow).")]
        public LevelData level;
        public PlayerConfig playerConfig;
        public InputActionAsset inputActions;
        [Tooltip("Layer solid geometry is placed on; must match the player's ground mask.")]
        public string groundLayerName = "Ground";
        public bool spawnPlayer = true;
        [Tooltip("Frame and follow the player with the main camera on launch.")]
        public bool configureCamera = true;
        [Tooltip("Spawn the on-screen debug HUD (controls + state readout).")]
        public bool spawnDebugHud = true;
        [Tooltip("Spawn the world map overlay (toggled with Select / M / Tab).")]
        public bool spawnMap = true;

        [Header("Scene-first mode (no LevelData asset)")]
        [Tooltip("Where the player starts when the level lives as scene GameObjects. Ignored " +
                 "when a LevelData asset is assigned above.")]
        public Vector2 sceneSpawn = new Vector2(0f, 2f);
        [Tooltip("Falling below this Y respawns the player. Scene-first mode only.")]
        public float sceneKillY = -80f;

        void Start()
        {
            int groundLayer = Mathf.Max(0, LayerMask.NameToLayer(groundLayerName));
            Vector2 spawn; float killY;

            if (level != null)
            {
                // Legacy data-driven flow — kept so old levels still boot.
                LevelBuilder.Build(level, transform, groundLayer);
                spawn = level.playerSpawn;
                killY = level.killY;
            }
            else
            {
                // Scene-first: LevelBlockObjects already in the scene self-configure via their
                // own ExecuteAlways + Start. Nothing to build here; the loader just spawns the
                // shared bits (backdrop, player, map, camera) alongside them.
                spawn = sceneSpawn;
                killY = sceneKillY;
            }

            BuildScenery();

            PlayerController player = null;
            if (spawnPlayer)
            {
                Arrow arrow = PlayerFactory.BuildArrowTemplate(transform);
                player = PlayerFactory.Build(playerConfig, inputActions, groundLayer,
                    arrow, spawn, transform, killY);

                if (spawnDebugHud) AddDebugHud(player);
                if (spawnMap) AddMap(player);
            }

            // Eko exists as a second PlayerController now (see PlayerFactory), so the camera needs
            // to be told explicitly who to follow rather than finding "the" PlayerController.
            if (configureCamera) ConfigureCamera(player, spawn);
        }

        /// <summary>Spawn the parallax faerie-forest backdrop (sky gradient, silhouette trees,
        /// drifting fireflies). Parented under the loader so it retires with the level.</summary>
        void BuildScenery()
        {
            var go = new GameObject("Scenery");
            go.transform.SetParent(transform, false);
            go.AddComponent<NixAndEko.Environment.Scenery>();
        }

        /// <summary>Attach the world-map overlay (Select / M / Tab), following the player.</summary>
        void AddMap(PlayerController player)
        {
            var mapGo = new GameObject("MapDisplay");
            mapGo.transform.SetParent(transform, false);
            var map = mapGo.AddComponent<NixAndEko.Environment.MapDisplay>();
            map.level = level;                       // null in scene-first mode; MapDisplay handles that
            map.sceneSpawn = sceneSpawn;
            map.sceneKillY = sceneKillY;
            map.player = player.transform;
            map.input = player.GetComponent<PlayerInputReader>();
            // Second dot for Eko, if the summoner built one under the level root.
            var eko = transform.Find("Eko");
            if (eko != null) map.secondary = eko;
        }

        void AddDebugHud(PlayerController player)
        {
            var hudGo = new GameObject("DebugHUD");
            hudGo.transform.SetParent(transform, false);
            var hud = hudGo.AddComponent<DebugHud>();
            hud.player = player;
            hud.health = player.GetComponent<NixAndEko.Environment.Health>();
            hud.bow = player.GetComponentInChildren<Bow>();
        }

        /// <summary>
        /// Frame the main camera the same way the editor build does: orthographic, following the
        /// archer, with pixel-perfect upscaling when the package is present. Runtime-safe — the
        /// PixelPerfectCamera type is resolved by reflection so there's no hard package dependency.
        /// </summary>
        void ConfigureCamera(PlayerController player, Vector2 spawn)
        {
            var cam = Camera.main;
            if (cam == null) return;

            cam.orthographic = true;
            cam.orthographicSize = 10f;
            cam.backgroundColor = Palette.Black;
            cam.transform.position = new Vector3(spawn.x, spawn.y + 3f, -10f);

            var type = System.Type.GetType("UnityEngine.U2D.PixelPerfectCamera, Unity.2D.PixelPerfect.Runtime")
                       ?? System.Type.GetType("UnityEngine.U2D.PixelPerfectCamera, Unity.RenderPipelines.Universal.Runtime");
            if (type != null && cam.GetComponent(type) == null)
            {
                var ppc = cam.gameObject.AddComponent(type);
                TrySet(ppc, "assetsPPU", SpriteFactory.PPU);
                TrySet(ppc, "refResolutionX", 384);
                TrySet(ppc, "refResolutionY", 216);
                TrySet(ppc, "upscaleRT", true);
                TrySet(ppc, "pixelSnapping", true);
            }

            var follow = cam.GetComponent<CameraFollow>();
            if (follow == null) follow = cam.gameObject.AddComponent<CameraFollow>();
            // Eko is a second PlayerController sharing the scene now, so this has to be explicit —
            // CameraFollow's own "find any PlayerController" fallback can no longer tell them apart.
            if (player != null) follow.target = player.transform;
        }

        static void TrySet(Object target, string field, object value)
        {
            var t = target.GetType();
            var p = t.GetProperty(field, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite) { p.SetValue(target, value); return; }
            var f = t.GetField(field, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) f.SetValue(target, value);
        }
    }
}
