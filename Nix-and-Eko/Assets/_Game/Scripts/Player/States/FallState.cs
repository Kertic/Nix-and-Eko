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
            // No natural air drag: horizontal speed only changes if there's input to steer it,
            // and holding the direction you're already flying never slows you down — only
            // opposing input can. Letting go (or holding forward) keeps whatever speed you were
            // carrying, so a running jump stays a long jump instead of bleeding off on its own.
            if (Mathf.Abs(In.Move.x) > 0.2f)
                P.AccelerateHorizontal(Mathf.Sign(In.Move.x) * Cfg.moveSpeed, Cfg.airAccel);

            P.ApplyGravity(Cfg.gravityDown);
        }
    }
}
