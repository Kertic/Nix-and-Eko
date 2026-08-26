using NixAndEko.Environment;
using UnityEngine;

namespace NixAndEko.Player.States
{
    /// <summary>Grounded crouch: slow shuffle, and crouch+jump to drop through one-way platforms.</summary>
    public class CrouchState : PlayerStateBase
    {
        public CrouchState(PlayerController p) : base(p) { }

        public override void Tick(float dt)
        {
            if (TryMeleeOrRoll()) return;
            if (!P.Grounded) { P.Machine.ChangeState(P.Fall); return; }

            // Crouch + jump = drop through a one-way platform if standing on one, instead of
            // jumping — a normal button-jump only fires from Idle/Move (see TryButtonJump).
            if (P.BufferedJump && OneWayPlatform.TryDropThrough(P))
            {
                P.ConsumeJumpBuffer();
                P.Machine.ChangeState(P.Fall);
                return;
            }

            if (!In.CrouchHeld)
            {
                P.Machine.ChangeState(Mathf.Abs(In.Move.x) > 0.2f ? (Core.IState)P.MoveS : P.Idle);
                return;
            }

            P.FaceMoveInput();
        }

        public override void FixedTick(float fdt)
        {
            float target = Mathf.Abs(In.Move.x) > 0.2f
                ? Mathf.Sign(In.Move.x) * Cfg.moveSpeed * Cfg.crouchSpeedMultiplier
                : 0f;
            P.MoveHorizontal(target, Cfg.groundAccel);

            // Zero vertical velocity outright — see IdleState's FixedTick for why only clamping
            // upward drift lets a one-way platform's soft landing contact leave a residual
            // downward creep unopposed.
            P.Velocity = new Vector2(P.Velocity.x, 0f);
        }
    }
}
