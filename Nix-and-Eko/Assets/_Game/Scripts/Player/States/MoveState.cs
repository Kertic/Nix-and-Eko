using UnityEngine;

namespace NixAndEko.Player.States
{
    /// <summary>Grounded running. Bow drawing (handled by Bow component) slows the player.</summary>
    public class MoveState : PlayerStateBase
    {
        public MoveState(PlayerController p) : base(p) { }

        public override void Tick(float dt)
        {
            if (TryStartGroundJump()) return;

            if (!P.Grounded) { P.Machine.ChangeState(P.Fall); return; }
            if (In.CrouchHeld) { P.Machine.ChangeState(P.Crouch); return; }
            if (Mathf.Abs(In.Move.x) <= 0.2f) { P.Machine.ChangeState(P.Idle); return; }

            P.FaceMoveInput();
        }

        public override void FixedTick(float fdt)
        {
            float speed = Cfg.moveSpeed;
            if (In.AttackHeld) speed *= Cfg.drawMoveMultiplier;

            float target = Mathf.Sign(In.Move.x) * speed;
            bool accelerating = Mathf.Abs(In.Move.x) > 0.2f;
            P.MoveHorizontal(target, accelerating ? Cfg.groundAccel : Cfg.groundDecel);

            if (P.Velocity.y > 0f) P.Velocity = new Vector2(P.Velocity.x, 0f);
        }
    }
}
