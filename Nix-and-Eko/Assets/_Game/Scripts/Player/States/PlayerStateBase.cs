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

        /// <summary>Try to consume a buffered/coyote jump. Returns true if a jump began.</summary>
        protected bool TryStartGroundJump()
        {
            if (P.BufferedJump && (P.Grounded || P.CanCoyoteJump))
            {
                P.Machine.ChangeState(P.Jump);
                return true;
            }
            return false;
        }

        /// <summary>Try to consume a buffered air jump (double jump). Returns true if a jump began.</summary>
        protected bool TryStartAirJump()
        {
            if (P.BufferedJump && !P.Grounded && !P.CanCoyoteJump &&
                P.AirJumpsUsed < Cfg.airJumps)
            {
                P.AirJumpsUsed++;
                P.Machine.ChangeState(P.Jump);
                return true;
            }
            return false;
        }

        /// <summary>Are we pushing into a wall we're touching, while airborne?</summary>
        protected bool WantsWallSlide()
        {
            return !P.Grounded && P.OnWall && P.Velocity.y < 0.1f &&
                   Mathf.Sign(In.Move.x) == P.WallDir && Mathf.Abs(In.Move.x) > 0.2f;
        }
    }
}
