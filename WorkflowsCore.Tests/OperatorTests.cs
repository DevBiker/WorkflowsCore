using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class OperatorTests
    {
        private enum States
        {
            // ReSharper disable once UnusedMember.Local
            None,
            Due
        }

        [Fact]
        public async Task AnyShouldWaitForAnyTaskIsCompleted()
        {
            Task task = null;
            await new TestWorkflow().WaitForAny(
                () =>
                {
                    task = Task.Delay(1);
                    return task;
                },
                () => Task.Delay(1000, Utilities.CurrentCancellationToken));

            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        [Fact]
        public async Task AnyShouldReturnIndexOfFirstCompletedNonOptionalTask()
        {
            var index = await new TestWorkflow().WaitForAny(
                () => Task.Delay(1000, Utilities.CurrentCancellationToken),
                () => Task.Delay(1),
                () => Task.Delay(100));

            Assert.Equal(1, index);
        }

        [Fact]
        public async Task AnyShouldWaitUntilAllOtherTasksAreCompletedOrCanceled()
        {
            Task task1 = null;
            Task task2 = null;
            Task task3 = null;
            await new TestWorkflow().WaitForAny(
                () => task1 = Task.Delay(1000, Utilities.CurrentCancellationToken),
                () => task2 = Task.Delay(1),
                () => task3 = Task.Delay(100));

            Assert.Equal(TaskStatus.Canceled, task1.Status);
            Assert.Equal(TaskStatus.RanToCompletion, task2.Status);
            Assert.Equal(TaskStatus.RanToCompletion, task3.Status);
        }

        [Fact]
        public async Task AnyShouldMakeResultingTaskAsFaultedIfAnyTaskIsFaulted()
        {
            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => new TestWorkflow().WaitForAny(() => Task.Run(() => { throw new InvalidOperationException(); })));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public async Task AnyInCaseOfFaultShouldWaitUntilAllOtherTasksAreCompletedOrCanceled()
        {
            Task task1 = null;
            Task task2 = null;
            try
            {
                await new TestWorkflow().WaitForAny(
                    () => task1 = Task.Delay(1000, Utilities.CurrentCancellationToken),
                    () => task2 = Task.Delay(100),
                    () => Task.Run(() => { throw new InvalidOperationException(); }));
            }
            catch (InvalidOperationException)
            {
            }
            
            Assert.Equal(TaskStatus.Canceled, task1?.Status);
            Assert.Equal(TaskStatus.RanToCompletion, task2?.Status);
        }

        [Fact]
        public async Task AnyInCaseOfFaultOfTaskCreationShouldWaitUntilAllOtherTasksAreCompletedOrCanceled()
        {
            Task task1 = null;
            Task task2 = null;
            try
            {
                await new TestWorkflow().WaitForAny(
                    () => task1 = Task.Delay(1000, Utilities.CurrentCancellationToken),
                    () => task2 = Task.Delay(100),
                    () => { throw new InvalidOperationException(); });
            }
            catch (InvalidOperationException)
            {
            }

            Assert.Equal(TaskStatus.Canceled, task1?.Status);
            Assert.Equal(TaskStatus.RanToCompletion, task2?.Status);
        }

        [Fact]
        public async Task AnyShouldMakeResultingTaskAsFaultedIfAnyOptionalTaskIsFaulted()
        {
            var testWorkflow = new TestWorkflow();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => testWorkflow.WaitForAny(
                    () => testWorkflow.Optional(Task.Run(() => { throw new InvalidOperationException(); }))));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public async Task AnyShouldWaitForAnyNonOptionalTaskIsCompleted()
        {
            Task task = null;
            Task optionalTask = null;
            var testWorkflow = new TestWorkflow();
            await testWorkflow.WaitForAny(
                () =>
                {
                    optionalTask = Task.Delay(1);
                    return testWorkflow.Optional(optionalTask);
                },
                () =>
                {
                    task = Task.Delay(50);
                    return task;
                });

            Assert.Equal(TaskStatus.RanToCompletion, optionalTask.Status);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        [Fact]
        public async Task IfOptionalTaskIsCanceledThenResultingTaskIsCanceled()
        {
            var testWorkflow = new TestWorkflow();
            var tsc = new TaskCompletionSource<bool>();
            tsc.SetCanceled();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => testWorkflow.Optional(tsc.Task));

            Assert.IsType<TaskCanceledException>(ex);
        }

        [Fact]
        public async Task AnyShouldCancelOtherTasksIfAnyNonOptionalTaskIsCompleted()
        {
            var cts = new CancellationTokenSource();
            await Utilities.SetCurrentCancellationTokenTemporarily(
                cts.Token,
                async () =>
                {
                    Task task = null;
                    Task optionalTask = null;
                    var testWorkflow = new TestWorkflow();
                    var token = default(CancellationToken);
                    await testWorkflow.WaitForAny(
                        () =>
                        {
                            Assert.NotEqual(cts.Token, Utilities.CurrentCancellationToken);
                            token = Utilities.CurrentCancellationToken;
                            return Task.Delay(1, Utilities.CurrentCancellationToken);
                        },
                        () =>
                        {
                            optionalTask = Task.Delay(50, Utilities.CurrentCancellationToken);
                            return testWorkflow.Optional(optionalTask);
                        },
                        () =>
                        {
                            task = Task.Delay(50, Utilities.CurrentCancellationToken);
                            return task;
                        });

                    Assert.Equal(cts.Token, Utilities.CurrentCancellationToken);
                    Assert.False(cts.IsCancellationRequested);
                    Assert.True(token.IsCancellationRequested);
                    Assert.Equal(TaskStatus.Canceled, task.Status);
                    Assert.Equal(TaskStatus.Canceled, optionalTask.Status);
                });
        }

        [Fact]
        public async Task AnyShouldNotStartOtherTasksIfAnyNonOptionalTaskIsCompletedImmediately()
        {
            var cts = new CancellationTokenSource();
            await Utilities.SetCurrentCancellationTokenTemporarily(
                cts.Token,
                async () =>
                {
                    var testWorkflow = new TestWorkflow();
                    await testWorkflow.WaitForAny(
                        () => testWorkflow.Optional(Task.CompletedTask),
                        () => Task.CompletedTask,
                        () =>
                        {
                            Assert.True(false);
                            return Task.CompletedTask;
                        });
                });
        }

        [Fact]
        public async Task AnyShouldCancelAllTasksIfWorkflowIsCanceled()
        {
            var cts = new CancellationTokenSource();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => Utilities.SetCurrentCancellationTokenTemporarily(
                    cts.Token,
                    async () =>
                    {
                        Task task = null;
                        var testWorkflow = new TestWorkflow();
#pragma warning disable 4014
                        Task.Delay(1).ContinueWith(_ => cts.Cancel());
#pragma warning restore 4014
                        try
                        {
                            await testWorkflow.WaitForAny(
                                () =>
                                {
                                    task = Task.Delay(100, Utilities.CurrentCancellationToken);
                                    return task;
                                });
                        }
                        catch (TaskCanceledException)
                        {
                            // ReSharper disable once PossibleNullReferenceException
                            Assert.Equal(TaskStatus.Canceled, task.Status);
                            throw;
                        }
                    }));

            Assert.IsType<TaskCanceledException>(ex);
        }

        [Fact]
        public async Task AnyShouldBeCanceledImmediatelyIfWorkflowIsAlreadyCanceled()
        {
            var cts = new CancellationTokenSource();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => Utilities.SetCurrentCancellationTokenTemporarily(
                    cts.Token,
                    async () =>
                    {
                        var testWorkflow = new TestWorkflow();
                        cts.Cancel();
                        await testWorkflow.WaitForAny(
                            () =>
                            {
                                Assert.True(false);
                                return Task.CompletedTask;
                            });
                    }));

            Assert.IsType<TaskCanceledException>(ex);
        }

        [Fact]
        public async Task WaitForActionShouldWaitUntilSpecifiedActionExecuted()
        {
            var testWorkflow = new TestWorkflow(new CancellationTokenSource(100).Token, doInit: false);
            testWorkflow.StartWorkflow();
            await testWorkflow.DoWorkflowTaskAsync(
                async w =>
                {
                    Task t = null;
                    var parameters = new Dictionary<string, object> { ["Id"] = 3 };
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    Task.Delay(1)
                        .ContinueWith(_ => t = testWorkflow.ExecuteActionAsync("Contacted", parameters));
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    await testWorkflow.WaitForAction("Contacted 2"); // Wait via synonym
                    await t;

                    Assert.Equal("Contacted", testWorkflow.Action);
                    Assert.True(parameters.SequenceEqual(testWorkflow.Parameters.Data));
                }).Unwrap();

            await testWorkflow.CompletedTask;
        }

        [Fact]
        public async Task WaitForActionShouldBeCanceledIfWorkflowIsCanceled()
        {
            var testWorkflow = new TestWorkflow(new CancellationTokenSource(100).Token);

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => testWorkflow.DoWorkflowTaskAsync(
                    async w =>
                    {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                        Task.Delay(1).ContinueWith(_ => testWorkflow.CancelWorkflow());
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                        await testWorkflow.WaitForAction("Contacted");
                        Assert.True(false);
                    },
                    forceExecution: true).Unwrap());

            Assert.IsType<TaskCanceledException>(ex);
        }

        [Fact]
        public async Task WaitForActionWithCheckWasExecutedShouldReturnImmediatelyIfActionWasExecutedBefore()
        {
            var testWorkflow = new TestWorkflow(new CancellationTokenSource(100).Token, doInit: false);
            testWorkflow.StartWorkflow();
            await testWorkflow.DoWorkflowTaskAsync(
                async w =>
                {
                    await testWorkflow.ExecuteActionAsync("Contacted");
                    await testWorkflow.WaitForActionWithWasExecutedCheck("Contacted");

                    Assert.Equal("Contacted", testWorkflow.Action);
                }).Unwrap();

            await testWorkflow.CompletedTask;
        }

        [Fact]
        public async Task WaitForActionShouldReturnImmediatelyIfStateIsSpecifiedAndThatStateIsFirstInStatesHistory()
        {
            var testWorkflow = new TestWorkflowWithState(
                () => new WorkflowRepository(),
                new CancellationTokenSource(100).Token);
            await testWorkflow.DoWorkflowTaskAsync(
                async w =>
                {
                    w.SetTransientData("StatesHistory", new[] { States.Due });
                    await testWorkflow.WaitForAction("Contacted", state: States.Due);

                    Assert.Null(testWorkflow.Action);
                    Assert.Null(testWorkflow.Parameters);
                },
                forceExecution: true).Unwrap();
        }

        [Fact]
        public async Task WaitForActionShouldBeCanceledImmediatelyIfWorkflowIsAlreadyCanceled()
        {
            var testWorkflow = new TestWorkflowWithState(() => new WorkflowRepository());

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => testWorkflow.DoWorkflowTaskAsync(
                    async w =>
                    {
                        testWorkflow.CancelWorkflow();
                        w.SetTransientData("StatesHistory", new[] { States.Due });
                        await testWorkflow.WaitForAction("Contacted", state: States.Due);
                        Assert.True(false);
                    },
                    forceExecution: true).Unwrap());

            Assert.IsType<TaskCanceledException>(ex);
        }

        [Fact]
        public async Task WaitForStateShouldWaitUntilWorkflowEntersSpecifiedState()
        {
            var testWorkflow = new TestWorkflowWithState(
                () => new WorkflowRepository(),
                new CancellationTokenSource(100).Token);
            await testWorkflow.DoWorkflowTaskAsync(
                async w =>
                {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    Task.Delay(1).ContinueWith(_ => testWorkflow.SetState(States.Due));
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                    await testWorkflow.WaitForState(States.Due);
                },
                forceExecution: true).Unwrap();
        }

        [Fact]
        public async Task WaitForStateCalledWithAnyStateAsTrueShouldWaitUntilWorkflowStateIsChanged()
        {
            var testWorkflow = new TestWorkflowWithState(
                () => new WorkflowRepository(),
                new CancellationTokenSource(100).Token);
            await testWorkflow.DoWorkflowTaskAsync(
                async w =>
                {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    Task.Delay(1).ContinueWith(_ => testWorkflow.SetState(States.Due));
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                    await testWorkflow.WaitForState(anyState: true);
                    Assert.Equal(States.Due, testWorkflow.State);
                },
                forceExecution: true).Unwrap();
        }

        [Fact]
        public async Task WaitForStateShouldBeCompletedImmediatelyIfWorkflowIsAlreayInRequiredState()
        {
            var testWorkflow = new TestWorkflowWithState(
                () => new WorkflowRepository(),
                new CancellationTokenSource(100).Token);
            await testWorkflow.DoWorkflowTaskAsync(
                async w =>
                {
                    testWorkflow.SetState(States.Due);

                    await testWorkflow.WaitForState(States.Due);
                },
                forceExecution: true).Unwrap();
        }

        [Fact]
        public async Task WaitForStateflowShouldBeCanceledImmediatelyIfWorkflowIsAlreadyCanceled()
        {
            var testWorkflow = new TestWorkflowWithState(() => new WorkflowRepository());
            
            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => testWorkflow.DoWorkflowTaskAsync(
                    async w =>
                    {
                        testWorkflow.CancelWorkflow();
                        await testWorkflow.WaitForState(States.Due);
                        Assert.True(false);
                    },
                    forceExecution: true).Unwrap());

            Assert.IsType<TaskCanceledException>(ex);
        }

        [Fact]
        public async Task WaitForStateflowShouldBeCanceledIfWorkflowIsCanceled()
        {
            var testWorkflow = new TestWorkflowWithState(() => new WorkflowRepository());

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => testWorkflow.DoWorkflowTaskAsync(
                    async w =>
                    {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                        Task.Delay(1).ContinueWith(_ => testWorkflow.CancelWorkflow());
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                        await testWorkflow.WaitForState(States.Due);
                        Assert.True(false);
                    },
                    forceExecution: true).Unwrap());

            Assert.IsType<TaskCanceledException>(ex);
        }

        [Fact]
        public async Task ThenShouldRunActionWhenTaskIsCompleted()
        {
            var isRun = false;
            await Task.Delay(1).Then(() => isRun = true);
            Assert.True(isRun);
        }

        [Fact]
        public async Task ThenShouldNotRunActionWhenTaskIsCanceled()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            
            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => Task.Delay(1, cts.Token).Then(() => Assert.True(false)));

            Assert.IsType<TaskCanceledException>(ex);
        }

        [Fact]
        public async Task ThenShouldReturnFaultedTaskWhenInputTaskFaulted()
        {
            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => Task.Run(() => { throw new InvalidOperationException(); }).Then(() => Assert.True(false)));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public async Task WaitForTimeoutShouldThrowTimeoutExceptionIfTimeoutOccurrs()
        {
            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => Task.Delay(100).WaitWithTimeout(1));

            Assert.IsType<TimeoutException>(ex);
        }

        [Fact]
        public async Task WaitForTimeoutShouldWaitUntilTaskFinished()
        {
            var task = Task.Delay(10);
            await task.WaitWithTimeout(100);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        [Fact]
        public async Task WaitForTimeout2ShouldThrowTimeoutExceptionIfTimeoutOccurrs()
        {
            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => new Func<Task<int>>(
                    async () =>
                    {
                        await Task.Delay(100);
                        return 1;
                    })().WaitWithTimeout(1));

            Assert.IsType<TimeoutException>(ex);
        }

        [Fact]
        public async Task WaitForTimeout2ShouldWaitUntilTaskFinished()
        {
            var task = new Func<Task<int>>(
                async () =>
                {
                    await Task.Delay(10);
                    return 1;
                })();
            await task.WaitWithTimeout(100);
            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        }

        private sealed class TestWorkflow : WorkflowBase
        {
            public TestWorkflow()
            {
            }

            public TestWorkflow(CancellationToken parentCancellationToken, bool doInit = true) 
                : base(() => new WorkflowRepository(), !doInit, parentCancellationToken)
            {
                if (doInit)
                {
                    OnInit();
                    SetStateInitialized();
                }
            }

            public string Action { get; private set; }

            public NamedValues Parameters { get; private set; }

            protected override void OnActionsInit()
            {
                base.OnActionsInit();
                ConfigureAction(
                    "Contacted",
                    p =>
                    {
                        Action = "Contacted";
                        Parameters = p;
                    },
                    synonyms: new[] { "Contacted 2" });
            }

            protected override Task RunAsync() => Task.Delay(50);
        }

        private sealed class TestWorkflowWithState : WorkflowBase<States>
        {
            public TestWorkflowWithState(
                Func<IWorkflowStateRepository> workflowRepoFactory,
                CancellationToken parentCancellationToken = default(CancellationToken))
                : base(workflowRepoFactory, parentCancellationToken)
            {
                OnInit();
            }

            public string Action { get; private set; }

            public NamedValues Parameters { get; private set; }

            public new States State => base.State;

            // ReSharper disable once UnusedParameter.Local
            public new void SetState(States state) => base.SetState(state);

            protected override void OnInit()
            {
                base.OnInit();
                ConfigureAction(
                    "Contacted",
                    p =>
                    {
                        Action = "Contacted";
                        Parameters = p;
                    });
            }

            protected override Task RunAsync()
            {
                throw new NotImplementedException();
            }

            protected override void OnStatesInit()
            {
                ConfigureState(States.Due);
            }
        }

        private class WorkflowRepository : IWorkflowStateRepository
        {
            public void SaveWorkflowData(WorkflowBase workflow)
            {
            }

            public void MarkWorkflowAsCompleted(WorkflowBase workflow)
            {
            }

            public void MarkWorkflowAsFailed(WorkflowBase workflow, Exception exception)
            {
                throw new NotImplementedException();
            }

            public void MarkWorkflowAsCanceled(WorkflowBase workflow)
            {
                throw new NotImplementedException();
            }

            public void MarkWorkflowAsSleeping(WorkflowBase workflow)
            {
                throw new NotImplementedException();
            }

            public void MarkWorkflowAsInProgress(WorkflowBase workflow)
            {
                throw new NotImplementedException();
            }
        }
    }
}
