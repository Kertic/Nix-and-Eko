using UnityEngine;

namespace NixAndEko.Player.States
{
    /// <summary>Airborne and descending. Handles landing, coyote/air jumps and wall slides.</summary>
    public class FallState : PlayerStateBase
    {
        public FallState(PlayerController p) : base(p) { }

        public override void Tick(float dt)
        {
            if (P.Grounded)
            {
                P.Machine.ChangeState(Mathf.Abs(In.Move.x) > 0.2f ? (Core.IState)P.MoveS : P.Idle);
                return;
            }

            if (WantsWallSlide()) { P.Machine.ChangeState(P.WallSlide); return; }

            P.FaceMoveInput();
        }

        public override void FixedTick(float fdt)
        {
            float target = Mathf.Abs(In.Move.x) > 0.2f ? Mathf.Sign(In.Move.x) * Cfg.moveSpeed : 0f;
            P.MoveHorizontal(target, Cfg.airAccel);
            P.ApplyGravity(Cfg.gravityDown);
        }
    }
}
