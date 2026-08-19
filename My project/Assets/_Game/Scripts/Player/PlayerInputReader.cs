using UnityEngine;
using UnityEngine.InputSystem;

namespace NixAndEko.Player
{
    /// <summary>
    /// Thin wrapper over an <see cref="InputActionAsset"/> so the rest of the code never
    /// touches the Input System directly. Point <see cref="actions"/> at the project's
    /// InputSystem_Actions asset (Player map). No generated wrapper class required.
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        [Tooltip("Drag the InputSystem_Actions asset here. Uses the 'Player' action map.")]
        public InputActionAsset actions;

        InputAction _move, _look, _jump, _attack, _crouch, _interact;

        public Vector2 Move { get; private set; }
        /// <summary>Raw aim source (gamepad right stick / look). Zero when unused; controller falls back to mouse.</summary>
        public Vector2 Look { get; private set; }

        public bool JumpPressed { get; private set; }
        public bool JumpHeld { get; private set; }
        public bool JumpReleased { get; private set; }

        public bool AttackPressed { get; private set; }
        public bool AttackHeld { get; private set; }
        public bool AttackReleased { get; private set; }

        public bool CrouchHeld { get; private set; }
        public bool InteractPressed { get; private set; }

        void Awake()
        {
            if (actions == null)
            {
                Debug.LogError("[PlayerInputReader] No InputActionAsset assigned.", this);
                enabled = false;
                return;
            }

            var map = actions.FindActionMap("Player", throwIfNotFound: true);
            _move = map.FindAction("Move");
            _look = map.FindAction("Look");
            _jump = map.FindAction("Jump");
            _attack = map.FindAction("Attack");
            _crouch = map.FindAction("Crouch");
            _interact = map.FindAction("Interact");
        }

        void OnEnable() => actions?.FindActionMap("Player")?.Enable();
        void OnDisable() => actions?.FindActionMap("Player")?.Disable();

        void Update()
        {
            Move = _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
            Look = _look != null ? _look.ReadValue<Vector2>() : Vector2.zero;

            JumpPressed = _jump != null && _jump.WasPressedThisFrame();
            JumpHeld = _jump != null && _jump.IsPressed();
            JumpReleased = _jump != null && _jump.WasReleasedThisFrame();

            AttackPressed = _attack != null && _attack.WasPressedThisFrame();
            AttackHeld = _attack != null && _attack.IsPressed();
            AttackReleased = _attack != null && _attack.WasReleasedThisFrame();

            CrouchHeld = _crouch != null && _crouch.IsPressed();
            InteractPressed = _interact != null && _interact.WasPressedThisFrame();
        }

        /// <summary>Consume a buffered jump press so it isn't re-used by another system this frame.</summary>
        public void ConsumeJump() => JumpPressed = false;
    }
}
