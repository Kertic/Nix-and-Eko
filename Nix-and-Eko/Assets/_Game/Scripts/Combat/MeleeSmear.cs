using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// The crescent blade smear that trails one melee swing. Spawned in code (no prefab, matching
    /// the rest of the placeholder art), draws itself as a <see cref="LineRenderer"/> arc that
    /// sweeps out across the swing window and fades on the tail — a horizontal slice for hit 0, a
    /// tight forward stab for hit 1, an overhead chop for hit 2. World-space, unparented, so a
    /// running Nix leaves the smear anchored where she struck rather than dragging it with her.
    /// </summary>
    public class MeleeSmear : MonoBehaviour
    {
        LineRenderer _lr;
        float _age;
        float _duration = 0.28f;
        int _segments = 20;

        // Arc parameters chosen at spawn.
        Vector3 _origin;
        int _facing;
        float _radius;
        float _startAngle;
        float _endAngle;
        Color _color;

        /// <summary>Spawn a smear for one swing. <paramref name="hitIndex"/> selects the arc
        /// shape (0 = swipe, 1 = thrust, 2 = overhead), <paramref name="range"/> is the outer
        /// radius (roughly the blade's reach), and <paramref name="duration"/> matches the
        /// swing so the smear finishes when the strike does.</summary>
        public static MeleeSmear Play(Vector3 origin, int facing, int hitIndex, float range, float duration)
        {
            var go = new GameObject("MeleeSmear");
            Transform parent = Particle.FxContainer();
            if (parent != null) go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = origin;
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.55f;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 2;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.sortingOrder = 21;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            go.AddComponent<ProceduralLine>();   // keeps the material valid on assembly reload

            var m = go.AddComponent<MeleeSmear>();
            m._lr = lr;
            m._origin = origin;
            m._facing = facing >= 0 ? 1 : -1;
            m._radius = range;
            m._duration = Mathf.Max(0.05f, duration);
            m._color = new Color(0.95f, 0.98f, 1f, 0.9f);

            // Arc bounds (degrees around Nix, 0 = forward along facing, CCW positive when facing right).
            // Flip signs when facing left so the arc always sweeps down-forward from Nix's shoulder.
            switch (hitIndex)
            {
                case 1:                          // thrust: tight forward slit
                    m._startAngle =  25f;
                    m._endAngle   = -25f;
                    m._lr.widthMultiplier = 0.35f;
                    m._radius = range * 1.05f;   // slightly longer to sell the poke
                    break;
                case 2:                          // overhead: from up-back to down-forward
                    m._startAngle = 120f;
                    m._endAngle   = -40f;
                    m._lr.widthMultiplier = 0.7f;
                    m._radius = range * 1.15f;
                    break;
                default:                         // swipe: horizontal crescent
                    m._startAngle =  75f;
                    m._endAngle   = -75f;
                    m._lr.widthMultiplier = 0.55f;
                    break;
            }

            m.Rebuild(revealFraction: 0f);
            return m;
        }

        void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _duration);

            // Reveal the arc across the first ~55% of the swing, then hold, then fade the whole
            // stroke out over the remainder. Read as: blade sweeps, smear lingers, smear fades.
            float reveal = Mathf.Clamp01(t / 0.55f);
            float fade = t < 0.55f ? 1f : Mathf.Clamp01(1f - (t - 0.55f) / 0.45f);

            Rebuild(reveal);
            float a = _color.a * fade;
            Color head = new Color(_color.r, _color.g, _color.b, a);
            Color tail = new Color(_color.r, _color.g, _color.b, 0f);
            _lr.startColor = tail;   // trailing edge is transparent
            _lr.endColor   = head;   // leading edge is bright

            if (t >= 1f) Destroy(gameObject);
        }

        void Rebuild(float revealFraction)
        {
            int count = Mathf.Max(2, Mathf.CeilToInt(_segments * Mathf.Clamp01(revealFraction)));
            _lr.positionCount = count;

            for (int i = 0; i < count; i++)
            {
                float u = (count == 1) ? 0f : (float)i / (count - 1);
                float deg = Mathf.Lerp(_startAngle, _endAngle, u) * _facing;
                float rad = deg * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * _radius;
                _lr.SetPosition(i, _origin + p);
            }
        }
    }
}
