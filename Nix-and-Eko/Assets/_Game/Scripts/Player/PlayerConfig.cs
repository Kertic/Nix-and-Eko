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
        [Tooltip("Seconds after leaving the ground a shot still counts as grounded (down-only " +
                 "recoil, doesn't spend airtime ammo) — coyote time, but for the bow instead of " +
                 "a jump.")]
        public float coyoteTime = 0.1f;
        [Tooltip("Seconds a Jump press is remembered before landing — only used by crouch + " +
                 "jump to drop through one-way platforms, now that there's no button-jump.")]
        public float jumpBuffer = 0.12f;
        [Tooltip("Seconds after becoming airborne before natural air deceleration kicks in " +
                 "(outside glide) — lets a jump/launch keep its pop for a beat before drag " +
                 "starts pulling horizontal speed back toward moveSpeed.")]
        public float airDragDelay = 0.15f;

        [Header("Glide")]
        [Tooltip("Seconds of glide fuel: how long the glide trigger can hold off air drag and " +
                 "fall gravity before it runs dry and you drop back to normal falling.")]
        public float glideDuration = 1.5f;
        [Tooltip("Gravity while falling with glide held — much lighter than gravityDown so it " +
                 "actually glides instead of just falling slower. Ignored while rising.")]
        public float glideGravity = 8f;

        [Header("Wall")]
        [Tooltip("Downward speed while sliding on a wall (no wall-jump — slide only).")]
        public float wallSlideSpeed = 4f;

        [Header("Bow")]
        [Tooltip("Seconds to fully draw the bow.")]
        public float bowDrawTime = 0.2f;
        [Tooltip("Flat arrow speed for anything less than a full draw — binary, not lerped with charge.")]
        public float arrowMinSpeed = 10f;
        [Tooltip("Arrow speed on a full draw only.")]
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

        // ------------------------------------------------------------------ Jump physics locks
        // For level design you often want to author "this gap needs exactly 6m of jump distance"
        // rather than reverse-engineering it from gravity/recoil numbers. These fields (edited via
        // the lock toggles in PlayerConfigEditor, not directly) pin a target and let
        // ResolveJumpLocks() solve the one physics variable that keeps the formula true whenever
        // anything else changes. Hidden from the default inspector — PlayerConfigEditor draws them
        // next to the derived stat they govern instead.
        [HideInInspector] public bool lockJumpHeight;
        [HideInInspector] public float targetJumpHeight = 3f;
        [HideInInspector] public bool lockAirTime;
        [HideInInspector] public float targetAirTime = 0.6f;
        [HideInInspector] public bool lockJumpDistance;
        [HideInInspector] public float targetJumpDistance = 6f;

        /// <summary>
        /// Re-solve whichever physics variable each active lock governs, so its target keeps
        /// holding after some other field changed:
        /// <list type="bullet">
        /// <item><see cref="lockJumpHeight"/> solves <see cref="gravityUp"/>.</item>
        /// <item><see cref="lockAirTime"/> solves <see cref="gravityDown"/>.</item>
        /// <item><see cref="lockJumpDistance"/> solves <see cref="moveSpeed"/>.</item>
        /// </list>
        /// Each lock owns a different variable, so all three can be active at once without
        /// fighting each other. Order matters: distance depends on air time, which depends on
        /// height, so they resolve in that order — height, then air time, then distance.
        /// </summary>
        public void ResolveJumpLocks()
        {
            if (lockJumpHeight)
                gravityUp = SolveGravityForHeight(JumpLaunchSpeed, targetJumpHeight);

            if (lockAirTime)
            {
                float upTime = JumpLaunchSpeed / Mathf.Max(0.01f, gravityUp);
                float downTimeTarget = targetAirTime - upTime;
                gravityDown = SolveGravityForFallTime(MaxJumpHeight, downTimeTarget, maxFallSpeed);
            }

            if (lockJumpDistance)
                moveSpeed = targetJumpDistance / Mathf.Max(0.01f, MaxAirTime);
        }

        /// <summary>Gravity that gives a launch of <paramref name="launchSpeed"/> a peak height of exactly <paramref name="targetHeight"/> (H = v²/2g ⟹ g = v²/2H).</summary>
        public static float SolveGravityForHeight(float launchSpeed, float targetHeight)
        {
            float h = Mathf.Max(0.01f, targetHeight);
            return (launchSpeed * launchSpeed) / (2f * h);
        }

        /// <summary>
        /// Gravity that makes a fall of <paramref name="height"/> take exactly
        /// <paramref name="targetTime"/> seconds, respecting <paramref name="maxFallSpeed"/>.
        /// <see cref="FallTime"/> is piecewise (it clamps at terminal velocity) but monotonically
        /// decreasing in gravity, so this bisects instead of inverting each case by hand — no
        /// fall can ever be faster than covering the whole height at <paramref name="maxFallSpeed"/>,
        /// so a target below that floor is clamped up to it.
        /// </summary>
        public static float SolveGravityForFallTime(float height, float targetTime, float maxFallSpeed)
        {
            float h = Mathf.Max(0.001f, height);
            float vMax = Mathf.Max(0.01f, maxFallSpeed);

            float floor = h / vMax;   // fastest possible: falling the whole way at max speed
            float t = Mathf.Max(targetTime, floor * 1.0001f);

            float lo = 0.01f, hi = 100000f;
            for (int i = 0; i < 60; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (FallTime(h, mid, vMax) > t) lo = mid; else hi = mid;
            }
            return (lo + hi) * 0.5f;
        }

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
                return upTime + FallTime(MaxJumpHeight, gravityDown, maxFallSpeed);
            }
        }

        /// <summary>
        /// Farthest flat-ground horizontal distance covered over <see cref="MaxAirTime"/>,
        /// assuming top run speed is already held when the shot goes off.
        /// </summary>
        public float MaxJumpDistance => moveSpeed * MaxAirTime;

        /// <summary>
        /// Seconds to fall <paramref name="height"/> under <paramref name="gravity"/>, respecting
        /// <paramref name="maxFallSpeed"/>. Static (rather than reading gravityDown/maxFallSpeed
        /// off this instance) so <see cref="SolveGravityForFallTime"/> can probe it against
        /// candidate gravities without a live PlayerConfig to mutate.
        /// </summary>
        public static float FallTime(float height, float gravity, float maxFallSpeed)
        {
            float gd = Mathf.Max(0.01f, gravity);
            float vMax = Mathf.Max(0.01f, maxFallSpeed);
            float distAtTerminal = (vMax * vMax) / (2f * gd);   // distance covered while still accelerating

            if (height <= distAtTerminal) return Mathf.Sqrt(2f * height / gd);

            float timeAtTerminal = vMax / gd;
            float remaining = height - distAtTerminal;
            return timeAtTerminal + remaining / vMax;
        }
    }
}
