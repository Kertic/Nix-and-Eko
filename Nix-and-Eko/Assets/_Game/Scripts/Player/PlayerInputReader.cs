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
    /// Aiming and firing are decoupled: the right stick (gamepad) or the mouse cursor (KB&amp;M)
    /// only <em>aims</em> — the shot is loosed by a discrete press of the Nix Bow button (R2 / LMB),
    /// never by the stick springing back. There is no draw/charge any more; every shot is full.
    ///
    /// Aim mode is a simple last-used-device heuristic: pushing the stick selects stick aiming
    /// (reticle shows only while it's deflected); moving the mouse selects mouse aiming (reticle
    /// tracks the cursor). <see cref="Combat.Bow"/> reads <see cref="AimStickActive"/> /
    /// <see cref="MouseAiming"/> and resolves the actual direction against the player's centre.
    /// </summary>
    [DefaultExecutionOrder(-100)]   // sample input before anything reads it this frame
    public class PlayerInputReader : MonoBehaviour
    {
        [Tooltip("Drag the InputSystem_Actions asset here. Uses the 'Player' action map.")]
        public InputActionAsset actions;

        [Tooltip("Optional; when set, the aim-stick thresholds below are taken from it.")]
        public PlayerConfig config;

        [Header("Aim stick (gamepad)")]
        [Tooltip("How far the right stick must be pushed before it counts as aiming the bow.")]
        [Range(0.1f, 1f)]
        public float aimStickEngage = 0.6f;
        [Tooltip("The stick must fall back below this before aiming disengages — a little hysteresis so the reticle doesn't flicker at the threshold.")]
        [Range(0.05f, 1f)]
        public float aimStickRelease = 0.35f;

        InputAction _move, _aim, _glide, _eko, _nixBow, _melee, _jump, _crouch, _interact;

        public Vector2 Move { get; private set; }
        /// <summary>Raw right-stick vector, deadzone applied by the Input System.</summary>
        public Vector2 Aim { get; private set; }

        /// <summary>True while the right stick is pushed far enough to count as aiming the bow.</summary>
        public bool AimStickActive { get; private set; }
        /// <summary>Unit direction the right stick is currently pushed (held from the last live frame).</summary>
        public Vector2 AimStickDirection { get; private set; } = Vector2.right;

        /// <summary>True when the mouse is the active aim device (KB&amp;M): the reticle tracks the cursor.</summary>
        public bool MouseAiming { get; private set; }

        public bool JumpPressed { get; private set; }

        /// <summary>The frame the Nix Bow button (R2 / LMB) went down — fires the shot, or sends Eko to fetch when empty.</summary>
        public bool NixBowPressed { get; private set; }
        /// <summary>The frame the Eko button (L1 / Q) went down — plants / prepares / fires / returns the phantom.</summary>
        public bool EkoPressed { get; private set; }
        /// <summary>The frame the Nix Melee button (R1 / RMB) went down — melee combo, or a roll when unarmed.</summary>
        public bool MeleePressed { get; private set; }

        /// <summary>True while the glide trigger (L2) is held.</summary>
        public bool GlideHeld { get; private set; }

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

            if (config != null)
            {
                aimStickEngage = config.aimStickEngage;
                aimStickRelease = config.aimStickRelease;
            }

            var map = actions.FindActionMap("Player", throwIfNotFound: true);
            _move = map.FindAction("Move");
            _aim = map.FindAction("Aim");
            _glide = map.FindAction("Glide");
            _eko = map.FindAction("Eko");
            _nixBow = map.FindAction("NixBow");
            _melee = map.FindAction("Melee");
            _jump = map.FindAction("Jump");
            _crouch = map.FindAction("Crouch");
            _interact = map.FindAction("Interact");
        }

        void OnEnable() => actions?.FindActionMap("Player")?.Enable();

        void OnDisable()
        {
            actions?.FindActionMap("Player")?.Disable();
            AimStickActive = false;
            GlideHeld = false;
            NixBowPressed = EkoPressed = MeleePressed = false;
        }

        void Update()
        {
            Move = _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;

            JumpPressed = _jump != null && _jump.WasPressedThisFrame();

            GlideHeld = _glide != null && _glide.IsPressed();

            NixBowPressed = _nixBow != null && _nixBow.WasPressedThisFrame();
            EkoPressed = _eko != null && _eko.WasPressedThisFrame();
            MeleePressed = _melee != null && _melee.WasPressedThisFrame();

            UpdateAim();

            CrouchHeld = _crouch != null && _crouch.IsPressed();
            InteractPressed = _interact != null && _interact.WasPressedThisFrame();
        }

        /// <summary>
        /// Track the aim stick as a plain engage/disengage with hysteresis, and pick which device
        /// owns aiming this frame. Pushing the stick claims stick aiming; moving the mouse (or a
        /// Nix Bow press with the pointer as the active device) claims mouse aiming.
        /// </summary>
        void UpdateAim()
        {
            Aim = _aim != null ? _aim.ReadValue<Vector2>() : Vector2.zero;

            float mag = Aim.magnitude;
            float release = Mathf.Min(aimStickRelease, aimStickEngage);
            AimStickActive = mag >= (AimStickActive ? release : aimStickEngage);
            if (mag >= aimStickEngage) AimStickDirection = Aim / mag;

            // Last-used-device heuristic: the stick wins while it's deflected; otherwise mouse
            // motion selects (and keeps) mouse aiming so the cursor reticle stays live for KB&M.
            if (AimStickActive) MouseAiming = false;
            else if (Mouse.current != null &&
                     Mouse.current.delta.ReadValue().sqrMagnitude > 0.01f) MouseAiming = true;
        }

        /// <summary>Consume a buffered jump press so it isn't re-used by another system this frame.</summary>
        public void ConsumeJump() => JumpPressed = false;
    }
}
