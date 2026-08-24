using NixAndEko.Core;
using NixAndEko.Player.States;
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

        // --- Sensing state ---
        public bool Grounded { get; private set; }
        public bool WasGrounded { get; private set; }
        /// <summary>-1 = wall on left, +1 = wall on right, 0 = none.</summary>
        public int WallDir { get; private set; }
        public bool OnWall => WallDir != 0;

        // --- Facing ---
        /// <summary>+1 faces right, -1 faces left.</summary>
        public int Facing { get; private set; } = 1;

        // --- Timers / counters shared between states ---
        public float CoyoteTimer;
        public float JumpBufferTimer;
        public int AirJumpsUsed;

        // --- States ---
        public IdleState Idle { get; private set; }
        public MoveState MoveS { get; private set; }
        public JumpState Jump { get; private set; }
        public FallState Fall { get; private set; }
        public WallSlideState WallSlide { get; private set; }
        public CrouchState Crouch { get; private set; }
        public HurtState Hurt { get; private set; }

        /// <summary>Coarse animation state, derived from the current locomotion state.</summary>
        public enum AnimState { Idle, Run, Jump, Fall, WallSlide, Crouch, Hurt }

        /// <summary>What the sprite should currently be showing.</summary>
        public AnimState Anim
        {
            get
            {
                var c = Machine.Current;
                if (c == Hurt) return AnimState.Hurt;
                if (c == WallSlide) return AnimState.WallSlide;
                if (c == Crouch) return AnimState.Crouch;
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

            Idle = new IdleState(this);
            MoveS = new MoveState(this);
            Jump = new JumpState(this);
            Fall = new FallState(this);
            WallSlide = new WallSlideState(this);
            Crouch = new CrouchState(this);
            Hurt = new HurtState(this);
        }

        void Start() => _machine.Initialize(Idle);

        void Update()
        {
            Sense();
            UpdateTimers(Time.deltaTime);
            _machine.Tick(Time.deltaTime);
            currentState = _machine.Current?.GetType().Name;
        }

        void FixedUpdate() => _machine.FixedTick(Time.fixedDeltaTime);

        // ------------------------------------------------------------------ Sensing
        void Sense()
        {
            WasGrounded = Grounded;
            Bounds b = Col.bounds;

            // Ground: thin box just under the feet.
            Vector2 feet = new Vector2(b.center.x, b.min.y);
            Vector2 groundSize = new Vector2(b.size.x * 0.92f, groundProbe * 2f);
            Grounded = Physics2D.OverlapBox(feet, groundSize, 0f, groundMask) &&
                       Velocity.y <= 0.01f;

            // Walls: boxes at each side around torso height.
            Vector2 mid = new Vector2(b.center.x, b.center.y);
            Vector2 sideSize = new Vector2(wallProbe * 2f, b.size.y * 0.7f);
            bool wallR = Physics2D.OverlapBox(mid + new Vector2(b.extents.x, 0f), sideSize, 0f, groundMask);
            bool wallL = Physics2D.OverlapBox(mid - new Vector2(b.extents.x, 0f), sideSize, 0f, groundMask);
            WallDir = wallR ? 1 : wallL ? -1 : 0;
        }

        void UpdateTimers(float dt)
        {
            if (Grounded)
            {
                CoyoteTimer = Config.coyoteTime;
                AirJumpsUsed = 0;
            }
            else CoyoteTimer -= dt;

            if (Input.JumpPressed) JumpBufferTimer = Config.jumpBuffer;
            else JumpBufferTimer -= dt;

        }

        // ------------------------------------------------------------------ Helpers used by states
        public bool BufferedJump => JumpBufferTimer > 0f;
        public bool CanCoyoteJump => CoyoteTimer > 0f;

        public void ConsumeJumpBuffer()
        {
            JumpBufferTimer = 0f;
            Input.ConsumeJump();
        }

        /// <summary>Accelerate horizontal velocity toward <paramref name="targetSpeed"/>.</summary>
        public void MoveHorizontal(float targetSpeed, float accel)
        {
            float v = Velocity.x;
            v = Mathf.MoveTowards(v, targetSpeed, accel * Time.fixedDeltaTime);
            Velocity = new Vector2(v, Velocity.y);
        }

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
