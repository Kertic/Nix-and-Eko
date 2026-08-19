using UnityEngine;

namespace NixAndEko.Environment
{
    /// <summary>Sets the player's respawn point when touched.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class Checkpoint : MonoBehaviour
    {
        [Tooltip("Where the player respawns (defaults to this object's position).")]
        public Transform respawnAnchor;
        public bool activateOnce = true;

        bool _used;

        void Reset()
        {
            var col = GetComponent<Collider2D>();
            if (col) col.isTrigger = true;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (activateOnce && _used) return;

            var health = other.GetComponentInParent<Health>();
            if (health == null) return;

            health.RespawnPoint = respawnAnchor != null ? respawnAnchor.position : transform.position;
            _used = true;
        }
    }
}
