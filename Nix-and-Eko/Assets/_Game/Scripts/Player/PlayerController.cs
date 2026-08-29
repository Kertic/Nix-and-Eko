using System.Collections.Generic;
using NixAndEko.Core;
using NixAndEko.Environment;
using NixAndEko.Player.States;
using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Player
{
    /// <summary>
    /// Core player brain: owns the Rigidbody, environment sensing (ground / wall),
    /// facing, movement helpers and the locomotion state machine. States live in
    /// NixAndEko.Player.States and drive transitions through this component.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Config & References")]
        public PlayerConfig config;
        public PlayerInputReader input;
        [Tooltip("Child transform holding the sprite; flipped for facing.")]
        public Transform spriteRoot;

        [Header("Sensing")]
        public LayerMask groundMask;
        [Tooltip("Extra distance below the feet used for the ground probe.")]
        public float groundProbe = 0.08f;
        [Tooltip("Width/distance of the side probe used for wall detection.")]
        public float wallProbe = 0.06f;

        [Header("Debug (read-only)")]
        public string currentState;

        // --- Runtime components ---
        public Rigidbody2D Rb { get; private set; }
        public Collider2D Col { get; private set; }
        public PlayerConfig Config => config;
        public PlayerInputReader Input => input;

        /// <summary>Fully suspend this controller — physics off, state machine idle. Nix uses this
        /// while the player is possessing Eko (ghost mode): she stays put wherever she was, doesn't
        /// tick states, doesn't collide with anything, and isn't sensed by anyone's physics queries.
        /// <see cref="ForceGhostPose"/> handles the matching visual (crouch pose + translucency).</summary>
        public bool Frozen { get; private set; }
        /// <summary>Force the sprite to the Crouch pose regardless of the current locomotion state
        /// — used by ghost-mode Nix, who shouldn't run her regular animator while frozen.</summary>
        public bool ForceGhostPose;

        /// <summary>Suspend/resume the state machine and zero velocity. The rigidbody stays
        /// simulated (a planted Eko is still hit by arrows), but is switched to Kinematic while
        /// frozen so an incoming impulse — an arrow crashing into a planted Eko, or an enemy
        /// bumping into it — can't shove it off its held position. For Nix's ghost mode, use
        /// <see cref="SetIntangible"/> alongside this to fully drop out of the simulation.
        /// Reversible; safe to call twice.</summary>
        public void SetFrozen(bool frozen)
        {
            if (Frozen == frozen) return;
            Frozen = frozen;
            if (Rb != null)
            {
                if (frozen)
                {
                    Rb.linearVelocity = Vector2.zero;
                    Rb.angularVelocity = 0f;
                    Rb.bodyType = RigidbodyType2D.Kinematic;   // impulses no longer move it
                }
                else
                {
                    Rb.bodyType = RigidbodyType2D.Dynamic;
                    Rb.linearVelocity = Vector2.zero;   // wake up cleanly, not with any leftover
                }
            }
            if (frozen) Grounded = false;   // sensed value would go stale; keep "no" while suspended
        }

        /// <summary>Take this rigidbody out of the physics simulation entirely (or bring it back).
        /// While intangible, no other body collides with or is sensed by this one — used together
        /// with <see cref="SetFrozen"/> for Nix's ghost mode during an Eko possession.</summary>
        public void SetIntangible(bool intangible)
        {
            if (Rb != null) Rb.simulated = !intangible;
        }

        // --- Sensing state ---
        public bool Grounded { get; private set; }
        public bool WasGrounded { get; private set; }
        /// <summary>-1 = wall on left, +1 = wall on right, 0 = none.</summary>
        public int WallDir { get; private set; }
        public bool OnWall => WallDir != 0;

        /// <summary>Seconds of glide fuel left. Refills to <see cref="PlayerConfig.glideDuration"/>
        /// on landing, drains while actually gliding, and doesn't refill mid-air — once it's
        /// gone, holding the glide trigger does nothing until you touch ground again.</summary>
        public float GlideFuel { get; private set; }

        /// <summary>True while airborne, holding the glide trigger, and there's still fuel left —
        /// momentum is preserved instead of decaying, fall gravity is the lighter glide gravity,
        /// and the glide sprite shows. Runs out mid-air and this goes false on its own, dropping
        /// back to a normal fall even with the trigger still held.</summary>
        public bool IsGliding => !Grounded && Input != null && Input.GlideHeld && GlideFuel > 0f
                                 && PlayerAbilities.Glider;

        // --- Facing ---
        /// <summary>+1 faces right, -1 faces left.</summary>
        public int Facing { get; private set; } = 1;

        // --- Timers shared between states ---
        /// <summary>Buffers a Jump press briefly so it still lands a frame early — consumed by
        /// the grounded button-jump (see <see cref="States.PlayerStateBase.TryButtonJump"/>) and by
        /// crouch + jump dropping through a one-way platform.</summary>
        public float JumpBufferTimer;
        /// <summary>Seconds remaining before horizontal steering input is honored again. Used by
        /// bursts (recoil, dashes) so held input can't immediately cancel the kick out.</summary>
        public float InputLockTimer;
        /// <summary>Seconds after leaving the ground during which the bow still treats a shot as
        /// grounded — the equivalent of jump coyote time, but for firing a "grounded" arrow
        /// instead of jumping.</summary>
        public float CoyoteTimer;
        /// <summary>Seconds spent airborne since last leaving the ground. Drives the non-glide
        /// grace window before natural air deceleration kicks in.</summary>
        public float AirTimer;

        /// <summary>Air jumps Nix has banked. Granted by an Eko-arrow catch (see
        /// <see cref="Combat.EkoArrowTarget"/>); reset to zero whenever she stands on ground.
        /// Consumed one at a time by the Fall/Jump states' <see cref="States.PlayerStateBase.TryAirJump"/>
        /// helper — a jump press cashes one in for a fresh JumpLaunchSpeed burst.</summary>
        public int ExtraJumps;

        // --- States ---
        public IdleState Idle { get; private set; }
        public MoveState MoveS { get; private set; }
        public JumpState Jump { get; private set; }
        public FallState Fall { get; private set; }
        public WallSlideState WallSlide { get; private set; }
        public CrouchState Crouch { get; private set; }
        public HurtState Hurt { get; private set; }
        public MeleeState Melee { get; private set; }
        public RollState Roll { get; private set; }

        /// <summary>The bow — source of truth for whether Nix currently holds her arrow.</summary>
        Combat.Bow _bow;
        /// <summary>Does Nix currently hold her arrow (armed for the melee combo instead of a roll)?</summary>
        public bool HasArrow
        {
            get
            {
                if (_bow == null) _bow = GetComponentInChildren<Combat.Bow>();
                return _bow != null && _bow.HasAnyArrow;
            }
        }
        /// <summary>Which combo swing (0-2) the melee state is currently showing — read by the animator.</summary>
        public int MeleePose;

        /// <summary>Coarse animation state, derived from the current locomotion state.</summary>
        public enum AnimState { Idle, Run, Jump, Fall, Glide, WallSlide, Crouch, Hurt, Melee, Roll }

        /// <summary>What the sprite should currently be showing.</summary>
        public AnimState Anim
        {
            get
            {
                var c = Machine.Current;
                if (c == Hurt) return AnimState.Hurt;
                if (c == Melee) return AnimState.Melee;
                if (c == Roll) return AnimState.Roll;
                if (c == WallSlide) return AnimState.WallSlide;
                if (c == Crouch) return AnimState.Crouch;
                if ((c == Jump || c == Fall) && IsGliding) return AnimState.Glide;
                if (c == Jump) return AnimState.Jump;
                if (c == Fall) return AnimState.Fall;
                if (c == MoveS) return AnimState.Run;
                return AnimState.Idle;
            }
        }

        readonly StateMachine _machine = new StateMachine();
        public StateMachine Machine => _machine;

        public Vector2 Velocity
        {
            get => Rb.linearVelocity;
            set => Rb.linearVelocity = value;
        }

        void Awake()
        {
            Rb = GetComponent<Rigidbody2D>();
            Col = GetComponent<Collider2D>();
            Rb.gravityScale = 0f;                 // gravity handled manually for full control
            Rb.freezeRotation = true;
            Rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            Rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            if (spriteRoot == null) spriteRoot = transform;
            if (input == null) input = GetComponent<PlayerInputReader>();

            // Run from a private copy of the config so tweaking values on the player at runtime
            // (in the inspector during Play) never writes back into the shared asset that holds
            // the defaults. States, the bow and input all read through Config / player.Config,
            // so they pick up this copy too.
            if (config != null)
            {
                string baseName = config.name;
                config = Instantiate(config);
                config.name = baseName + " (runtime copy)";
                if (input != null) input.config = config;
            }

            GlideFuel = config != null ? config.glideDuration : 0f;

            Idle = new IdleState(this);
            MoveS = new MoveState(this);
            Jump = new JumpState(this);
            Fall = new FallState(this);
            WallSlide = new WallSlideState(this);
            Crouch = new CrouchState(this);
            Hurt = new HurtState(this);
            Melee = new MeleeState(this);
            Roll = new RollState(this);

            _bow = GetComponentInChildren<Combat.Bow>();
        }

        void Start() => _machine.Initialize(Idle);

        void Update()
        {
            if (Frozen) { currentState = "Frozen"; return; }
            Sense();
            // Just touched down after a real airborne stretch (AirTimer is reset below in
            // UpdateTimers, so it still holds last frame's airtime here). Skips the spawn frame.
            if (Grounded && !WasGrounded && AirTimer > 0.08f) Sfx.Play(Sfx.Id.Land);
            UpdateTimers(Time.deltaTime);
            _machine.Tick(Time.deltaTime);
            currentState = _machine.Current?.GetType().Name;
        }

        void FixedUpdate()
        {
            if (Frozen) return;
            _machine.FixedTick(Time.fixedDeltaTime);
        }

        // ------------------------------------------------------------------ Sensing
        readonly List<Collider2D> _probeHits = new List<Collider2D>();

        void Sense()
        {
            WasGrounded = Grounded;
            Bounds b = Col.bounds;

            // Ground: thin box just under the feet.
            Vector2 feet = new Vector2(b.center.x, b.min.y);
            Vector2 groundSize = new Vector2(b.size.x * 0.92f, groundProbe * 2f);
            Grounded = ProbeGround(feet, groundSize) && Velocity.y <= 0.01f;

            // Walls: boxes at each side around torso height.
            Vector2 mid = new Vector2(b.center.x, b.center.y);
            Vector2 sideSize = new Vector2(wallProbe * 2f, b.size.y * 0.7f);
            bool wallR = ProbeWall(mid + new Vector2(b.extents.x, 0f), sideSize);
            bool wallL = ProbeWall(mid - new Vector2(b.extents.x, 0f), sideSize);
            WallDir = wallR ? 1 : wallL ? -1 : 0;
        }

        /// <summary>
        /// Ground probe that agrees with what we'll actually collide with. A raw overlap test
        /// ignores <c>Physics2D.IgnoreCollision</c>, so a one-way platform the player is currently
        /// passing through would still register as ground — landing them in mid-air, halfway
        /// inside the plank, because the grounded states then zero out their fall. Filtering by
        /// <see cref="OneWayPlatform.IsSolid"/> keeps sensing and collision telling the same story.
        /// </summary>
        bool ProbeGround(Vector2 center, Vector2 size)
        {
            int n = Physics2D.OverlapBox(center, size, 0f, ProbeFilter(), _probeHits);
            for (int i = 0; i < n; i++)
                if (OneWayPlatform.IsSolid(_probeHits[i], Col)) return true;
            return false;
        }

        /// <summary>
        /// Wall probe. One-way platforms are never walls — there's nothing to cling to on the side
        /// of a plank you're meant to pass straight through, and treating one as a wall is what
        /// makes a jump alongside it snag into a wall-slide.
        /// </summary>
        bool ProbeWall(Vector2 center, Vector2 size)
        {
            int n = Physics2D.OverlapBox(center, size, 0f, ProbeFilter(), _probeHits);
            for (int i = 0; i < n; i++)
                if (!OneWayPlatform.Is(_probeHits[i])) return true;
            return false;
        }

        /// <summary>Rebuilt per probe so a runtime change to <see cref="groundMask"/> is honored.</summary>
        ContactFilter2D ProbeFilter()
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(groundMask);
            return filter;
        }

        void UpdateTimers(float dt)
        {
            if (Input.JumpPressed) JumpBufferTimer = Config.jumpBuffer;
            else JumpBufferTimer -= dt;

            if (InputLockTimer > 0f) InputLockTimer -= dt;

            if (Grounded) CoyoteTimer = Config.coyoteTime;
            else CoyoteTimer -= dt;

            if (Grounded)
            {
                AirTimer = 0f;
                GlideFuel = Config.glideDuration;   // only refills by touching ground
                ExtraJumps = 0;                     // banked air jumps clear on landing
            }
            else
            {
                AirTimer += dt;
                if (IsGliding) GlideFuel = Mathf.Max(0f, GlideFuel - dt);
            }
        }

        // ------------------------------------------------------------------ Helpers used by states
        public bool BufferedJump => JumpBufferTimer > 0f;
        /// <summary>True while actually standing on the ground, or still within the coyote
        /// window after leaving it. Used by <see cref="NixAndEko.Combat.Bow"/> to decide whether
        /// a shot counts as "grounded" (down-only recoil, no airtime ammo spent).</summary>
        public bool GroundedForRecoil => Grounded || CoyoteTimer > 0f;

        public void ConsumeJumpBuffer()
        {
            JumpBufferTimer = 0f;
            Input.ConsumeJump();
        }

        /// <summary>Accelerate horizontal velocity toward <paramref name="targetSpeed"/>.
        /// No-ops while <see cref="InputLockTimer"/> is running, so steering input can't
        /// immediately eat into a burst (recoil, dash, etc.) that just set the velocity.</summary>
        public void MoveHorizontal(float targetSpeed, float accel)
        {
            if (InputLockTimer > 0f) return;

            float v = Velocity.x;
            v = Mathf.MoveTowards(v, targetSpeed, accel * Time.fixedDeltaTime);
            Velocity = new Vector2(v, Velocity.y);
        }

        /// <summary>
        /// Steer horizontal velocity toward <paramref name="targetSpeed"/>, but never slow it
        /// down if it's already going that way at least as fast — only pushes speed up toward
        /// the target, or turns it around if input opposes it. Used in the air so holding the
        /// direction you're already flying (e.g. after a recoil burst or a running jump) can't
        /// cap you back down to normal move speed; only opposing input can actually slow you.
        /// No-ops while <see cref="InputLockTimer"/> is running, same as <see cref="MoveHorizontal"/>.
        /// </summary>
        public void AccelerateHorizontal(float targetSpeed, float accel)
        {
            if (InputLockTimer > 0f) return;

            float v = Velocity.x;
            if (Mathf.Sign(v) == Mathf.Sign(targetSpeed) && Mathf.Abs(v) >= Mathf.Abs(targetSpeed))
                return;

            v = Mathf.MoveTowards(v, targetSpeed, accel * Time.fixedDeltaTime);
            Velocity = new Vector2(v, Velocity.y);
        }

        /// <summary>Suppress horizontal steering input for at least <paramref name="seconds"/>.</summary>
        public void LockInput(float seconds) => InputLockTimer = Mathf.Max(InputLockTimer, seconds);

        /// <summary>Top the glide meter back up without touching the ground — what an Eko arrow
        /// catching Nix does, alongside reloading her air shot.</summary>
        public void RefillGlide() => GlideFuel = Config != null ? Config.glideDuration : GlideFuel;

        /// <summary>Apply manual gravity, clamped to max fall speed.</summary>
        public void ApplyGravity(float gravity)
        {
            float vy = Velocity.y - gravity * Time.fixedDeltaTime;
            vy = Mathf.Max(vy, -Config.maxFallSpeed);
            Velocity = new Vector2(Velocity.x, vy);
        }

        public void SetFacing(int dir)
        {
            if (dir == 0 || dir == Facing) return;
            Facing = dir;
            Vector3 s = spriteRoot.localScale;
            s.x = Mathf.Abs(s.x) * dir;
            spriteRoot.localScale = s;
        }

        public void FaceMoveInput()
        {
            float x = Input.Move.x;
            if (Mathf.Abs(x) > 0.2f) SetFacing(x > 0 ? 1 : -1);
        }

        /// <summary>Called by Health when the player is damaged: enter Hurt with knockback.</summary>
        public void ReceiveHit(Vector2 sourcePosition)
        {
            int dir = transform.position.x < sourcePosition.x ? -1 : 1;
            Hurt.SetKnockback(dir);
            _machine.ChangeState(Hurt);
        }

        void OnDrawGizmosSelected()
        {
            var col = GetComponent<Collider2D>();
            if (col == null) return;
            Bounds b = col.bounds;
            Gizmos.color = Grounded ? Color.green : Color.red;
            Gizmos.DrawWireCube(new Vector3(b.center.x, b.min.y, 0f),
                new Vector3(b.size.x * 0.92f, groundProbe * 2f, 0f));
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(b.center + new Vector3(b.extents.x, 0f), new Vector3(wallProbe * 2f, b.size.y * 0.7f, 0f));
            Gizmos.DrawWireCube(b.center - new Vector3(b.extents.x, 0f), new Vector3(wallProbe * 2f, b.size.y * 0.7f, 0f));
        }
    }
}
