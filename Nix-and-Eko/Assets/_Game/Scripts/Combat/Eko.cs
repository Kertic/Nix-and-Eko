using NixAndEko.Environment;
using NixAndEko.Player;
using NixAndEko.Util;
using UnityEngine;
using UnityEngine.InputSystem;
using Hitstop = NixAndEko.Util.Hitstop; // fully-qualify away any future ambiguity

namespace NixAndEko.Combat
{
    /// <summary>
    /// Eko: a faerie (they/them). Lives on the phantom's own GameObject alongside its
    /// <see cref="PlayerController"/>, and switches between three states across a possession cycle:
    ///
    /// <list type="bullet">
    /// <item><b>Dormant</b> — <see cref="Active"/> false, GameObject hidden.</item>
    /// <item><b>Live (possessed)</b> — <see cref="Active"/> true, <see cref="Frozen"/> false. The
    /// player is walking / jumping Eko around under its own controller; this component tracks the
    /// aim direction (right stick / mouse) and draws a straight preview line for Eko's own arrow
    /// (Eko's arrows ignore gravity, so it really is dead straight). Eko can't fire during this
    /// phase — L1 hands control back to Nix instead. See <see cref="EkoSummoner"/>.</item>
    /// <item><b>Planted (frozen)</b> — <see cref="Active"/> true, <see cref="Frozen"/> true. Eko
    /// stands where the player left them, with whatever aim they were holding at handoff still on
    /// them, waiting for Nix's L1 to loose the shot. Rigidbody is suspended so it hovers exactly
    /// where it stopped — an air shot Nix set up stays put for her to fire.</item>
    /// </list>
    ///
    /// Eko's phantom is always considered armed — the preview / firing don't depend on Nix's own
    /// ammo state (Eko fires its own blue arrows regardless).
    /// </summary>
    public class Eko : MonoBehaviour
    {
        [Header("References")]
        public SpriteRenderer sprite;
        [Tooltip("Straight-line preview of Eko's next shot.")]
        public LineRenderer trajectory;
        public Arrow arrowPrefab;
        [Tooltip("Reticle shown while the player is aiming Eko.")]
        public Transform aimIndicator;
        public SpriteRenderer aimIndicatorRenderer;
        [Tooltip("Nix — the return home target for the DismissWithOrb visual.")]
        public PlayerController player;
        [Tooltip("Eko's own controller — set by PlayerFactory; used to source the current facing " +
                 "when picking a default aim direction.")]
        public PlayerController ekoPlayer;
        [Tooltip("Eko's own input reader — right stick / mouse aim comes through here, same as " +
                 "Nix's Bow reads its own reader.")]
        public PlayerInputReader ekoInput;
        [Tooltip("Nix's bow — read for the arrow launch speed only, so Eko's arrows fly at the " +
                 "same speed as the rest of the game.")]
        public Bow nixBow;
        [Tooltip("Small dot markers dropped wherever the preview crosses clean through a one-way " +
                 "platform instead of stopping there.")]
        public Transform[] passThroughMarkers;

        [Header("Look")]
        [Tooltip("How far the straight preview reaches before giving up.")]
        public float previewDistance = 40f;
        [Tooltip("How far off Eko the reticle sits along the current aim.")]
        public float indicatorDistance = 1.4f;
        [Tooltip("Snap Eko's aim to 8 directions (N, NE, E, ...).")]
        public bool eightDirectional = true;
        [Tooltip("Extra degrees past a sector boundary the aim must travel before switching " +
                 "direction (anti-flicker).")]
        [Range(0f, 22f)]
        public float aimHysteresis = 12f;
        public Color previewColor = new Color(0.35f, 0.75f, 1f, 0.9f);
        public Color aimColor = new Color(0.35f, 0.75f, 1f, 1f);

        // Freeze-frame length (real seconds) for the Eko-catches-Nix boost, kept as a code
        // constant so recompiles always apply (a serialized field would be baked into a scene's
        // Eko at build time and ignore edits until scene rebuild).
        const float CatchHitstop = 0.1f;

        // Fully opaque translucent-echo colour while the player is driving Eko.
        static readonly Color liveTint = new Color(0.4f, 0.8f, 1f, 0.9f);
        // A hair dimmer while planted and waiting to fire — reads as "held, not owned".
        static readonly Color frozenTint = new Color(0.4f, 0.8f, 1f, 0.7f);
        const int PhantomSortingOrder = 11;

        /// <summary>True whenever the phantom is out — live (possessed) or planted (frozen).</summary>
        public bool Active { get; private set; }
        /// <summary>True once control has been handed back to Nix and Eko is standing frozen with a
        /// held aim, waiting for Nix's L1 to loose. False while the player is driving Eko directly.</summary>
        public bool Frozen { get; private set; }
        /// <summary>Eko is always considered to have an arrow ready — Eko fires its own blues.</summary>
        public bool Prepared => Active;
        /// <summary>The current aim direction (unit vector). Live-updated from input while the
        /// player controls Eko; held frozen once control hands back.</summary>
        public Vector2 AimDirection { get; private set; } = Vector2.right;
        /// <summary>Has the player actually aimed Eko at any point during this possession? False
        /// means the phantom is planted with the default direction — Nix's L1 dismisses instead
        /// of firing in that case, so a summon-and-return-without-aiming cleans up quietly.</summary>
        public bool HasAim { get; private set; }

        LayerMask _mask;
        int _aimSector;
        bool _snapNow;
        bool _aimFromStickLast;
        Camera _cam;

        void Awake()
        {
            HideAimUI();
        }

        // ------------------------------------------------------------------ lifecycle
        /// <summary>Plant the phantom at <paramref name="position"/>, facing <paramref name="facing"/>,
        /// and switch it on in the live (player-controlled) state. Aim starts at the facing direction
        /// and gets updated each frame from Eko's own input reader.</summary>
        public void Summon(Vector3 position, int facing, LayerMask groundMask)
        {
            transform.position = position;
            _mask = groundMask;
            Frozen = false;
            HasAim = false;
            AimDirection = new Vector2(facing >= 0 ? 1 : -1, 0f);
            _aimSector = facing >= 0 ? 0 : 4;
            _snapNow = false;
            _aimFromStickLast = false;

            if (sprite != null)
            {
                sprite.enabled = true;
                sprite.sortingOrder = PhantomSortingOrder;
                if (sprite.sprite == null) sprite.sprite = ArcherSprites.IdleFrames[0];
                sprite.color = liveTint;

                float mag = Mathf.Abs(sprite.transform.localScale.x);
                if (mag < 0.01f) mag = 1f;
                sprite.transform.localScale = new Vector3(mag * (facing >= 0 ? 1 : -1), 1f, 1f);
            }

            Active = true;
            gameObject.SetActive(true);
            Sfx.Play(Sfx.Id.EkoSpawn);
        }

        /// <summary>The player let go of Eko (L1) — freeze it exactly where it is, preserving the
        /// current aim, so Nix can now fire it (or dismiss it) with another L1. Also stops the
        /// phantom's controller ticking so the aim stays anchored on a stable spot.</summary>
        public void FreezeInPlace()
        {
            if (!Active) return;
            Frozen = true;
            if (ekoPlayer != null) ekoPlayer.SetFrozen(true);
            if (sprite != null) sprite.color = frozenTint;
        }

        /// <summary>Dismiss with no visual effect. Used after Vanish / DismissWithOrb / Loose have
        /// already fired whatever they need — this just retires the GameObject cleanly.</summary>
        public void Dismiss()
        {
            if (ekoPlayer != null) ekoPlayer.SetFrozen(false);
            Active = false;
            Frozen = false;
            HasAim = false;
            HideAimUI();
            if (trajectory != null) trajectory.positionCount = 0;
            HidePassThroughMarkers();
            gameObject.SetActive(false);
        }

        /// <summary>Collapse into a blue orb that zips home to Nix, then dismiss — the clean return.</summary>
        public void DismissWithOrb()
        {
            if (!Active) return;
            Vector3 from = transform.position;
            Vector3 to = player != null ? player.transform.position : from;
            EkoOrb.Fly(from, to, 0.2f);
            Dismiss();
        }

        /// <summary>Eko yanked out of the world on the spot — a burst where they stood, no travel.</summary>
        public void Vanish()
        {
            if (!Active) return;
            Particle.Burst(transform.position, liveTint, 16, 6f, 0.4f, 0.8f);
            Sfx.Play(Sfx.Id.EkoZip, 0.75f);
            Dismiss();
        }

        // ------------------------------------------------------------------ per-frame
        void Update()
        {
            if (!Active || sprite == null) return;

            // Live: aim from input, updated each frame. Frozen: aim held, no input read.
            if (!Frozen) UpdateLiveAim();

            UpdateAimIndicator();
            UpdatePreview();

            // Keep the tint stable in case something knocked it off (a scene rebuild etc.).
            Color desired = Frozen ? frozenTint : liveTint;
            if (sprite.color != desired) sprite.color = desired;
        }

        // ------------------------------------------------------------------ aim
        /// <summary>Read the aim stick / mouse and snap the aim to the nearest 8-way sector, with
        /// hysteresis so it doesn't flicker at boundaries — same shape as <see cref="Bow"/>'s
        /// aim resolution, so the two behave identically.</summary>
        void UpdateLiveAim()
        {
            if (ekoInput == null) return;

            bool aimingNow = ekoInput.AimStickActive || ekoInput.MouseAiming;
            if (aimingNow) HasAim = true;

            Vector2 raw = GetRawAim();
            AimDirection = eightDirectional ? SnapEight(raw) : (raw.sqrMagnitude > 0.0001f ? raw.normalized : AimDirection);

            // Eko's move-facing (state machine sets it from move input) fights the aim otherwise —
            // face the aim direction whenever it's meaningfully horizontal, same as Bow does for Nix.
            if (ekoPlayer != null && Mathf.Abs(AimDirection.x) > 0.1f)
                ekoPlayer.SetFacing(AimDirection.x > 0 ? 1 : -1);
        }

        Vector2 GetRawAim()
        {
            if (ekoInput.AimStickActive)
            {
                if (!_aimFromStickLast) _snapNow = true;
                _aimFromStickLast = true;
                return ekoInput.AimStickDirection;
            }
            _aimFromStickLast = false;

            if (ekoInput.MouseAiming && Mouse.current != null)
            {
                if (_cam == null) _cam = Camera.main;
                if (_cam != null)
                {
                    Vector3 mp = Mouse.current.position.ReadValue();
                    mp.z = -_cam.transform.position.z;
                    Vector3 world = _cam.ScreenToWorldPoint(mp);
                    Vector2 d = (Vector2)(world - transform.position);
                    if (d.sqrMagnitude > 0.0001f) return d;
                }
            }

            int f = ekoPlayer != null ? ekoPlayer.Facing : 1;
            return new Vector2(f, 0f);
        }

        Vector2 SnapEight(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.0001f) return SectorToDir(_aimSector);

            if (_snapNow)
            {
                _snapNow = false;
                int nearest = Mathf.RoundToInt(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg / 45f);
                _aimSector = ((nearest % 8) + 8) % 8;
                return SectorToDir(_aimSector);
            }

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float fromCurrent = Mathf.DeltaAngle(_aimSector * 45f, angle);
            if (Mathf.Abs(fromCurrent) > 22.5f + aimHysteresis)
            {
                int nearest = Mathf.RoundToInt(angle / 45f);
                _aimSector = ((nearest % 8) + 8) % 8;
            }
            return SectorToDir(_aimSector);
        }

        static Vector2 SectorToDir(int sector)
        {
            float rad = sector * 45f * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;
        }

        // ------------------------------------------------------------------ visuals
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

        /// <summary>Redraw the shot preview. A straight arrow needs no arc simulation — one raycast
        /// (walking through one-ways that would pass clean through, marking the crossings) gives
        /// the exact flight path and the surface it stops on.</summary>
        void UpdatePreview()
        {
            if (trajectory == null) return;

            Vector2 origin = transform.position;
            Vector2 end = origin + AimDirection * previewDistance;
            int markerCount = 0;

            if (_mask.value != 0)
            {
                Vector2 castOrigin = origin;
                float remaining = previewDistance;
                Collider2D lastPassThrough = null;

                for (int i = 0; i < 8 && remaining > 0.01f; i++)
                {
                    var hit = Physics2D.Raycast(castOrigin, AimDirection, remaining, _mask);
                    if (hit.collider == null) break;

                    if (OneWayPlatform.Blocks(hit))
                    {
                        end = hit.point;
                        break;
                    }

                    if (hit.collider != lastPassThrough &&
                        passThroughMarkers != null && markerCount < passThroughMarkers.Length)
                    {
                        ShowPassThroughMarker(markerCount++, hit.point);
                        lastPassThrough = hit.collider;
                    }

                    float advanced = Vector2.Distance(castOrigin, hit.point) + 0.05f;
                    castOrigin += AimDirection * advanced;
                    remaining -= advanced;
                }
            }

            HidePassThroughMarkers(markerCount);

            trajectory.positionCount = 2;
            trajectory.SetPosition(0, origin);
            trajectory.SetPosition(1, end);
            trajectory.startColor = previewColor;
            trajectory.endColor = new Color(previewColor.r, previewColor.g, previewColor.b, 0f);
        }

        void ShowPassThroughMarker(int index, Vector2 pos)
        {
            if (passThroughMarkers == null || index >= passThroughMarkers.Length) return;
            Transform m = passThroughMarkers[index];
            if (m == null) return;
            m.gameObject.SetActive(true);
            m.position = pos;
        }

        void HidePassThroughMarkers(int fromIndex = 0)
        {
            if (passThroughMarkers == null) return;
            for (int i = fromIndex; i < passThroughMarkers.Length; i++)
                if (passThroughMarkers[i] != null) passThroughMarkers[i].gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ firing
        /// <summary>Loose the held shot along the current aim. <paramref name="nixCol"/> is Nix's
        /// collider, registered as the arrow's catch target so the shot can find her without
        /// physically shoving her — the momentum boost is applied deliberately on catch (see
        /// <see cref="EkoArrowTarget"/>). Straight, gravity-free, spawn cleared to Eko's body.</summary>
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
            arrow.blue = true;                // Eko's arrows read blue
            arrow.ekoAim = AimDirection;
            arrow.SetCatchTarget(nixCol);
            arrow.Launch(AimDirection * speed, 1f);

            // A tiny hitstop on release adds weight to the shot going off — a swap-style beat.
            Hitstop.Freeze(CatchHitstop);
            return arrow;
        }
    }
}
