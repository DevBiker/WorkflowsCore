using System;

namespace WorkflowsCore.StateMachines
{
    public struct StateId<TState, THiddenState>
    {
        private readonly TState _id;
        private readonly THiddenState _hiddenId;

        public StateId(TState state)
        {
            IsHiddenState = false;
            _id = state;
            _hiddenId = default(THiddenState);
        }

        public StateId(THiddenState state)
        {
            IsHiddenState = true;
            _id = default(TState);
            _hiddenId = state;
        }

        public bool IsHiddenState { get; }

        public TState Id
        {
            get
            {
                if (IsHiddenState)
                {
                    throw new InvalidOperationException();
                }

                return _id;
            }
        }

        public THiddenState HiddenId
        {
            get
            {
                if (!IsHiddenState)
                {
                    throw new InvalidOperationException();
                }

                return _hiddenId;
            }
        }

        public static implicit operator StateId<TState, THiddenState>(TState state) => 
            new StateId<TState, THiddenState>(state);

        public static implicit operator StateId<TState, THiddenState>(THiddenState state) =>
            new StateId<TState, THiddenState>(state);

        public static explicit operator TState(StateId<TState, THiddenState> state) => state.Id;

        public static explicit operator THiddenState(StateId<TState, THiddenState> state) => state.HiddenId;

        public override string ToString() => !IsHiddenState ? $"{Id}" : $"{HiddenId} (hidden)";
    }
}
