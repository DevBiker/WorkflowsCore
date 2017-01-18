using System;
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
    }
}
