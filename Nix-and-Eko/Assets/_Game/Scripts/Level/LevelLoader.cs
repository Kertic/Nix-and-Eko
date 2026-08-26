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

        void Start()
        {
            if (level == null)
            {
                Debug.LogWarning("[LevelLoader] No level assigned.", this);
                return;
            }

            int groundLayer = Mathf.Max(0, LayerMask.NameToLayer(groundLayerName));
            LevelBuilder.Build(level, transform, groundLayer);

            PlayerController player = null;
            if (spawnPlayer)
            {
                Arrow arrow = PlayerFactory.BuildArrowTemplate(transform);
                player = PlayerFactory.Build(playerConfig, inputActions, groundLayer,
                    arrow, level.playerSpawn, transform, level.killY);

                if (spawnDebugHud) AddDebugHud(player);
            }

            // Eko exists as a second PlayerController now (see PlayerFactory), so the camera needs
            // to be told explicitly who to follow rather than finding "the" PlayerController.
            if (configureCamera) ConfigureCamera(player);
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
        void ConfigureCamera(PlayerController player)
        {
            var cam = Camera.main;
            if (cam == null) return;

            cam.orthographic = true;
            cam.orthographicSize = 10f;
            cam.backgroundColor = Palette.Black;
            cam.transform.position = new Vector3(level.playerSpawn.x, level.playerSpawn.y + 3f, -10f);

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
