using NixAndEko.Player;
using UnityEngine;

namespace NixAndEko.Combat
{
    /// <summary>
    /// Homing-arrow aim assist: given a shot origin, aim direction, and shooter identity, find
    /// the best target in front of the shooter that the arrow should curve toward. Applied by
    /// both <see cref="Bow"/> (Nix's shots) and <see cref="Eko.Loose"/> (the phantom's spectral
    /// shot), so any arrow that leaves the scene with a live target in its cone catches it —
    /// enemies for damage, the opposing character for the ally-catch bonuses.
    ///
    /// <para>What counts as a target</para>
    /// <list type="bullet">
    /// <item>Every live <see cref="EnemyHealth"/> in the scene.</item>
    /// <item>Every <see cref="PlayerController"/> in the scene <b>except the shooter</b>. That
    /// covers Nix's arrow homing to the phantom (when it's out) and the phantom's arrow homing
    /// to Nix. A phantom that isn't <see cref="Eko.Active"/> is dormant and skipped.</item>
    /// </list>
    ///
    /// The best target is the one whose perpendicular offset from the aim line is smallest,
    /// among those inside the along-aim window [<paramref name="assistMinDistance"/>,
    /// <paramref name="assistMaxDistance"/>] and within <paramref name="assistRadius"/> world
    /// units of the line. Ties are broken by whichever was iterated first.
    /// </summary>
    public static class AimAssist
    {
        public static Transform FindTarget(Vector2 origin, Vector2 dir, Transform shooter,
                                           float assistRadius, float assistMinDistance,
                                           float assistMaxDistance)
        {
            if (dir.sqrMagnitude < 0.0001f) return null;
            dir = dir.normalized;

            Transform best = null;
            float bestPerp = float.MaxValue;

            var enemies = Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
            foreach (var e in enemies)
            {
                if (e == null || !e.isActiveAndEnabled) continue;
                if (shooter != null && e.transform == shooter) continue;
                if (InCone(origin, dir, e.transform.position, assistRadius, assistMinDistance,
                           assistMaxDistance, out float perp) && perp < bestPerp)
                { best = e.transform; bestPerp = perp; }
            }

            var players = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            foreach (var p in players)
            {
                if (p == null || !p.isActiveAndEnabled) continue;
                if (shooter != null && p.transform == shooter) continue;
                // A phantom whose Eko is dormant isn't a real target — skip it. Nix (no Eko
                // component) always qualifies.
                var eko = p.GetComponent<Eko>();
                if (eko != null && !eko.Active) continue;
                if (InCone(origin, dir, p.transform.position, assistRadius, assistMinDistance,
                           assistMaxDistance, out float perp) && perp < bestPerp)
                { best = p.transform; bestPerp = perp; }
            }

            return best;
        }

        static bool InCone(Vector2 origin, Vector2 dir, Vector2 target,
                           float radius, float minDist, float maxDist, out float perp)
        {
            Vector2 to = target - origin;
            float along = Vector2.Dot(to, dir);
            if (along < minDist || along > maxDist) { perp = 0f; return false; }
            perp = (to - dir * along).magnitude;
            return perp <= radius;
        }
    }
}
