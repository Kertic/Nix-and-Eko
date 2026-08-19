using UnityEngine;

namespace NixAndEko.Player.States
{
    /// <summary>Brief knockback + loss of control after taking damage.</summary>
    public class HurtState : PlayerStateBase
    {
        int _knockDir = 1;

        public HurtState(PlayerController p) : base(p) { }

        /// <summary>Set before transitioning in: -1 knocks left, +1 knocks right.</summary>
        public void SetKnockback(int dir) => _knockDir = dir == 0 ? 1 : dir;

        public override void Enter()
        {
            P.Velocity = new Vector2(_knockDir * Cfg.hurtKnockback, Cfg.hurtKnockback * 0.5f);
        }

        public override void Tick(float dt)
        {
            if (P.Machine.TimeInState >= Cfg.hurtControlLock)
            {
                P.Machine.ChangeState(P.Grounded ? (Core.IState)P.Idle : P.Fall);
            }
        }

        public override void FixedTick(float fdt)
        {
            P.ApplyGravity(Cfg.gravityDown);
            // light horizontal damping so knockback bleeds off
            P.MoveHorizontal(0f, Cfg.airAccel * 0.5f);
        }
    }
}
