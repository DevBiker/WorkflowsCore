using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorkflowsCore.StateMachines;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class AsyncOperationTests
    {
        public enum States
        {
            None,
            State1
        }

        public static void AssertTargetStatesEqual(
            IList<TargetState<States, string>> expected,
            IList<TargetState<States, string>> actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            foreach (var t in expected.Zip(actual, Tuple.Create))
            {
                Assert.Same(t.Item1.State, t.Item2.State);
                Assert.Equal(t.Item1.Conditions, t.Item2.Conditions);
            }
        }

        public class AsyncOperationWithoutDataTests : BaseStateTest<States>
        {
            private readonly AsyncOperation<States, string> _asyncOperation;
            private readonly State<States, string> _parent;

            public AsyncOperationWithoutDataTests()
            {
                _parent = CreateState(States.None);
                _asyncOperation = new AsyncOperation<States, string>(_parent, "Test description");
            }

            [Fact]
            public async Task DoShouldExecuteActionWhenAsyncOperationIsExecuted()
            {
                var wasCalled = false;
                var state = _asyncOperation.Do(
                    async () =>
                    {
                        await Task.Delay(1);
                        wasCalled = true;
                    });

                var res = await _asyncOperation.ExecuteAsync();

                Assert.Same(_parent, state);
                Assert.Null(res);
                Assert.True(wasCalled);
            }

            [Fact]
            public void HandlersCannotBeAttachedMultipleTimes()
            {
                _asyncOperation.Do(() => Task.Delay(1));
                var ex = Record.Exception(() => _asyncOperation.Do(() => { }));

                Assert.IsType<InvalidOperationException>(ex);
            }

            [Fact]
            public async Task GoToShouldReturnNewStateWhenAsyncOperationIsExecuted()
            {
                var newState = CreateState(States.State1);
                var state = _asyncOperation.GoTo(States.State1);

                var res = await _asyncOperation.ExecuteAsync();

                Assert.Same(_parent, state);
                Assert.Same(newState, res);
            }

            [Fact]
            public async Task InvokeShouldCallFuncAndPropagateReturnedData()
            {
                var res = 0;
                _asyncOperation.Invoke(() => 3).Do(i => res = i);

                await _asyncOperation.ExecuteAsync();
                Assert.Equal(3, res);
            }

            [Fact]
            public async Task InvokeShouldCallAction()
            {
                var wasCalled = false;
                _asyncOperation.Invoke(new Action(() => wasCalled = true)).Do(() => { });

                await _asyncOperation.ExecuteAsync();
                Assert.True(wasCalled);
            }

            [Fact]
            public async Task IfShouldInterruptChainIfPredicateIsFalse()
            {
                var wasCalled = false;
                var state = _asyncOperation.If(() => false).Do(() => wasCalled = true);

                var res = await _asyncOperation.ExecuteAsync();

                Assert.Same(_parent, state);
                Assert.Null(res);
                Assert.False(wasCalled);
            }

            [Fact]
            public async Task IfShouldNotInterruptChainIfPredicateIsTrue()
            {
                var wasCalled = false;
                var state = _asyncOperation.If(() => true).Do(() => wasCalled = true);

                var res = await _asyncOperation.ExecuteAsync();

                Assert.Same(_parent, state);
                Assert.Null(res);
                Assert.True(wasCalled);
            }

            [Fact]
            public async Task IfThenGoToShouldReturnNewStateIfPredicateIsTrue()
            {
                var wasCalled = false;
                var state = _asyncOperation.IfThenGoTo(() => true, States.State1).Do(() => wasCalled = true);

                var res = await _asyncOperation.ExecuteAsync();

                Assert.Same(_parent, state);
                Assert.Equal(States.State1, res.StateId);
                Assert.False(wasCalled);
            }

            [Fact]
            public async Task IfThenGoToShouldContinueChainIfPredicateIsFalse()
            {
                var wasCalled = false;
                var state = _asyncOperation.IfThenGoTo(() => false, States.State1).Do(() => wasCalled = true);

                var res = await _asyncOperation.ExecuteAsync();

                Assert.Same(_parent, state);
                Assert.Null(res);
                Assert.True(wasCalled);
            }
        }

        public class AsyncOperationWithDataTests : BaseStateTest<States>
        {
            private readonly AsyncOperation<States, string, int> _asyncOperation;
            private readonly State<States, string> _parent;

            public AsyncOperationWithDataTests()
            {
                _parent = CreateState(States.None);
                _asyncOperation = new AsyncOperation<States, string, int>(_parent, "Test description");
            }

            [Fact]
            public async Task DoShouldExecuteActionWhenAsyncOperationIsExecuted()
            {
                var wasCalled = false;
                var state = _asyncOperation.Do(
                    async i =>
                    {
                        Assert.Equal(3, i);
                        await Task.Delay(1);
                        wasCalled = true;
                    });

                var res = await _asyncOperation.ExecuteAsync(3);

                Assert.Same(_parent, state);
                Assert.Null(res);
                Assert.True(wasCalled);
            }

            [Fact]
            public async Task IfShouldInterruptChainIfPredicateIsFalse()
            {
                var wasCalled = false;
                var state = _asyncOperation.If(i => i != 3).Do(() => wasCalled = true);

                var res = await _asyncOperation.ExecuteAsync(3);

                Assert.Same(_parent, state);
                Assert.Null(res);
                Assert.False(wasCalled);
            }

            [Fact]
            public async Task IfShouldNotInterruptChainIfPredicateIsTrue()
            {
                var wasCalled = false;
                var state = _asyncOperation.If(i => i != 3).Do(() => wasCalled = true);

                var res = await _asyncOperation.ExecuteAsync(1);

                Assert.Same(_parent, state);
                Assert.Null(res);
                Assert.True(wasCalled);
            }

            [Fact]
            public async Task InvokeShouldCallFuncAndPropagateReturnedData()
            {
                var res = 0;
                _asyncOperation.Invoke(i => i + 1).Do(i => res = i);

                await _asyncOperation.ExecuteAsync(1);
                Assert.Equal(2, res);
            }

            [Fact]
            public async Task InvokeShouldCallAction()
            {
                var res = 0;
                _asyncOperation.Invoke(new Action<int>(i => res = 3)).Do(() => { });

                await _asyncOperation.ExecuteAsync(3);
                Assert.Equal(3, res);
            }

            [Fact]
            public async Task IfThenGoToShouldReturnNewStateIfPredicateIsTrue()
            {
                var wasCalled = false;
                var state = _asyncOperation.IfThenGoTo(i => i == 3, States.State1).Do(() => wasCalled = true);

                var res = await _asyncOperation.ExecuteAsync(3);

                Assert.Same(_parent, state);
                Assert.Equal(States.State1, res.StateId);
                Assert.False(wasCalled);
            }

            [Fact]
            public async Task IfThenGoToShouldContinueChainIfPredicateIsFalse()
            {
                var wasCalled = false;
                var state = _asyncOperation.IfThenGoTo(i => i == 3, States.State1).Do(() => wasCalled = true);

                var res = await _asyncOperation.ExecuteAsync(1);

                Assert.Same(_parent, state);
                Assert.Null(res);
                Assert.True(wasCalled);
            }
        }

        public class TargetStatesTests : BaseStateTest<States>
        {
            private readonly AsyncOperation<States, string> _asyncOperation;

            public TargetStatesTests()
            {
                _asyncOperation = new AsyncOperation<States, string>(CreateState(States.None), "Test description");
            }

            [Fact]
            public void DoHandlerShouldReturnEmptyTargetStates()
            {
                _asyncOperation.Do(() => { });
                var targetStates = _asyncOperation.GetTargetStates(Enumerable.Empty<string>());

                Assert.True(!targetStates.Any());
            }

            [Fact]
            public void GoToHandlerShouldReturnTargetState()
            {
                var state = CreateState(States.State1);
                _asyncOperation.GoTo(state.StateId);
                var targetStates = _asyncOperation.GetTargetStates(Enumerable.Empty<string>());

                AssertTargetStatesEqual(
                    new[] { new TargetState<States, string>(Enumerable.Empty<string>(), state) },
                    targetStates);
            }

            [Fact]
            public void InvokeHandlerShouldNotAffectTargetStates()
            {
                var state = CreateState(States.State1);
                _asyncOperation.Invoke(() => true).GoTo(state.StateId);
                var targetStates = _asyncOperation.GetTargetStates(Enumerable.Empty<string>());

                AssertTargetStatesEqual(
                    new[] { new TargetState<States, string>(Enumerable.Empty<string>(), state) },
                    targetStates);
            }

            [Fact]
            public void InvokeHandlerShouldNotAffectTargetStates2()
            {
                var state = CreateState(States.State1);
                _asyncOperation.Invoke(() => { }).GoTo(state.StateId);
                var targetStates = _asyncOperation.GetTargetStates(Enumerable.Empty<string>());

                AssertTargetStatesEqual(
                    new[] { new TargetState<States, string>(Enumerable.Empty<string>(), state) },
                    targetStates);
            }

            [Fact]
            public void IfHandlerShouldAddConditionToTargetStates()
            {
                var state = CreateState(States.State1);
                _asyncOperation.If(() => true, "Condition 1").GoTo(state.StateId);
                var targetStates = _asyncOperation.GetTargetStates(Enumerable.Empty<string>());

                AssertTargetStatesEqual(
                    new[] { new TargetState<States, string>(new[] { "Condition 1" }, state) },
                    targetStates);
            }

            [Fact]
            public void IfHandlerWithNullDescriptionShouldNotAddConditionToTargetStates()
            {
                var state = CreateState(States.State1);
                _asyncOperation.If(() => true).GoTo(state.StateId);
                var targetStates = _asyncOperation.GetTargetStates(Enumerable.Empty<string>());

                AssertTargetStatesEqual(
                    new[] { new TargetState<States, string>(Enumerable.Empty<string>(), state) },
                    targetStates);
            }

            [Fact]
            public void IfThenGoToHandlerShouldAddConditionOnlyToItsTargetState()
            {
                var state = CreateState(States.State1);
                var state2 = CreateState(States.None);
                _asyncOperation.IfThenGoTo(() => true, state2.StateId, "Condition 1").GoTo(state.StateId);
                var targetStates = _asyncOperation.GetTargetStates(Enumerable.Empty<string>());

                AssertTargetStatesEqual(
                    new[]
                    {
                        new TargetState<States, string>(new[] { "Condition 1" }, state2),
                        new TargetState<States, string>(Enumerable.Empty<string>(), state)
                    },
                    targetStates);
            }

            [Fact]
            public void IfThenGoToHandlerWithNullDescriptionShouldNotAddConditionToItsTargetState()
            {
                var state = CreateState(States.State1);
                var state2 = CreateState(States.None);
                _asyncOperation.IfThenGoTo(() => true, state2.StateId).GoTo(state.StateId);
                var targetStates = _asyncOperation.GetTargetStates(Enumerable.Empty<string>());

                AssertTargetStatesEqual(
                    new[]
                    {
                        new TargetState<States, string>(Enumerable.Empty<string>(), state2),
                        new TargetState<States, string>(Enumerable.Empty<string>(), state)
                    },
                    targetStates);
            }
        }

        public class TargetStatesTestsWithData : BaseStateTest<States>
        {
            private readonly AsyncOperation<States, string, bool> _asyncOperation;

            public TargetStatesTestsWithData()
            {
                _asyncOperation = new AsyncOperation<States, string, bool>(
                    CreateState(States.None),
                    "Test description");
            }

            [Fact]
            public void DoHandlerShouldReturnEmptyTargetStates()
            {
                _asyncOperation.Do(_ => { });
                var targetStates = _asyncOperation.GetTargetStates(Enumerable.Empty<string>());

                Assert.True(!targetStates.Any());
            }

            [Fact]
            public void InvokeHandlerShouldNotAffectTargetStates()
            {
                var state = CreateState(States.State1);
                _asyncOperation.Invoke(_ => true).GoTo(state.StateId);
                var targetStates = _asyncOperation.GetTargetStates(Enumerable.Empty<string>());

                AssertTargetStatesEqual(
                    new[] { new TargetState<States, string>(Enumerable.Empty<string>(), state) },
                    targetStates);
            }

            [Fact]
            public void InvokeHandlerShouldNotAffectTargetStates2()
            {
                var state = CreateState(States.State1);
                _asyncOperation.Invoke(_ => { }).GoTo(state.StateId);
                var targetStates = _asyncOperation.GetTargetStates(Enumerable.Empty<string>());

                AssertTargetStatesEqual(
                    new[] { new TargetState<States, string>(Enumerable.Empty<string>(), state) },
                    targetStates);
            }

            [Fact]
            public void IfHandlerShouldAddConditionToTargetStates()
            {
                var state = CreateState(States.State1);
                _asyncOperation.If(_ => true, "Condition 1").GoTo(state.StateId);
                var targetStates = _asyncOperation.GetTargetStates(Enumerable.Empty<string>());

                AssertTargetStatesEqual(
                    new[] { new TargetState<States, string>(new[] { "Condition 1" }, state) },
                    targetStates);
            }

            [Fact]
            public void IfHandlerWithNullDescriptionShouldNotAddConditionToTargetStates()
            {
                var state = CreateState(States.State1);
                _asyncOperation.If(_ => true).GoTo(state.StateId);
                var targetStates = _asyncOperation.GetTargetStates(Enumerable.Empty<string>());

                AssertTargetStatesEqual(
                    new[] { new TargetState<States, string>(Enumerable.Empty<string>(), state) },
                    targetStates);
            }

            [Fact]
            public void IfThenGoToHandlerShouldAddConditionOnlyToItsTargetState()
            {
                var state = CreateState(States.State1);
                var state2 = CreateState(States.None);
                _asyncOperation.IfThenGoTo(_ => true, state2.StateId, "Condition 1").GoTo(state.StateId);
                var targetStates = _asyncOperation.GetTargetStates(Enumerable.Empty<string>());

                AssertTargetStatesEqual(
                    new[]
                    {
                        new TargetState<States, string>(new[] { "Condition 1" }, state2),
                        new TargetState<States, string>(Enumerable.Empty<string>(), state)
                    },
                    targetStates);
            }

            [Fact]
            public void IfThenGoToHandlerWithNullDescriptionShouldNotAddConditionToItsTargetState()
            {
                var state = CreateState(States.State1);
                var state2 = CreateState(States.None);
                _asyncOperation.IfThenGoTo(_ => true, state2.StateId).GoTo(state.StateId);
                var targetStates = _asyncOperation.GetTargetStates(Enumerable.Empty<string>());

                AssertTargetStatesEqual(
                    new[]
                    {
                        new TargetState<States, string>(Enumerable.Empty<string>(), state2),
                        new TargetState<States, string>(Enumerable.Empty<string>(), state)
                    },
                    targetStates);
            }
        }
    }
}
