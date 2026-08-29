using NixAndEko.Core;
using UnityEngine;

namespace NixAndEko.Player.States
{
    /// <summary>Shared base for player states: caches the controller and common shortcuts.</summary>
    public abstract class PlayerStateBase : IState
    {
        protected readonly PlayerController P;
        protected PlayerConfig Cfg => P.Config;
        protected PlayerInputReader In => P.Input;

        protected PlayerStateBase(PlayerController player) => P = player;

        public virtual void Enter() { }
        public virtual void Exit() { }
        public virtual void Tick(float deltaTime) { }
        public virtual void FixedTick(float fixedDeltaTime) { }

        // --- Transition helpers shared across states ---

        /// <summary>
        /// Nix Melee (R1 / RMB): with an arrow in hand it starts the melee combo; unarmed and
        /// grounded it starts a roll (airborne + unarmed does nothing yet). No-ops during an input
        /// lock (e.g. a recoil burst) so it can't cancel a launch. Returns true if it changed state.
        /// </summary>
        protected bool TryMeleeOrRoll()
        {
            if (!In.MeleePressed || P.InputLockTimer > 0f) return false;

            if (P.HasArrow) { P.Machine.ChangeState(P.Melee); return true; }
            if (P.Grounded) { P.Machine.ChangeState(P.Roll); return true; }
            return false;
        }

        /// <summary>
        /// Grounded button-jump (X / Space) — launches straight up at <see cref="PlayerConfig.JumpLaunchSpeed"/>,
        /// the same launch speed as the bow-recoil "jump" so the two feel identical. Buffered briefly
        /// (see <see cref="PlayerController.JumpBufferTimer"/>) so a press that lands a frame early
        /// still counts. No-ops during an input lock so a burst can't be cancelled by a stray buffered
        /// press. Returns true if it changed state.
        /// </summary>
        protected bool TryButtonJump()
        {
            if (!P.Grounded || !P.BufferedJump || P.InputLockTimer > 0f) return false;

            P.ConsumeJumpBuffer();
            P.Velocity = new Vector2(P.Velocity.x, Cfg.JumpLaunchSpeed);
            P.Machine.ChangeState(P.Jump);
            return true;
        }

        /// <summary>
        /// Airborne button-jump backed by <see cref="PlayerController.ExtraJumps"/> — a banked
        /// jump the Eko-arrow catch (see <see cref="Combat.EkoArrowTarget"/>) grants Nix. Consumes
        /// one bank on each successful use, launches at <see cref="PlayerConfig.JumpLaunchSpeed"/>,
        /// and re-enters <see cref="PlayerController.Jump"/> so the rise reads as a fresh jump.
        /// No-ops during an input lock so a recoil burst can't be cancelled by a stray buffered
        /// press. Returns true if it changed state.
        /// </summary>
        protected bool TryAirJump()
        {
            if (!P.BufferedJump || P.ExtraJumps <= 0 || P.InputLockTimer > 0f) return false;

            P.ExtraJumps--;
            P.ConsumeJumpBuffer();
            P.Velocity = new Vector2(P.Velocity.x, Cfg.JumpLaunchSpeed);
            P.Machine.ChangeState(P.Jump);
            return true;
        }

        /// <summary>Are we pushing into a wall we're touching, while airborne?</summary>
        protected bool WantsWallSlide()
        {
            return !P.Grounded && P.OnWall && P.Velocity.y < 0.1f &&
                   Mathf.Sign(In.Move.x) == P.WallDir && Mathf.Abs(In.Move.x) > 0.2f;
        }

        /// <summary>
        /// Air steering shared by Jump/Fall: while gliding, or still within the post-launch
        /// <see cref="PlayerConfig.airDragDelay"/> grace window, momentum is preserved — input
        /// only pushes speed up toward moveSpeed, never decays it, so releasing (or holding
        /// forward) can't slow you down. Outside both of those it's normal air control: no input
        /// decelerates back to a stop, same as on the ground.
        /// </summary>
        protected void AirHorizontal()
        {
            bool hasInput = Mathf.Abs(In.Move.x) > 0.2f;
            bool preserveMomentum = P.IsGliding || P.AirTimer < Cfg.airDragDelay;

            if (preserveMomentum)
            {
                if (hasInput) P.AccelerateHorizontal(Mathf.Sign(In.Move.x) * Cfg.moveSpeed, Cfg.airAccel);
            }
            else
            {
                P.MoveHorizontal(hasInput ? Mathf.Sign(In.Move.x) * Cfg.moveSpeed : 0f, Cfg.airAccel);
            }
        }
    }
}
