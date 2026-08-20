using System;
using System.Collections.Generic;
using UnityEngine;

namespace NixAndEko.Level
{
    /// <summary>The kinds of block a level can be built from.</summary>
    public enum BlockType
    {
        Ground,
        OneWay,
        MovingPlatform,
        Hazard,
        Checkpoint,
        Gate,
        TargetSwitch,
        BreakableWall,
    }

    /// <summary>One placed block. Only the fields relevant to <see cref="type"/> are used.</summary>
    [Serializable]
    public class LevelBlock
    {
        public BlockType type = BlockType.Ground;
        public Vector2 position;
        public Vector2 size = new Vector2(4f, 1f);

        [Tooltip("MovingPlatform: patrol offset from its start position.")]
        public Vector2 patrolOffset = new Vector2(4f, 0f);
        [Tooltip("MovingPlatform: units per second.")]
        public float speed = 3f;

        [Tooltip("Gate/TargetSwitch: switches drive every gate sharing their link id.")]
        public int linkId;

        [Tooltip("BreakableWall: minimum draw charge (0-1) that can break it.")]
        [Range(0f, 1f)] public float minCharge = 0.5f;

        [Tooltip("Gate: local offset applied when open.")]
        public Vector2 openOffset = new Vector2(0f, 4.5f);

        public LevelBlock Clone() => (LevelBlock)MemberwiseClone();

        /// <summary>Axis-aligned world bounds, used for picking and drawing in the editor.</summary>
        public Rect Rect => new Rect(position - size * 0.5f, size);
    }

    /// <summary>
    /// A level as pure data: a list of blocks plus the player's spawn. The level editor edits
    /// this asset, and <see cref="LevelBuilder"/> turns it into GameObjects — so levels are
    /// diff-friendly data rather than hand-maintained scene YAML.
    /// </summary>
    [CreateAssetMenu(menuName = "Nix & Eko/Level", fileName = "Level")]
    public class LevelData : ScriptableObject
    {
        public Vector2 playerSpawn = new Vector2(-20f, 2f);
        [Tooltip("Falling below this Y respawns the player.")]
        public float killY = -40f;
        public List<LevelBlock> blocks = new List<LevelBlock>();

        /// <summary>Default size for a freshly placed block of the given type.</summary>
        public static Vector2 DefaultSize(BlockType type) => type switch
        {
            BlockType.OneWay => new Vector2(4f, 0.4f),
            BlockType.MovingPlatform => new Vector2(3f, 0.5f),
            BlockType.Hazard => new Vector2(3f, 0.6f),
            BlockType.Checkpoint => new Vector2(1f, 2f),
            BlockType.Gate => new Vector2(1f, 4f),
            BlockType.TargetSwitch => new Vector2(1f, 1f),
            BlockType.BreakableWall => new Vector2(1.2f, 3f),
            _ => new Vector2(4f, 1f),
        };

        /// <summary>Blocks that live on the Ground layer and can be stood on.</summary>
        public static bool IsSolid(BlockType type) =>
            type == BlockType.Ground || type == BlockType.OneWay ||
            type == BlockType.MovingPlatform || type == BlockType.Gate ||
            type == BlockType.BreakableWall;
    }
}
