using UnityEngine;

namespace NixAndEko.Player.States
{
    /// <summary>
    /// Clinging to a wall while airborne with a slowed descent. There is no wall-jump — the
    /// player can only slide and then drop or push off into a normal fall.
    /// </summary>
    public class WallSlideState : PlayerStateBase
    {
        public WallSlideState(PlayerController p) : base(p) { }

        public override void Enter()
        {
            P.SetFacing(-P.WallDir); // face away from the wall
        }

        public override void Tick(float dt)
        {
            if (P.Grounded) { P.Machine.ChangeState(P.Idle); return; }
            if (!P.OnWall) { P.Machine.ChangeState(P.Fall); return; }

            // Let go by pushing away from the wall (or releasing input).
            bool pushingAway = Mathf.Abs(In.Move.x) > 0.2f && Mathf.Sign(In.Move.x) == -P.WallDir;
            bool notHolding = Mathf.Abs(In.Move.x) <= 0.2f;
            if (pushingAway || notHolding)
            {
                P.Machine.ChangeState(P.Fall);
                return;
            }
        }

        public override void FixedTick(float fdt)
        {
            float vy = Mathf.Max(P.Velocity.y - Cfg.gravityDown * fdt, -Cfg.wallSlideSpeed);
            P.Velocity = new Vector2(0f, vy);
        }
    }
}
