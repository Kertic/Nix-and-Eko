using NixAndEko.Environment;
using UnityEngine;

namespace NixAndEko.Player.States
{
    /// <summary>
    /// The unarmed grounded dodge: a dashing roll in the direction Nix is moving (or facing), with
    /// optional i-frames. Steering is locked for its duration so held input can't cancel the dash.
    /// </summary>
    public class RollState : PlayerStateBase
    {
        int _dir = 1;
        Health _health;

        public RollState(PlayerController p) : base(p) { }

        public override void Enter()
        {
            _dir = Mathf.Abs(In.Move.x) > 0.2f ? (int)Mathf.Sign(In.Move.x) : P.Facing;
            P.SetFacing(_dir);

            P.Velocity = new Vector2(_dir * Cfg.rollSpeed, 0f);
            P.LockInput(Cfg.rollDuration);

            if (Cfg.rollInvuln > 0f)
            {
                if (_health == null) _health = P.GetComponent<Health>();
                if (_health != null) _health.GrantInvuln(Cfg.rollInvuln);
            }
        }

        public override void Tick(float dt)
        {
            if (!P.Grounded) { P.Machine.ChangeState(P.Fall); return; }
            if (P.Machine.TimeInState >= Cfg.rollDuration)
                P.Machine.ChangeState(Mathf.Abs(In.Move.x) > 0.2f ? (Core.IState)P.MoveS : P.Idle);
        }

        public override void FixedTick(float fdt)
        {
            P.Velocity = new Vector2(_dir * Cfg.rollSpeed, 0f);
        }
    }
}
