using UnityEngine;

namespace NixAndEko.Player.States
{
    /// <summary>Airborne and descending. Handles landing, coyote/air jumps and wall slides.</summary>
    public class FallState : PlayerStateBase
    {
        public FallState(PlayerController p) : base(p) { }

        public override void Tick(float dt)
        {
            if (TryMeleeOrRoll()) return;
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
            AirHorizontal();
            // Glide uses its own much lighter gravity while falling — that's what makes it
            // actually glide instead of just falling slower.
            P.ApplyGravity(P.IsGliding ? Cfg.glideGravity : Cfg.gravityDown);
        }
    }
}
