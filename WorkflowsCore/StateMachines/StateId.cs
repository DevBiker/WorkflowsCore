namespace WorkflowsCore.StateMachines
{
    public struct StateId<TState, THiddenState>
    {
        public StateId(TState state)
        {
            Id = state;
            HiddenId = default(THiddenState);
            IsHiddenState = false;
        }

        public StateId(THiddenState state)
        {
            Id = default(TState);
            HiddenId = state;
            IsHiddenState = true;
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
