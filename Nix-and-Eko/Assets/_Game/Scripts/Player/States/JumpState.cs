using UnityEngine;

namespace NixAndEko.Player.States
{
    /// <summary>
    /// Rising portion of a bow-recoil launch (there's no button-jump any more — this is entered
    /// only when <see cref="NixAndEko.Combat.Bow"/> fires a downward shot and kicks the player
    /// upward). Uses the lighter "up" gravity for a floatier rise, then hands off to
    /// <see cref="PlayerController.Fall"/> for the heavier descent once the apex is reached.
    /// </summary>
    public class JumpState : PlayerStateBase
    {
        public JumpState(PlayerController p) : base(p) { }

        public override void Tick(float dt)
        {
            P.FaceMoveInput();

            // Apex reached (or head-bonk) -> fall.
            if (P.Velocity.y <= 0.01f) { P.Machine.ChangeState(P.Fall); return; }
            if (WantsWallSlide()) { P.Machine.ChangeState(P.WallSlide); return; }
        }

        public override void FixedTick(float fdt)
        {
            // No natural air drag: horizontal speed only changes if there's input to steer it,
            // and holding the direction you're already flying never slows you down — only
            // opposing input can.
            if (Mathf.Abs(In.Move.x) > 0.2f)
                P.AccelerateHorizontal(Mathf.Sign(In.Move.x) * Cfg.moveSpeed, Cfg.airAccel);

            P.ApplyGravity(Cfg.gravityUp);
        }
    }
}
