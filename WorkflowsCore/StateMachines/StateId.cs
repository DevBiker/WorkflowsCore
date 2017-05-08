using System;

namespace WorkflowsCore.StateMachines
{
    public struct StateId<TState, TInternalState>
    {
        private readonly TState _id;
        private readonly TInternalState _internalState;

        public StateId(TState state)
        {
            IsInternalState = false;
            _id = state;
            _internalState = default(TInternalState);
        }

        public StateId(TInternalState state)
        {
            IsInternalState = true;
            _id = default(TState);
            _internalState = state;
        }

        public bool IsInternalState { get; }

        public TState Id
        {
            get
            {
                if (IsInternalState)
                {
                    throw new InvalidOperationException();
                }

                return _id;
            }
        }

        public TInternalState InternalState
        {
            get
            {
                if (!IsInternalState)
                {
                    throw new InvalidOperationException();
                }

                return _internalState;
            }
        }

        public static implicit operator StateId<TState, TInternalState>(TState state) => 
            new StateId<TState, TInternalState>(state);

        public static implicit operator StateId<TState, TInternalState>(TInternalState state) =>
            new StateId<TState, TInternalState>(state);

        public static explicit operator TState(StateId<TState, TInternalState> state) => state.Id;

        public static explicit operator TInternalState(StateId<TState, TInternalState> state) => state.InternalState;

        public override string ToString() => !IsInternalState ? $"{Id}" : $"{InternalState} (hidden)";
    }
}
