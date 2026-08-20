using NixAndEko.Combat;
using NixAndEko.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NixAndEko.Level
{
    /// <summary>
    /// Builds a <see cref="LevelData"/> asset at runtime, so a level authored in the Level Editor
    /// can be played from an otherwise empty scene (and swapped without re-authoring the scene).
    /// </summary>
    public class LevelLoader : MonoBehaviour
    {
        public LevelData level;
        public PlayerConfig playerConfig;
        public InputActionAsset inputActions;
        [Tooltip("Layer solid geometry is placed on; must match the player's ground mask.")]
        public string groundLayerName = "Ground";
        public bool spawnPlayer = true;

        void Start()
        {
            if (level == null)
            {
                Debug.LogWarning("[LevelLoader] No level assigned.", this);
                return;
            }

            int groundLayer = Mathf.Max(0, LayerMask.NameToLayer(groundLayerName));
            LevelBuilder.Build(level, transform, groundLayer);

            if (!spawnPlayer) return;

            Arrow arrow = PlayerFactory.BuildArrowTemplate(transform);
            PlayerFactory.Build(playerConfig, inputActions, groundLayer, arrow,
                                level.playerSpawn, transform, level.killY);
        }
    }
}
