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

        public class AsyncOperationWithoutDataTests
        {
            private readonly AsyncOperation<States> _asyncOperation;
            private readonly State<States> _parent;

            public AsyncOperationWithoutDataTests()
            {
                _parent = new State<States>(States.None);
                _asyncOperation = new AsyncOperation<States>(_parent, "Test description");
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
                var newState = new State<States>(States.State1);
                var state = _asyncOperation.GoTo(newState);

                var res = await _asyncOperation.ExecuteAsync();

                Assert.Same(_parent, state);
                Assert.Same(newState, res);
            }
        }

        public class AsyncOperationWithDataTests
        {
            private readonly AsyncOperation<States, int> _asyncOperation;
            private readonly State<States> _parent;

            public AsyncOperationWithDataTests()
            {
                _parent = new State<States>(States.None);
                _asyncOperation = new AsyncOperation<States, int>(_parent, "Test description");
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
        }
    }
}
