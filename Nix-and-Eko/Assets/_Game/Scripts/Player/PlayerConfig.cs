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

        [Header("Jump (there's no button-jump — see the derived stats below)")]
        [Tooltip("Gravity while rising.")]
        public float gravityUp = 32f;
        [Tooltip("Gravity while falling (higher = snappier arc).")]
        public float gravityDown = 42f;
        [Tooltip("Maximum downward speed.")]
        public float maxFallSpeed = 16f;
        [Tooltip("Seconds a Jump press is remembered before landing — only used by crouch + " +
                 "jump to drop through one-way platforms, now that there's no button-jump.")]
        public float jumpBuffer = 0.12f;

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
        [Tooltip("Recoil: velocity the player is set to (opposite the shot) at zero draw — a dash-style burst, not an add-on.")]
        public float recoilMin = 4f;
        [Tooltip("Recoil: velocity the player is set to (opposite the shot) at full draw — a dash-style burst, not an add-on.")]
        public float recoilMax = 14f;
        [Tooltip("Extra degrees past a 45° sector boundary the aim must travel before snapping to the next direction (anti-flicker).")]
        [Range(0f, 22f)]
        public float aimHysteresis = 12f;
        [Tooltip("Apply firing recoil while standing on the ground, for downward shots only (S / SW / SE) — that's the \"bow jump\". Sideways/upward ground shots never touch velocity, so running is never interrupted.")]
        public bool recoilWhileGrounded = true;
        [Tooltip("Seconds of steering input lockout after a recoil burst, so held input can't immediately cancel the kick out.")]
        public float recoilInputLock = 0.08f;

        [Header("Aim stick (gamepad)")]
        [Tooltip("How far the right stick must be pushed before the bow starts drawing.")]
        [Range(0.1f, 1f)]
        public float aimStickEngage = 0.6f;
        [Tooltip("The stick has to fall back below this before the shot goes off. Kept well under the engage threshold — a wide gap is a big deadzone against unintentional snapback fires from an imprecise release or stick drift.")]
        [Range(0.05f, 1f)]
        public float aimStickRelease = 0.15f;

        [Header("Health")]
        public int maxHealth = 5;
        [Tooltip("Invulnerability seconds after taking a hit.")]
        public float invulnTime = 0.8f;
        [Tooltip("Knockback speed applied when hurt.")]
        public float hurtKnockback = 12f;
        [Tooltip("How long the player loses control after being hurt.")]
        public float hurtControlLock = 0.25f;

        // ------------------------------------------------------------------ Derived "jump" stats
        // There's no button-jump any more: the closest thing to a jump is a full-charge shot
        // fired straight down (or down-left/down-right), which recoils the player upward at
        // recoilMax. These are read-only — tune recoilMax / gravityUp / gravityDown / moveSpeed
        // instead — and are surfaced in the inspector (see PlayerConfigEditor) so the effect of
        // those tweaks is visible at a glance.

        /// <summary>Initial upward speed of a full-charge downward shot (the "jump" launch speed).</summary>
        public float JumpLaunchSpeed => recoilMax;

        /// <summary>Peak height reached by a full-charge downward shot.</summary>
        public float MaxJumpHeight =>
            (JumpLaunchSpeed * JumpLaunchSpeed) / (2f * Mathf.Max(0.01f, gravityUp));

        /// <summary>
        /// Total airtime of a full-charge downward shot: rising (under <see cref="gravityUp"/>)
        /// plus falling back to the same height (under <see cref="gravityDown"/>, respecting
        /// <see cref="maxFallSpeed"/>).
        /// </summary>
        public float MaxAirTime
        {
            get
            {
                float upTime = JumpLaunchSpeed / Mathf.Max(0.01f, gravityUp);
                return upTime + FallTime(MaxJumpHeight);
            }
        }

        /// <summary>
        /// Farthest flat-ground horizontal distance covered over <see cref="MaxAirTime"/>,
        /// assuming top run speed is already held when the shot goes off.
        /// </summary>
        public float MaxJumpDistance => moveSpeed * MaxAirTime;

        /// <summary>Seconds to fall <paramref name="height"/> under <see cref="gravityDown"/>, respecting <see cref="maxFallSpeed"/>.</summary>
        float FallTime(float height)
        {
            float gd = Mathf.Max(0.01f, gravityDown);
            float vMax = Mathf.Max(0.01f, maxFallSpeed);
            float distAtTerminal = (vMax * vMax) / (2f * gd);   // distance covered while still accelerating

            if (height <= distAtTerminal) return Mathf.Sqrt(2f * height / gd);

            float timeAtTerminal = vMax / gd;
            float remaining = height - distAtTerminal;
            return timeAtTerminal + remaining / vMax;
        }
    }
}
