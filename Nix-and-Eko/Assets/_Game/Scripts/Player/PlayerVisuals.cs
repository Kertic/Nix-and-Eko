using NixAndEko.Combat;
using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Player
{
    /// <summary>
    /// Nix's non-locomotion visual accents:
    ///
    /// <list type="bullet">
    /// <item>A tiny blue orb — the same ball form Eko wears while zipping around on retrievals
    /// (see <see cref="EkoOrb"/>) — that hovers around Nix's head whenever the phantom is riding
    /// with her (<see cref="Eko.Active"/> false AND <see cref="EkoSummoner.Fetching"/> false). It
    /// leaks a small trail of blue particles behind it so the "faerie-y" motion reads at a glance.
    /// The moment Eko is deployed OR sent out on a fetch, the head-orbit ball hides — the phantom
    /// is out doing something else and shouldn't visually double up next to Nix.</item>
    /// <item>A physical arrow held in her draw hand, shown only while <see cref="Bow.HasAnyArrow"/>
    /// is true. Tinted blue when the next shot is one of Eko's blues.</item>
    /// </list>
    ///
    /// The held arrow lives under <see cref="PlayerController.spriteRoot"/> so it flips with
    /// facing (following the hand it's in). The Eko ball lives under the player root so its orbit
    /// isn't mirrored by facing. Both alphas track the main sprite's alpha so ghost mode dims
    /// them alongside Nix herself.
    /// </summary>
    public class PlayerVisuals : MonoBehaviour
    {
        public PlayerController player;
        public Bow bow;
        public Eko eko;
        public EkoSummoner summoner;
        public SpriteRenderer mainSprite;

        [Header("Eko ball (floating around Nix's head)")]
        public Transform ekoBall;
        public SpriteRenderer ekoBallRenderer;
        public Color ekoBallColor = new Color(0.4f, 0.72f, 1f, 0.95f);
        [Tooltip("Center of the orbit, in local space relative to the player root (above her head).")]
        public Vector2 orbitCenter = new Vector2(0f, 0.55f);
        [Tooltip("Orbit radius in world units.")]
        public float orbitRadius = 0.35f;
        [Tooltip("Vertical squash on the orbit so it reads as a hovering loop rather than a flat circle.")]
        public float orbitVerticalScale = 0.55f;
        [Tooltip("Radians per second around the orbit.")]
        public float orbitSpeed = 3.2f;
        [Tooltip("Small vertical bob added on top of the orbit so the ball feels alive.")]
        public float bobAmplitude = 0.06f;
        public float bobFrequency = 2.4f;

        [Header("Eko ball trail")]
        [Tooltip("Average seconds between dropped trail particles. Randomised ±30% per drop so the " +
                 "trail doesn't march in exact time with the orbit.")]
        public float trailInterval = 0.06f;
        [Tooltip("Lifetime of each trail particle in seconds.")]
        public float trailLifetime = 0.35f;
        [Tooltip("Starting scale of each trail particle (world units).")]
        public float trailStartScale = 0.18f;

        [Header("Held arrow")]
        public SpriteRenderer heldArrowRenderer;
        public Color arrowNormal = new Color(0.93f, 0.95f, 1f, 1f);
        public Color arrowBlue   = new Color(0.42f, 0.75f, 1f, 1f);

        float _nextTrailDrop;
        static Sprite _trailSprite;
        static Sprite TrailSprite =>
            _trailSprite != null ? _trailSprite : (_trailSprite = SpriteFactory.SolidCircle(Color.white, 4, Color.white));

        void LateUpdate()
        {
            float alpha = mainSprite != null ? mainSprite.color.a : 1f;

            // ---- Eko ball
            if (ekoBall != null)
            {
                bool ekoDeployed = eko != null && eko.Active;
                bool ekoFetching = summoner != null && summoner.Fetching;
                bool showBall = !ekoDeployed && !ekoFetching;

                if (ekoBallRenderer != null && ekoBallRenderer.enabled != showBall)
                    ekoBallRenderer.enabled = showBall;

                if (showBall)
                {
                    // Unscaled time so hitstop / pause don't leave the ball frozen mid-orbit in
                    // a jarring spot — orbs already run on unscaled time (see EkoOrb), so this
                    // matches. Pause menu hides everything anyway, so this really only affects
                    // brief hitstop freezes.
                    float t = Time.unscaledTime;
                    float ang = t * orbitSpeed;
                    float x = Mathf.Cos(ang) * orbitRadius;
                    float y = Mathf.Sin(ang) * orbitRadius * orbitVerticalScale
                              + Mathf.Sin(t * bobFrequency) * bobAmplitude;
                    ekoBall.localPosition = new Vector3(orbitCenter.x + x, orbitCenter.y + y, 0f);

                    if (ekoBallRenderer != null)
                    {
                        Color c = ekoBallColor;
                        c.a *= alpha;
                        ekoBallRenderer.color = c;
                    }

                    // Bleed a small trail of blue particles behind the orbit. Skipped while the
                    // main sprite is barely visible (ghost mode), so a possession dip doesn't
                    // dump a puff of leftover trail into an empty spot.
                    if (alpha > 0.6f && !NixAndEko.Environment.PauseMenu.IsGameplayPaused
                        && Time.unscaledTime >= _nextTrailDrop)
                    {
                        DropTrailParticle(alpha);
                        _nextTrailDrop = Time.unscaledTime + trailInterval * Random.Range(0.7f, 1.3f);
                    }
                }
                else
                {
                    // Keep the drop timer close to "now" so the trail resumes immediately the
                    // next time the ball reappears, rather than dumping a stale backlog.
                    _nextTrailDrop = Time.unscaledTime + trailInterval;
                }
            }

            // ---- Held arrow
            if (heldArrowRenderer != null && bow != null)
            {
                bool has = bow.HasAnyArrow;
                if (heldArrowRenderer.enabled != has) heldArrowRenderer.enabled = has;
                if (has)
                {
                    Color c = bow.FiresBlueNext ? arrowBlue : arrowNormal;
                    c.a *= alpha;
                    heldArrowRenderer.color = c;
                }
            }
        }

        /// <summary>Spawn one short-lived blue puff at the ball's current world position — falls
        /// slightly and shrinks. Uses <see cref="Particle.Spawn"/> directly (not Burst) so we get
        /// a single trailing dot per call rather than a radial pop.</summary>
        void DropTrailParticle(float alpha)
        {
            if (ekoBall == null) return;

            // Small downward-and-back drift so the trail looks like it's shedding off the moving
            // orb, not radiating out of it. `back` follows the orbital-tangent direction reversed
            // — a rough cheap approximation using the current position relative to the head is
            // good enough for a decorative puff.
            Vector3 pos = ekoBall.position;
            Vector2 back = Random.insideUnitCircle * 0.3f + new Vector2(0f, -0.15f);

            var p = Particle.Spawn(pos, Quaternion.identity,
                                   Vector3.one * (trailStartScale * Random.Range(0.7f, 1.15f)),
                                   sortingOrder: 24);   // just under the ball (25)
            // Override the shared quad with a circle sprite so trail dots read as tiny orbs, not
            // rotating pixel-squares. Only touches this particle — Particle.Quad is unchanged.
            var psr = p.GetComponent<SpriteRenderer>();
            if (psr != null) psr.sprite = TrailSprite;
            p.velocity = back;
            p.drag = 3f;
            p.gravity = 0f;
            p.lifetime = trailLifetime * Random.Range(0.75f, 1.15f);
            Color start = ekoBallColor; start.a *= alpha;
            p.startColor = start;
            p.endColor = new Color(ekoBallColor.r, ekoBallColor.g, ekoBallColor.b, 0f);
            p.startScale = 1f;
            p.endScale = 0.1f;
            p.Play();
        }
    }
}
