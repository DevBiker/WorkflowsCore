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
                var state = _asyncOperation.GoTo(newState);

                var res = await _asyncOperation.ExecuteAsync();

                Assert.Same(_parent, state);
                Assert.Same(newState, res);
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
            public async Task IfShouldNotInterruptChainIfPredicateIsFalse()
            {
                var wasCalled = false;
                var state = _asyncOperation.If(i => i != 3).Do(() => wasCalled = true);

                var res = await _asyncOperation.ExecuteAsync(1);

                Assert.Same(_parent, state);
                Assert.Null(res);
                Assert.True(wasCalled);
            }
        }
    }
}
