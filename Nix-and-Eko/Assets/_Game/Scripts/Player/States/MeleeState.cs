using NixAndEko.Combat;
using UnityEngine;

namespace NixAndEko.Player.States
{
    /// <summary>
    /// Nix's 3-hit melee combo with the arrow in hand: a horizontal swipe, a forward thrust, then a
    /// big overhead swing. Each swing has a brief active window that sweeps a hitbox in front of her;
    /// pressing Melee again during (or just after) a swing chains to the next hit, otherwise the
    /// combo ends. Works grounded or airborne.
    /// </summary>
    public class MeleeState : PlayerStateBase
    {
        int _hit;
        float _timer;
        bool _didHit;
        bool _chainQueued;
        bool _lunged;

        public MeleeState(PlayerController p) : base(p) { }

        public override void Enter()
        {
            _hit = 0;
            StartHit();
        }

        void StartHit()
        {
            _timer = 0f;
            _didHit = false;
            _chainQueued = false;
            _lunged = false;
            P.MeleePose = _hit;
            P.FaceMoveInput();
        }

        public override void Tick(float dt)
        {
            _timer += dt;
            float dur = Mathf.Max(0.01f, Cfg.meleeHitDuration);
            float frac = _timer / dur;

            if (!_didHit && frac >= Cfg.meleeActiveStart && frac <= Cfg.meleeActiveEnd)
            {
                DoHitbox();
                _didHit = true;
            }

            // Buffer a chain once the swing is underway.
            if (In.MeleePressed && frac >= Cfg.meleeActiveStart) _chainQueued = true;

            if (_timer >= dur)
            {
                if (_chainQueued && _hit < 2) { _hit++; StartHit(); return; }
                if (_timer >= dur + Cfg.meleeChainWindow) EndCombo();
            }
        }

        void EndCombo()
        {
            if (!P.Grounded) { P.Machine.ChangeState(P.Fall); return; }
            P.Machine.ChangeState(Mathf.Abs(In.Move.x) > 0.2f ? (Core.IState)P.MoveS : P.Idle);
        }

        public override void FixedTick(float fdt)
        {
            // A forward lunge at the start of each swing — the thrust (hit 1) lunges hardest.
            if (!_lunged)
            {
                float lunge = Cfg.meleeLunge * (_hit == 1 ? 1.6f : 1f);
                P.Velocity = new Vector2(P.Facing * lunge, P.Velocity.y);
                _lunged = true;
            }

            if (P.Grounded)
            {
                // Bleed the lunge off so she plants, and never drift downward through the floor.
                float vx = Mathf.MoveTowards(P.Velocity.x, 0f, Cfg.groundDecel * fdt);
                P.Velocity = new Vector2(vx, 0f);
            }
            else
            {
                P.ApplyGravity(Cfg.gravityDown);
            }
        }

        void DoHitbox()
        {
            Vector2 center = (Vector2)P.transform.position + new Vector2(P.Facing * Cfg.meleeRange * 0.5f, 0f);
            Vector2 size = new Vector2(Cfg.meleeRange, Cfg.meleeHeight);

            var hits = Physics2D.OverlapBoxAll(center, size, 0f);
            foreach (var h in hits)
            {
                var enemy = h.GetComponentInParent<EnemyHealth>();
                if (enemy != null) enemy.Damage(Cfg.meleeDamage, P.transform.position);
            }
        }
    }
}
