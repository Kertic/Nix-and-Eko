namespace NixAndEko.Core
{
    /// <summary>
    /// Minimal finite state machine. States drive their own transitions by calling
    /// <see cref="ChangeState"/> on the owning machine.
    /// </summary>
    public class StateMachine
    {
        public IState Current { get; private set; }
        public IState Previous { get; private set; }

        /// <summary>Seconds spent in the current state.</summary>
        public float TimeInState { get; private set; }

        public void Initialize(IState startState)
        {
            Current = startState;
            TimeInState = 0f;
            Current?.Enter();
        }

        public void ChangeState(IState next)
        {
            if (next == null || next == Current)
                return;

            Current?.Exit();
            Previous = Current;
            Current = next;
            TimeInState = 0f;
            Current.Enter();
        }

        /// <summary>Per-frame update. Call from MonoBehaviour.Update.</summary>
        public void Tick(float deltaTime)
        {
            TimeInState += deltaTime;
            Current?.Tick(deltaTime);
        }

        /// <summary>Fixed-step update. Call from MonoBehaviour.FixedUpdate.</summary>
        public void FixedTick(float fixedDeltaTime)
        {
            Current?.FixedTick(fixedDeltaTime);
        }
    }

    public interface IState
    {
        /// <summary>Called once when the machine switches into this state.</summary>
        void Enter();

        /// <summary>Called once when the machine switches out of this state.</summary>
        void Exit();

        /// <summary>Per-frame logic (input reading, transition decisions, animation).</summary>
        void Tick(float deltaTime);

        /// <summary>Physics-step logic (velocity / Rigidbody writes).</summary>
        void FixedTick(float fixedDeltaTime);
    }
}
