using UnityEngine;

namespace NixAndEko.Player.States
{
    /// <summary>Grounded, no meaningful horizontal input.</summary>
    public class IdleState : PlayerStateBase
    {
        public IdleState(PlayerController p) : base(p) { }

        public override void Tick(float dt)
        {
            if (TryMeleeOrRoll()) return;
            if (!P.Grounded) { P.Machine.ChangeState(P.Fall); return; }
            if (In.CrouchHeld) { P.Machine.ChangeState(P.Crouch); return; }
            if (Mathf.Abs(In.Move.x) > 0.2f) { P.Machine.ChangeState(P.MoveS); return; }
        }

        public override void FixedTick(float fdt)
        {
            P.MoveHorizontal(0f, Cfg.groundDecel);
            // Zero vertical velocity outright rather than only clamping upward drift. Grounded
            // states never re-apply gravity, so any small downward residual left over from
            // landing — a one-way platform's effector resolves the landing contact more softly
            // than solid ground's plain collider, and can leave a sliver of it uncancelled — would
            // otherwise persist untouched forever, reading as a slow, endless slide through the
            // platform. The ground probe is a pure position check, so this can't cost us contact.
            P.Velocity = new Vector2(P.Velocity.x, 0f);
        }
    }
}
