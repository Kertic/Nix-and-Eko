using NixAndEko.Combat;
using NixAndEko.Player;
using NixAndEko.Util;
using UnityEngine;

namespace NixAndEko.Environment
{
    /// <summary>
    /// Trigger portal that teleports the player to <see cref="target"/> on entry. The tutorial
    /// level uses one per room so each chamber stays a self-contained space — the door is what
    /// stitches them together, not a shared doorway in a shared strip of geometry.
    ///
    /// Teleport does three things beyond the position write: it zeroes Nix's velocity so she
    /// doesn't slam into the destination's wall, moves her respawn point to the new room (a
    /// death here shouldn't drop her back into the previous puzzle), and snaps the camera onto
    /// her — smooth-follow would otherwise slide the view across the whole level.
    ///
    /// A brief re-trigger cooldown after each teleport keeps a return door on the destination
    /// side (should one ever be added) from bouncing Nix back and forth.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Door : MonoBehaviour
    {
        [Tooltip("World position Nix is teleported to when she enters the door.")]
        public Vector2 target;
        [Tooltip("Seconds after a teleport during which this door ignores re-entry.")]
        public float retriggerCooldown = 0.75f;

        float _lockedUntil;

        void Reset()
        {
            var col = GetComponent<Collider2D>();
            if (col) col.isTrigger = true;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (Time.time < _lockedUntil) return;
            var player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;
            // Only Nix uses doors; Eko's controller is frozen anyway, but the null-check on the
            // summoner below narrows this down cleanly without a name comparison.
            var summoner = player.GetComponent<EkoSummoner>();
            if (summoner == null) return;

            // Bring Eko back to Nix before the teleport so a lingering phantom isn't stranded in
            // the old room's geometry.
            if (summoner.eko != null && summoner.eko.Active) summoner.eko.Dismiss();

            player.transform.position = target;
            player.Velocity = Vector2.zero;

            var health = player.GetComponent<Health>();
            if (health != null) health.RespawnPoint = target;

            var cam = Camera.main;
            var follow = cam != null ? cam.GetComponent<CameraFollow>() : null;
            if (follow != null) follow.SnapToTarget(player.transform);

            _lockedUntil = Time.time + retriggerCooldown;
        }
    }
}
