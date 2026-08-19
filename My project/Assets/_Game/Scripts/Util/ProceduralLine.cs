using UnityEngine;

namespace NixAndEko.Util
{
    /// <summary>
    /// Ensures a LineRenderer has a valid unlit sprite material at edit-time and runtime, so a
    /// code-generated trajectory line survives scene save/reload without a missing-material pink.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(LineRenderer))]
    public class ProceduralLine : MonoBehaviour
    {
        LineRenderer _lr;

        void OnEnable()
        {
            _lr = GetComponent<LineRenderer>();
            if (_lr != null && _lr.sharedMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null) _lr.sharedMaterial = new Material(shader);
            }
        }
    }
}
