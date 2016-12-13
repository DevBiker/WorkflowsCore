using WorkflowsCore.StateMachines;

namespace WorkflowsCore.Tests
{
    public class BaseStateTest<T>
    {
        private readonly StateMachine<T> _stateMachine = new StateMachine<T>();

        public State<T> CreateState(T state) => _stateMachine.ConfigureState(state); 
    }
}
