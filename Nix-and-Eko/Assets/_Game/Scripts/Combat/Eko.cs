using NixAndEko.Environment;
using NixAndEko.Player;
using NixAndEko.Util;
using UnityEngine;
using Hitstop = NixAndEko.Util.Hitstop;

namespace NixAndEko.Combat
{
    /// <summary>
    /// Eko: a faerie (they/them) who rides <em>inside</em> Nix's arrow. Every Nix arrow is
    /// visually blue for that reason — Eko is nested in it. See <see cref="EkoSummoner"/> for
    /// the three-way L1 flow: tap dashes Nix to the stuck arrow, hold morphs the arrow into
    /// the phantom for a frozen-time aimed shot.
    ///
    /// <para><b>Phantom states</b></para>
    /// While <see cref="Active"/> is true, the phantom stands frozen at the arrow's position.
    /// <see cref="EkoSummoner"/> writes <see cref="AimDirection"/> every frame from Nix's own
    /// input reader (stick or mouse relative to the phantom's world position) and this component
    /// draws the reticle + straight-line preview. On release, <see cref="Loose"/> fires a blue
    /// arrow along the held aim; the arrow's catch target is Nix, so a shot that connects still
    /// runs through <see cref="EkoArrowTarget"/> for the reload + momentum + <b>+1 air jump</b>.
    /// </summary>
    public class Eko : MonoBehaviour
    {
        [Header("References")]
        public SpriteRenderer sprite;
        public Arrow arrowPrefab;
        [Tooltip("Reticle shown while aiming the phantom.")]
        public Transform aimIndicator;
        public SpriteRenderer aimIndicatorRenderer;
        [Tooltip("Straight-line preview of Eko's next shot.")]
        public LineRenderer trajectory;
        [Tooltip("Nix — orb-home target on dismiss.")]
        public PlayerController player;
        [Tooltip("Eko's own controller — set by PlayerFactory; frozen throughout.")]
        public PlayerController ekoPlayer;
        [Tooltip("Nix's bow — read for arrow launch speed so Eko's blues match Nix's shot speed.")]
        public Bow nixBow;

        [Header("Look")]
        [Tooltip("How far the straight preview reaches before giving up.")]
        public float previewDistance = 40f;
        [Tooltip("How far off Eko the reticle sits along the current aim.")]
        public float indicatorDistance = 1.4f;
        public Color previewColor = new Color(0.35f, 0.75f, 1f, 0.9f);
        public Color aimColor     = new Color(0.35f, 0.75f, 1f, 1f);

        static readonly Color phantomTint = new Color(0.4f, 0.85f, 1f, 0.85f);
        const int PhantomSortingOrder = 11;
        const float CatchHitstop = 0.1f;

        /// <summary>True while the phantom is out at the arrow, awaiting the release.</summary>
        public bool Active { get; private set; }

        /// <summary>Aim written by <see cref="EkoSummoner"/> each frame; consumed by
        /// <see cref="Loose"/> at fire time.</summary>
        public Vector2 AimDirection { get; set; } = Vector2.right;

        void Awake() => HideAimUI();

        // ------------------------------------------------------------------ lifecycle
        public void Summon(Vector3 position, int facing)
        {
            transform.position = position;
            AimDirection = new Vector2(facing >= 0 ? 1 : -1, 0f);

            if (sprite != null)
            {
                sprite.enabled = true;
                sprite.sortingOrder = PhantomSortingOrder;
                if (sprite.sprite == null) sprite.sprite = ArcherSprites.IdleFrames[0];
                sprite.color = phantomTint;

                float mag = Mathf.Abs(sprite.transform.localScale.x);
                if (mag < 0.01f) mag = 1f;
                sprite.transform.localScale = new Vector3(mag * (facing >= 0 ? 1 : -1), 1f, 1f);
            }

            Active = true;
            gameObject.SetActive(true);
            Sfx.Play(Sfx.Id.EkoSpawn);
        }

        public void Dismiss()
        {
            if (ekoPlayer != null) ekoPlayer.SetFrozen(false);
            Active = false;
            HideAimUI();
            if (trajectory != null) trajectory.positionCount = 0;
            // Restore full scale in case a morph tween left us mid-transition.
            transform.localScale = Vector3.one;
            gameObject.SetActive(false);
        }

        public void DismissWithOrb()
        {
            if (!Active) return;
            Vector3 from = transform.position;
            Vector3 to = player != null ? player.transform.position : from;
            EkoOrb.Fly(from, to, 0.2f);
            Dismiss();
        }

        public void Vanish()
        {
            if (!Active) return;
            Particle.Burst(transform.position, phantomTint, 16, 6f, 0.4f, 0.8f);
            Sfx.Play(Sfx.Id.EkoZip, 0.75f);
            Dismiss();
        }

        // ------------------------------------------------------------------ per-frame visuals
        void Update()
        {
            if (!Active || sprite == null) return;
            if (PauseMenu.IsGameplayPaused) { HideAimUI(); return; }

            if (sprite.color != phantomTint) sprite.color = phantomTint;
            UpdateAimIndicator();
            UpdatePreview();
        }

        void UpdateAimIndicator()
        {
            if (aimIndicator == null) return;
            aimIndicator.gameObject.SetActive(true);
            aimIndicator.position = transform.position + (Vector3)AimDirection * indicatorDistance;
            aimIndicator.right = AimDirection;
            if (aimIndicatorRenderer != null) aimIndicatorRenderer.color = aimColor;
        }

        void HideAimUI()
        {
            if (aimIndicator != null) aimIndicator.gameObject.SetActive(false);
            if (trajectory != null) trajectory.positionCount = 0;
        }

        void UpdatePreview()
        {
            if (trajectory == null) return;

            Vector2 origin = transform.position;
            Vector2 end    = origin + AimDirection * previewDistance;

            LayerMask mask = ekoPlayer != null ? ekoPlayer.groundMask : default;
            if (mask.value != 0)
            {
                var hit = Physics2D.Raycast(origin, AimDirection, previewDistance, mask);
                if (hit.collider != null) end = hit.point;
            }

            trajectory.positionCount = 2;
            trajectory.SetPosition(0, origin);
            trajectory.SetPosition(1, end);
            trajectory.startColor = previewColor;
            trajectory.endColor = new Color(previewColor.r, previewColor.g, previewColor.b, 0f);
        }

        // ------------------------------------------------------------------ firing
        /// <summary>Loose the phantom's shot along the held aim. The arrow is BOTH a Nix arrow
        /// (persistent — becomes a pickup when it lands, so Nix can dash to it or fetch it) AND
        /// an Eko arrow (catches Nix on the way for the bonus slot + momentum + air jump via
        /// <see cref="EkoArrowTarget"/>). If the shot never lands and never catches, the
        /// safety-net grant on <see cref="Arrow.OnDestroy"/> hands Nix her normal arrow back,
        /// so the morph → fire cycle can never soft-lock her out of ammo.</summary>
        public Arrow Loose(float speed, Collider2D nixCol)
        {
            if (arrowPrefab == null)
            {
                Debug.LogWarning("[Eko] No arrow prefab assigned.", this);
                return null;
            }

            Quaternion rot = Quaternion.FromToRotation(Vector3.right, AimDirection);
            Arrow arrow = Instantiate(arrowPrefab, transform.position, rot);
            arrow.gameObject.SetActive(true);
            arrow.flyStraight = true;
            arrow.isEkoArrow = true;
            arrow.blue = false;                              // stays visually blue via ApplyTint(isNixArrow)
            arrow.ekoAim = AimDirection;
            if (nixBow != null && nixCol != null)
                arrow.SetNixArrow(nixBow, nixCol, false);    // persistent + pickup + safety-net grant
            arrow.SetCatchTarget(nixCol);                    // still catches Nix for the bonuses
            arrow.Launch(AimDirection * speed, 1f);
            return arrow;
        }

        /// <summary>Legacy stub so <see cref="Arrow.Impact"/>'s Frozen branch compiles — under
        /// this design the phantom is never in a "solid frozen-catch" role.</summary>
        public bool Frozen => false;

        /// <summary>Legacy: unreachable now. Kept for API parity with <see cref="Arrow.Impact"/>.</summary>
        public void OnNixArrowHit(Arrow arrow)
        {
            if (!Active || player == null || arrow == null) return;
            Hitstop.Freeze(CatchHitstop);
            arrow.MarkReclaimed();
            Destroy(arrow.gameObject);
            Vanish();
        }
    }
}
