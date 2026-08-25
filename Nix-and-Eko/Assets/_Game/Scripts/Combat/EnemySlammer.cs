using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// A primitive ground-pounder: it crouches (telegraph), springs up, hangs at the top for a
    /// beat, then slams straight back down to where it started and pauses before repeating. Moves by
    /// script on a simple timed cycle; the sprite swaps between idle / wind-up / slam poses.
    /// </summary>
    public class EnemySlammer : MonoBehaviour
    {
        public SpriteRenderer sprite;

        [Header("Timing (seconds)")]
        public float rest = 0.7f;
        public float windUp = 0.4f;
        public float rise = 0.35f;
        public float hang = 0.6f;
        public float slam = 0.14f;

        [Header("Motion")]
        public float jumpHeight = 4f;

        enum Phase { Rest, Wind, Rise, Hang, Slam }
        Phase _phase = Phase.Rest;
        float _t;
        float _groundY;

        void Start()
        {
            if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
            _groundY = transform.position.y;
            SetSprite(EnemySprites.SlammerIdle);
        }

        void Update()
        {
            _t += Time.deltaTime;
            Vector3 p = transform.position;

            switch (_phase)
            {
                case Phase.Rest:
                    if (_t >= rest) Go(Phase.Wind, EnemySprites.SlammerWind);
                    break;

                case Phase.Wind:
                    if (_t >= windUp) Go(Phase.Rise, EnemySprites.SlammerIdle);
                    break;

                case Phase.Rise:
                    p.y = Mathf.Lerp(_groundY, _groundY + jumpHeight, EaseOut(_t / rise));
                    if (_t >= rise) { p.y = _groundY + jumpHeight; Go(Phase.Hang, EnemySprites.SlammerIdle); }
                    break;

                case Phase.Hang:
                    if (_t >= hang) Go(Phase.Slam, EnemySprites.SlammerSlam);
                    break;

                case Phase.Slam:
                    p.y = Mathf.Lerp(_groundY + jumpHeight, _groundY, EaseIn(_t / slam));
                    if (_t >= slam)
                    {
                        p.y = _groundY;
                        Particle.Burst(new Vector3(p.x, _groundY - 0.4f, 0f), new Color(0.7f, 0.6f, 0.4f), 12, 7f);
                        Go(Phase.Rest, EnemySprites.SlammerIdle);
                    }
                    break;
            }

            transform.position = p;
        }

        void Go(Phase next, Sprite s)
        {
            _phase = next;
            _t = 0f;
            SetSprite(s);
        }

        void SetSprite(Sprite s)
        {
            if (sprite != null && s != null) sprite.sprite = s;
        }

        static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t); }
        static float EaseIn(float t) { t = Mathf.Clamp01(t); return t * t; }
    }
}
