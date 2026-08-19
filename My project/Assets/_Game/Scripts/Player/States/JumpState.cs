using UnityEngine;

namespace NixAndEko.Player.States
{
    /// <summary>Rising portion of a jump. Supports variable height and air control.</summary>
    public class JumpState : PlayerStateBase
    {
        public JumpState(PlayerController p) : base(p) { }

        public override void Enter()
        {
            P.ConsumeJumpBuffer();
            P.CoyoteTimer = 0f;
            P.Velocity = new Vector2(P.Velocity.x, Cfg.JumpVelocity);
        }

        public override void Tick(float dt)
        {
            if (TryStartAirJump()) return;
            P.FaceMoveInput();

            // Apex reached (or head-bonk) -> fall.
            if (P.Velocity.y <= 0.01f) { P.Machine.ChangeState(P.Fall); return; }
            if (WantsWallSlide()) { P.Machine.ChangeState(P.WallSlide); return; }
        }

        public override void FixedTick(float fdt)
        {
            float target = Mathf.Abs(In.Move.x) > 0.2f ? Mathf.Sign(In.Move.x) * Cfg.moveSpeed : 0f;
            P.MoveHorizontal(target, Cfg.airAccel);

            // Variable jump height: cut the rise short when the button is released.
            float g = In.JumpHeld ? Cfg.gravityUp : Cfg.jumpCutGravity;
            P.ApplyGravity(g);
        }
    }
}
