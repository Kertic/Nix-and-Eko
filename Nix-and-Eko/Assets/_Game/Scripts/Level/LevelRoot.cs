using UnityEngine;

namespace NixAndEko.Level
{
    /// <summary>
    /// Marks the GameObject a level was built under, so a rebuild can find and clear the
    /// previous build reliably - even if the root was renamed since it was created.
    /// </summary>
    public class LevelRoot : MonoBehaviour
    {
    }
}
