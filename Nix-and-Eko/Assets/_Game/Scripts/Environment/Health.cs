using System;
using NixAndEko.Player;
using UnityEngine;

namespace NixAndEko.Environment
{
    /// <summary>
    /// Player health + respawn. Handles invulnerability windows and routes knockback through
    /// the controller's Hurt state. Checkpoints update <see cref="RespawnPoint"/>.
    /// </summary>
    public class Health : MonoBehaviour
    {
        public PlayerController player;
        public PlayerConfig config;

        [Tooltip("If true, falling below KillY respawns the player (pits).")]
        public bool killBelowY = true;
        public float killY = -30f;

        public int Current { get; private set; }
        public Vector3 RespawnPoint { get; set; }
        public bool Invulnerable => _invulnTimer > 0f;

        /// <summary>(current, max)</summary>
        public event Action<int, int> HealthChanged;
        public event Action Died;

        float _invulnTimer;

        void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (config == null && player != null) config = player.Config;
            RespawnPoint = transform.position;
        }

        void Start()
        {
            // Prefer the player's runtime copy (see PlayerController.Awake) over the shared asset,
            // so editing health values on the player at runtime doesn't touch the defaults.
            if (player != null && player.Config != null) config = player.Config;

            Current = config != null ? config.maxHealth : 5;
            HealthChanged?.Invoke(Current, Max);
        }

        int Max => config != null ? config.maxHealth : 5;

        void Update()
        {
            if (_invulnTimer > 0f) _invulnTimer -= Time.deltaTime;
            if (killBelowY && transform.position.y < killY) Kill();
        }

        /// <summary>Deal damage from a world position (drives knockback direction).</summary>
        public void Damage(int amount, Vector2 sourcePosition)
        {
            if (Invulnerable || Current <= 0) return;

            Current = Mathf.Max(0, Current - amount);
            HealthChanged?.Invoke(Current, Max);

            if (Current <= 0) { Kill(); return; }

            _invulnTimer = config != null ? config.invulnTime : 0.8f;
            if (player != null) player.ReceiveHit(sourcePosition);
        }

        public void Heal(int amount)
        {
            Current = Mathf.Min(Max, Current + amount);
            HealthChanged?.Invoke(Current, Max);
        }

        public void Kill()
        {
            Died?.Invoke();
            Respawn();
        }

        public void Respawn()
        {
            transform.position = RespawnPoint;
            if (player != null) player.Velocity = Vector2.zero;
            Current = Max;
            _invulnTimer = config != null ? config.invulnTime : 0.8f;
            HealthChanged?.Invoke(Current, Max);
            if (player != null) player.Machine.ChangeState(player.Idle);
        }
    }
}
