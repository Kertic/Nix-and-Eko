using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Player.States
{
    /// <summary>
    /// Rising portion of a jump — entered either by a direct button-jump
    /// (<see cref="PlayerStateBase.TryButtonJump"/>) or when <see cref="NixAndEko.Combat.Bow"/>
    /// fires a downward shot and kicks the player upward as recoil. Uses the lighter "up" gravity
    /// for a floatier rise, then hands off to <see cref="PlayerController.Fall"/> for the heavier
    /// descent once the apex is reached.
    /// </summary>
    public class JumpState : PlayerStateBase
    {
        public JumpState(PlayerController p) : base(p) { }

        public override void Enter() => Sfx.Play(Sfx.Id.Jump);

        public override void Tick(float dt)
        {
            if (TryMeleeOrRoll()) return;
            P.FaceMoveInput();

            // Apex reached (or head-bonk) -> fall.
            if (P.Velocity.y <= 0.01f) { P.Machine.ChangeState(P.Fall); return; }
            if (WantsWallSlide()) { P.Machine.ChangeState(P.WallSlide); return; }
        }

        public override void FixedTick(float fdt)
        {
            AirHorizontal();
            // Glide only changes gravity on the way down — rising always uses gravityUp.
            P.ApplyGravity(Cfg.gravityUp);
        }
    }
}
