using UnityEngine;

namespace NixAndEko.Player.States
{
    /// <summary>Grounded running.</summary>
    public class MoveState : PlayerStateBase
    {
        public MoveState(PlayerController p) : base(p) { }

        public override void Tick(float dt)
        {
            if (TryMeleeOrRoll()) return;
            if (!P.Grounded) { P.Machine.ChangeState(P.Fall); return; }
            if (In.CrouchHeld) { P.Machine.ChangeState(P.Crouch); return; }
            if (Mathf.Abs(In.Move.x) <= 0.2f) { P.Machine.ChangeState(P.Idle); return; }

            P.FaceMoveInput();
        }

        public override void FixedTick(float fdt)
        {
            float target = Mathf.Sign(In.Move.x) * Cfg.moveSpeed;
            bool accelerating = Mathf.Abs(In.Move.x) > 0.2f;
            P.MoveHorizontal(target, accelerating ? Cfg.groundAccel : Cfg.groundDecel);

            // Zero vertical velocity outright — see IdleState's FixedTick for why only clamping
            // upward drift lets a one-way platform's soft landing contact leave a residual
            // downward creep unopposed.
            P.Velocity = new Vector2(P.Velocity.x, 0f);
        }
    }
}
