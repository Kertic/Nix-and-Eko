using UnityEngine;

namespace NixAndEko.Player.States
{
    /// <summary>Grounded, no meaningful horizontal input.</summary>
    public class IdleState : PlayerStateBase
    {
        public IdleState(PlayerController p) : base(p) { }

        public override void Tick(float dt)
        {
            if (TryStartGroundJump()) return;

            if (!P.Grounded) { P.Machine.ChangeState(P.Fall); return; }
            if (In.CrouchHeld) { P.Machine.ChangeState(P.Crouch); return; }
            if (Mathf.Abs(In.Move.x) > 0.2f) { P.Machine.ChangeState(P.MoveS); return; }
        }

        public override void FixedTick(float fdt)
        {
            P.MoveHorizontal(0f, Cfg.groundDecel);
            // Keep a tiny downward bias so the ground probe stays satisfied on slopes/edges.
            if (P.Velocity.y > 0f) P.Velocity = new Vector2(P.Velocity.x, 0f);
        }
    }
}
