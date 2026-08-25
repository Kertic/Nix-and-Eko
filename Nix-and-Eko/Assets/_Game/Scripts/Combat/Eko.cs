using NixAndEko.Environment;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// Eko: a faerie (they/them) who appears as a frozen echo of Nix, summoned while she aims and
    /// pinned in place for as long as the summon button is held. They keep holding the shot Nix
    /// was lining up and preview it as a dead straight line — Eko's arrows ignore gravity, unlike
    /// Nix's arcs — then loose it the moment the button is released. If that arrow catches Nix in
    /// the air it reloads her air shot (see <see cref="EkoArrowTarget"/>).
    ///
    /// Lives in world space rather than parented to Nix, so the phantom stays put while Nix flies
    /// off. The object is kept around inactive between summons instead of being rebuilt each time.
    /// </summary>
    public class Eko : MonoBehaviour
    {
        [Header("References")]
        public SpriteRenderer sprite;
        [Tooltip("Straight-line preview of the shot Eko is holding.")]
        public LineRenderer trajectory;
        public Arrow arrowPrefab;
        [Tooltip("Small dot markers dropped wherever the preview crosses clean through a one-way platform instead of stopping there.")]
        public Transform[] passThroughMarkers;

        [Header("Look")]
        [Tooltip("How far the straight-shot preview reaches before giving up.")]
        public float previewDistance = 40f;
        [Tooltip("Eko renders as a translucent blue echo of Nix's silhouette.")]
        public Color tint = new Color(0.35f, 0.75f, 1f, 0.55f);
        public Color previewColor = new Color(0.35f, 0.75f, 1f, 0.9f);

        /// <summary>True while a phantom is standing in the world.</summary>
        public bool Active { get; private set; }
        /// <summary>The frozen aim direction — Eko never re-aims after being summoned.</summary>
        public Vector2 AimDirection { get; private set; } = Vector2.right;

        LayerMask _mask;

        /// <summary>Plant the phantom, frozen at <paramref name="position"/> aiming along <paramref name="aim"/>. <paramref name="facing"/> mirrors their silhouette to match Nix.</summary>
        public void Summon(Vector3 position, Vector2 aim, int facing, LayerMask groundMask)
        {
            transform.position = position;
            AimDirection = aim.sqrMagnitude > 0.0001f ? aim.normalized : Vector2.right;
            _mask = groundMask;

            if (sprite != null)
            {
                sprite.color = tint;
                Vector3 s = sprite.transform.localScale;
                s.x = Mathf.Abs(s.x) * (facing >= 0 ? 1 : -1);
                sprite.transform.localScale = s;
            }

            Active = true;
            gameObject.SetActive(true);
            UpdatePreview();
        }

        /// <summary>
        /// Redraw the shot preview. A straight arrow needs no arc simulation — a raycast gives the
        /// exact flight path and the surface it stops at. A one-way platform only stops the line if
        /// the shot would actually hit its blocking face (<see cref="OneWayPlatform.Blocks"/>) — a
        /// shallow shot that would pass clean through instead gets a marker dropped at the crossing
        /// and the cast continues past it, same as the real arrow will.
        /// </summary>
        public void UpdatePreview()
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

                // Bounded rather than "while true" — a handful of stacked one-way platforms is
                // plenty, and this guarantees the cast can't loop forever on a degenerate setup.
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

                    // Step just past this collider and keep casting from there.
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

        /// <summary>
        /// Loose the held shot along the frozen aim. <paramref name="nixCol"/> is Nix's collider,
        /// registered as the catch target: the arrow never physically shoves her — the boost is a
        /// deliberate launch along the aim, applied on catch. A straight shot must clear her bounds
        /// once before it can catch, so one spawned where she was standing can't self-catch. When
        /// <paramref name="homeTarget"/> is set (aim assist — Nix was on the preview line, past the
        /// min range, at release), the arrow curves to her and phases through everything else so it
        /// always lands (it arms immediately, since the min range rules out a point-blank self-catch).
        /// </summary>
        public Arrow Loose(float speed, float charge, Collider2D nixCol, Transform homeTarget = null)
        {
            if (arrowPrefab == null)
            {
                Debug.LogWarning("[Eko] No arrow prefab assigned.", this);
                return null;
            }

            Quaternion rot = Quaternion.FromToRotation(Vector3.right, AimDirection);
            Arrow arrow = Instantiate(arrowPrefab, transform.position, rot);
            arrow.gameObject.SetActive(true);   // the template is inactive; copies must run

            arrow.flyStraight = true;
            arrow.isEkoArrow = true;
            arrow.ekoAim = AimDirection;      // the way Nix gets flung if this catches her
            arrow.SetCatchTarget(nixCol);     // never shove Nix; caught by overlap instead
            if (homeTarget != null) arrow.HomeTo(homeTarget);
            arrow.Launch(AimDirection * speed, charge);
            return arrow;
        }

        /// <summary>Send the phantom away without firing (or after they fire).</summary>
        public void Dismiss()
        {
            Active = false;
            if (trajectory != null) trajectory.positionCount = 0;
            gameObject.SetActive(false);
        }
    }
}
