using UnityEngine;
using UnityEngine.InputSystem;

namespace NixAndEko.Player
{
    /// <summary>
    /// Thin wrapper over an <see cref="InputActionAsset"/> so the rest of the code never
    /// touches the Input System directly. Point <see cref="actions"/> at the project's
    /// InputSystem_Actions asset (Player map). No generated wrapper class required.
    ///
    /// Keyboard/mouse and gamepad are both live at once; nothing has to be switched between.
    /// On a gamepad the right stick doubles as the bow's trigger — pushing it out starts the
    /// draw, letting it spring back fires — so <see cref="AttackHeld"/> and friends report the
    /// union of the Attack button, that stick gesture, and the aim-hold trigger.
    ///
    /// Holding the aim-hold button (R1) keeps the draw alive after the stick springs back, so
    /// the aim stays put and the shot only looses when it's released — the same way holding the
    /// Attack button (Square) already keeps a draw held.
    /// </summary>
    [DefaultExecutionOrder(-100)]   // sample input before anything reads it this frame
    public class PlayerInputReader : MonoBehaviour
    {
        [Tooltip("Drag the InputSystem_Actions asset here. Uses the 'Player' action map.")]
        public InputActionAsset actions;

        [Tooltip("Optional; when set, the aim-stick thresholds below are taken from it.")]
        public PlayerConfig config;

        [Header("Aim stick (gamepad)")]
        [Tooltip("How far the right stick must be pushed before the bow starts drawing.")]
        [Range(0.1f, 1f)]
        public float aimStickEngage = 0.6f;
        [Tooltip("The stick has to fall back below this before the shot goes off. Kept well under the engage threshold — a wide gap is a big deadzone against unintentional snapback fires from an imprecise release or stick drift.")]
        [Range(0.05f, 1f)]
        public float aimStickRelease = 0.15f;

        InputAction _move, _look, _aim, _aimHold, _glide, _eko, _jump, _attack, _crouch, _interact;

        public Vector2 Move { get; private set; }
        /// <summary>Right-stick / look vector. Unused by the bow (which reads <see cref="AimStickDirection"/>); kept for future use.</summary>
        public Vector2 Look { get; private set; }
        /// <summary>Raw right-stick vector, deadzone applied by the Input System.</summary>
        public Vector2 Aim { get; private set; }

        /// <summary>True while the right stick is pushed far enough to count as aiming the bow.</summary>
        public bool AimStickActive { get; private set; }
        /// <summary>
        /// The last direction the right stick was pushed, held on to after it springs back to
        /// centre — so pushing straight down and letting go fires straight down, rather than
        /// wherever the stick happened to pass through on its way home.
        /// </summary>
        public Vector2 AimStickDirection { get; private set; } = Vector2.right;

        public bool JumpPressed { get; private set; }
        public bool JumpHeld { get; private set; }
        public bool JumpReleased { get; private set; }

        public bool AttackPressed { get; private set; }
        public bool AttackHeld { get; private set; }
        public bool AttackReleased { get; private set; }

        /// <summary>True while the aim-hold button (R1) is held, pinning the current draw.</summary>
        public bool AimHoldHeld { get; private set; }
        /// <summary>True while the glide trigger (L2) is held — airborne, this keeps momentum
        /// instead of letting it bleed off on its own.</summary>
        public bool GlideHeld { get; private set; }

        /// <summary>The frame the Eko summon button (R2) went down — plants the phantom.</summary>
        public bool EkoPressed { get; private set; }
        /// <summary>True while the Eko summon button (R2) is held; releasing it looses Eko's shot.</summary>
        public bool EkoHeld { get; private set; }

        public bool CrouchHeld { get; private set; }
        public bool InteractPressed { get; private set; }

        bool _attackHeldLast;

        void Awake()
        {
            if (actions == null)
            {
                Debug.LogError("[PlayerInputReader] No InputActionAsset assigned.", this);
                enabled = false;
                return;
            }

            if (config != null)
            {
                aimStickEngage = config.aimStickEngage;
                aimStickRelease = config.aimStickRelease;
            }

            var map = actions.FindActionMap("Player", throwIfNotFound: true);
            _move = map.FindAction("Move");
            _look = map.FindAction("Look");
            _aim = map.FindAction("Aim");
            _aimHold = map.FindAction("AimHold");
            _glide = map.FindAction("Glide");
            _eko = map.FindAction("Eko");
            _jump = map.FindAction("Jump");
            _attack = map.FindAction("Attack");
            _crouch = map.FindAction("Crouch");
            _interact = map.FindAction("Interact");
        }

        void OnEnable() => actions?.FindActionMap("Player")?.Enable();

        void OnDisable()
        {
            actions?.FindActionMap("Player")?.Disable();
            // Don't leave a half-finished gesture latched across a disable.
            AimStickActive = false;
            AimHoldHeld = false;
            GlideHeld = false;
            EkoHeld = false;
            EkoPressed = false;
            _attackHeldLast = false;
        }

        void Update()
        {
            Move = _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
            Look = _look != null ? _look.ReadValue<Vector2>() : Vector2.zero;

            JumpPressed = _jump != null && _jump.WasPressedThisFrame();
            JumpHeld = _jump != null && _jump.IsPressed();
            JumpReleased = _jump != null && _jump.WasReleasedThisFrame();

            AimHoldHeld = _aimHold != null && _aimHold.IsPressed();
            GlideHeld = _glide != null && _glide.IsPressed();

            EkoPressed = _eko != null && _eko.WasPressedThisFrame();
            EkoHeld = _eko != null && _eko.IsPressed();

            UpdateAimStick();
            UpdateAttack();

            CrouchHeld = _crouch != null && _crouch.IsPressed();
            InteractPressed = _interact != null && _interact.WasPressedThisFrame();
        }

        /// <summary>
        /// Track the right stick as a hold-and-release gesture: it engages once pushed past
        /// <see cref="aimStickEngage"/> and stays engaged until it falls under
        /// <see cref="aimStickRelease"/>, and its direction is only sampled while it's clearly
        /// deflected so the springback can't drag the aim with it.
        /// </summary>
        void UpdateAimStick()
        {
            Aim = _aim != null ? _aim.ReadValue<Vector2>() : Vector2.zero;

            float mag = Aim.magnitude;
            float release = Mathf.Min(aimStickRelease, aimStickEngage);
            AimStickActive = mag >= (AimStickActive ? release : aimStickEngage);

            if (mag >= aimStickEngage) AimStickDirection = Aim / mag;
        }

        /// <summary>
        /// Attack is the Attack button OR the aim-stick gesture OR the aim-hold trigger, whichever
        /// is active. The aim-hold trigger keeps the draw alive on its own, so a shot lined up with
        /// the stick stays drawn after the stick springs back and fires when the trigger releases.
        /// </summary>
        void UpdateAttack()
        {
            bool held = (_attack != null && _attack.IsPressed()) || AimStickActive || AimHoldHeld;

            AttackPressed = held && !_attackHeldLast;
            AttackReleased = !held && _attackHeldLast;
            AttackHeld = held;
            _attackHeldLast = held;
        }

        /// <summary>Consume a buffered jump press so it isn't re-used by another system this frame.</summary>
        public void ConsumeJump() => JumpPressed = false;
    }
}
