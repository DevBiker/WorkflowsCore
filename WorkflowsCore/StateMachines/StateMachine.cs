using System;
using System.Threading.Tasks;

namespace WorkflowsCore.StateMachines
{
    public class StateMachine<T>
    {
        public State<T> ConfigureState(T state)
        {
            throw new NotImplementedException();
        }

        public State<T> GetState(T state)
        {
            throw new NotImplementedException();
        }

        public State<T> ConfigureHiddenState(string name)
        {
            throw new NotImplementedException();
        }

        public State<T> GetHiddenState(string name)
        {
            throw new NotImplementedException();
        }

        public Task Run(WorkflowBase workflow, State<T> initialState)
        {
            throw new NotImplementedException();
        }
    }
}
