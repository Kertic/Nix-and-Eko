using UnityEngine;

namespace NixAndEko.Player
{
    /// <summary>
    /// Tunable movement / combat parameters for the player.
    /// Kept in a ScriptableObject so designers can tweak feel without touching prefabs
    /// (and so multiple characters can share or swap profiles).
    /// </summary>
    [CreateAssetMenu(menuName = "Nix & Eko/Player Config", fileName = "PlayerConfig")]
    public class PlayerConfig : ScriptableObject
    {
        [Header("Run")]
        [Tooltip("Top horizontal speed (units/sec).")]
        public float moveSpeed = 8f;
        [Tooltip("How fast we reach top speed on the ground.")]
        public float groundAccel = 90f;
        [Tooltip("How fast we stop on the ground.")]
        public float groundDecel = 100f;
        [Tooltip("How fast we accelerate/decelerate in the air.")]
        public float airAccel = 45f;
        [Range(0f, 1f)]
        [Tooltip("Horizontal speed multiplier while crouching.")]
        public float crouchSpeedMultiplier = 0.45f;

        [Header("Jump")]
        [Tooltip("Peak jump height in units (used to derive jump velocity).")]
        public float jumpHeight = 3.2f;
        [Tooltip("Gravity while rising.")]
        public float gravityUp = 32f;
        [Tooltip("Gravity while falling (higher = snappier arc).")]
        public float gravityDown = 42f;
        [Tooltip("Extra gravity applied when jump is released early (variable jump height).")]
        public float jumpCutGravity = 80f;
        [Tooltip("Maximum downward speed.")]
        public float maxFallSpeed = 16f;
        [Tooltip("Seconds after leaving a ledge you can still jump.")]
        public float coyoteTime = 0.1f;
        [Tooltip("Seconds a jump press is remembered before landing.")]
        public float jumpBuffer = 0.12f;
        [Tooltip("Number of mid-air jumps (0 = only ground jump).")]
        public int airJumps = 0;

        [Header("Wall")]
        [Tooltip("Downward speed while sliding on a wall (no wall-jump — slide only).")]
        public float wallSlideSpeed = 4f;

        [Header("Bow")]
        [Tooltip("Seconds to fully draw the bow.")]
        public float bowDrawTime = 0.2f;
        [Tooltip("Arrow speed at zero draw.")]
        public float arrowMinSpeed = 10f;
        [Tooltip("Arrow speed at full draw.")]
        public float arrowMaxSpeed = 34f;
        [Tooltip("Horizontal move speed multiplier while drawing the bow.")]
        [Range(0f, 1f)]
        public float drawMoveMultiplier = 0.55f;
        [Tooltip("Recoil speed applied opposite the shot at zero draw.")]
        public float recoilMin = 4f;
        [Tooltip("Recoil speed applied opposite the shot at full draw.")]
        public float recoilMax = 14f;
        [Tooltip("Extra degrees past a 45° sector boundary the aim must travel before snapping to the next direction (anti-flicker).")]
        [Range(0f, 22f)]
        public float aimHysteresis = 12f;
        [Tooltip("Apply firing recoil while standing on the ground. Off = recoil only kicks in mid-air, so ground shots don't shove you around.")]
        public bool recoilWhileGrounded = false;

        [Header("Aim stick (gamepad)")]
        [Tooltip("How far the right stick must be pushed before the bow starts drawing.")]
        [Range(0.1f, 1f)]
        public float aimStickEngage = 0.5f;
        [Tooltip("The stick has to fall back below this before the shot goes off. Kept under the engage threshold so a stick held near the edge can't chatter.")]
        [Range(0.05f, 1f)]
        public float aimStickRelease = 0.3f;

        [Header("Health")]
        public int maxHealth = 5;
        [Tooltip("Invulnerability seconds after taking a hit.")]
        public float invulnTime = 0.8f;
        [Tooltip("Knockback speed applied when hurt.")]
        public float hurtKnockback = 12f;
        [Tooltip("How long the player loses control after being hurt.")]
        public float hurtControlLock = 0.25f;

        /// <summary>Initial upward velocity that reaches <see cref="jumpHeight"/> under <see cref="gravityUp"/>.</summary>
        public float JumpVelocity => Mathf.Sqrt(2f * gravityUp * Mathf.Max(0.01f, jumpHeight));
    }
}
