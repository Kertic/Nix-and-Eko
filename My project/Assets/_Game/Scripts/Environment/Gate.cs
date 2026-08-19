using UnityEngine;

namespace NixAndEko.Environment
{
    /// <summary>
    /// A door/gate that opens by sliding away. Driven by a <see cref="TargetSwitch"/> or any
    /// script calling <see cref="SetOpen"/>. Blocks passage while closed.
    /// </summary>
    public class Gate : MonoBehaviour
    {
        [Tooltip("Local offset applied when open (e.g. slide up).")]
        public Vector3 openOffset = new Vector3(0f, 3f, 0f);
        public float moveSpeed = 8f;
        public bool startOpen = false;

        Vector3 _closedPos;
        Vector3 _openPos;
        bool _open;

        void Awake()
        {
            _closedPos = transform.localPosition;
            _openPos = _closedPos + openOffset;
            _open = startOpen;
            transform.localPosition = _open ? _openPos : _closedPos;
        }

        public void SetOpen(bool open) => _open = open;
        public void Toggle() => _open = !_open;

        void Update()
        {
            Vector3 target = _open ? _openPos : _closedPos;
            transform.localPosition = Vector3.MoveTowards(
                transform.localPosition, target, moveSpeed * Time.deltaTime);
        }
    }
}
