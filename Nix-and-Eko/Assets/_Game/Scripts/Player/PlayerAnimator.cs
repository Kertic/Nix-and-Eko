using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Player
{
    /// <summary>
    /// Frame-based sprite animation driven straight off the locomotion state machine. Kept as
    /// plain code (no Animator asset) so the whole character stays generated and tweakable.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerAnimator : MonoBehaviour
    {
        public PlayerController player;

        [Header("Speeds (frames per second)")]
        public float idleFps = 2.5f;
        public float walkFps = 10f;
        [Tooltip("Scale walk playback with actual speed, so slow shuffles animate slowly.")]
        public bool scaleWalkWithSpeed = true;

        SpriteRenderer _sr;
        PlayerController.AnimState _state;
        Sprite[] _frames;
        float _timer;
        int _index;

        void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (player == null) player = GetComponentInParent<PlayerController>();
            SetClip(PlayerController.AnimState.Idle, force: true);
        }

        void Update()
        {
            if (player == null) return;

            SetClip(player.Anim);
            if (_frames == null || _frames.Length == 0) return;

            float fps = FpsFor(_state);
            if (fps <= 0f || _frames.Length == 1)
            {
                _sr.sprite = _frames[0];
                return;
            }

            _timer += Time.deltaTime * fps;
            while (_timer >= 1f)
            {
                _timer -= 1f;
                _index = (_index + 1) % _frames.Length;
            }
            _sr.sprite = _frames[_index];
        }

        float FpsFor(PlayerController.AnimState state)
        {
            switch (state)
            {
                case PlayerController.AnimState.Idle:
                    return idleFps;
                case PlayerController.AnimState.Run:
                    if (!scaleWalkWithSpeed) return walkFps;
                    float speed01 = player.Config != null && player.Config.moveSpeed > 0.01f
                        ? Mathf.Abs(player.Velocity.x) / player.Config.moveSpeed
                        : 1f;
                    return walkFps * Mathf.Clamp(speed01, 0.35f, 1.2f);
                default:
                    return 0f;   // single-frame poses
            }
        }

        void SetClip(PlayerController.AnimState state, bool force = false)
        {
            if (!force && state == _state) return;

            _state = state;
            _index = 0;
            _timer = 0f;
            _frames = state switch
            {
                PlayerController.AnimState.Run => ArcherSprites.WalkFrames,
                PlayerController.AnimState.Jump => ArcherSprites.JumpFrames,
                PlayerController.AnimState.Fall => ArcherSprites.FallFrames,
                PlayerController.AnimState.Glide => ArcherSprites.GlideFrames,
                PlayerController.AnimState.WallSlide => ArcherSprites.WallSlideFrames,
                PlayerController.AnimState.Crouch => ArcherSprites.CrouchFrames,
                PlayerController.AnimState.Hurt => ArcherSprites.HurtFrames,
                _ => ArcherSprites.IdleFrames,
            };
            if (_frames != null && _frames.Length > 0) _sr.sprite = _frames[0];
        }
    }
}
