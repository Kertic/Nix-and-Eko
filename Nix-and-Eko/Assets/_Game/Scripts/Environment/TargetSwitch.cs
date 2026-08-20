using NixAndEko.Combat;
using UnityEngine;
using UnityEngine.Events;

namespace NixAndEko.Environment
{
    /// <summary>
    /// A shootable switch. An arrow hit toggles (or one-shot activates) it, firing UnityEvents
    /// and toggling any linked <see cref="Gate"/>s. The classic metroidvania "shoot the target
    /// to open the door" block.
    /// </summary>
    public class TargetSwitch : MonoBehaviour, IArrowHittable
    {
        [Tooltip("If true, the switch can be toggled on and off. If false, it latches on first hit.")]
        public bool toggle = true;
        [Tooltip("Arrows stick into the switch (false = arrow is consumed).")]
        public bool arrowSticks = false;

        [Header("Linked")]
        public Gate[] gates;

        [Header("Visuals")]
        public SpriteRenderer targetRenderer;
        public Color offColor = new Color(0.7f, 0.3f, 0.3f);
        public Color onColor = new Color(0.4f, 0.9f, 0.5f);

        [Header("Events")]
        public UnityEvent onActivated;
        public UnityEvent onDeactivated;

        public bool IsOn { get; private set; }

        void Start() => Refresh();

        public bool OnArrowHit(Arrow arrow)
        {
            if (toggle) SetState(!IsOn);
            else if (!IsOn) SetState(true);
            return arrowSticks;
        }

        public void SetState(bool on)
        {
            IsOn = on;
            if (on) onActivated?.Invoke();
            else onDeactivated?.Invoke();

            if (gates != null)
                foreach (var g in gates)
                    if (g != null) g.SetOpen(on);

            Refresh();
        }

        void Refresh()
        {
            if (targetRenderer != null)
                targetRenderer.color = IsOn ? onColor : offColor;
        }
    }
}
