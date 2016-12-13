namespace WorkflowsCore.StateMachines
{
    public struct StateId<TState, THiddenState>
    {
        public StateId(TState state)
        {
            IsHiddenState = false;
            Id = state;
            HiddenId = default(THiddenState);
        }

        public StateId(THiddenState state)
        {
            IsHiddenState = true;
            Id = default(TState);
            HiddenId = state;
        }

        public bool IsHiddenState { get; }

        public TState Id { get; }

        public THiddenState HiddenId { get; }

        public static implicit operator StateId<TState, THiddenState>(TState state) => 
            new StateId<TState, THiddenState>(state);

        public static implicit operator StateId<TState, THiddenState>(THiddenState state) =>
            new StateId<TState, THiddenState>(state);
    }
}
