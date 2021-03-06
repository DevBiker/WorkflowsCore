﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class OperatorTests
    {
        public enum States
        {
            // ReSharper disable once UnusedMember.Local
            None,
            Due
        }

        public class WaitForAnyTests
        {
            private readonly WorkflowBase _workflow = new TestWorkflow();

            [Fact]
            public async Task AnyShouldWaitForAnyTaskIsCompleted()
            {
                Task task = null;
                await _workflow.WaitForAny(
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
                var index = await _workflow.WaitForAny(
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
                await _workflow.WaitForAny(
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
                    () => _workflow.WaitForAny(() => Task.Run(() => { throw new InvalidOperationException(); })));

                Assert.IsType<InvalidOperationException>(ex);
            }

            [Fact]
            public async Task AnyInCaseOfFaultShouldWaitUntilAllOtherTasksAreCompletedOrCanceled()
            {
                Task task1 = null;
                Task task2 = null;

                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(
                    () => _workflow.WaitForAny(
                        () => task1 = Task.Delay(1000, Utilities.CurrentCancellationToken),
                        () => task2 = Task.Delay(100),
                        () => Task.Run(() => { throw new InvalidOperationException(); })));

                Assert.IsType<InvalidOperationException>(ex);
                Assert.Equal(TaskStatus.Canceled, task1?.Status);
                Assert.Equal(TaskStatus.RanToCompletion, task2?.Status);
            }

            [Fact]
            public async Task AnyInCaseOfFaultOfTaskCreationShouldWaitUntilAllOtherTasksAreCompletedOrCanceled()
            {
                Task task1 = null;
                Task task2 = null;

                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(
                    () => _workflow.WaitForAny(
                        () => task1 = Task.Delay(1000, Utilities.CurrentCancellationToken),
                        () => task2 = Task.Delay(100),
                        () => { throw new InvalidOperationException(); }));

                Assert.IsType<InvalidOperationException>(ex);
                Assert.Equal(TaskStatus.Canceled, task1?.Status);
                Assert.Equal(TaskStatus.RanToCompletion, task2?.Status);
            }

            [Fact]
            public async Task AnyShouldMakeResultingTaskAsFaultedIfAnyOptionalTaskIsFaulted()
            {
                var testWorkflow = _workflow;

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
                var testWorkflow = _workflow;
                await testWorkflow.WaitForAny(
                    () =>
                    {
                        optionalTask = Task.Delay(1);
                        return testWorkflow.Optional(optionalTask);
                    },
                    () =>
                    {
                        task = Task.Delay(100);
                        return task;
                    });

                Assert.Equal(TaskStatus.RanToCompletion, optionalTask.Status);
                Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            }

            [Fact]
            public async Task IfOptionalTaskIsCanceledThenResultingTaskIsCanceled()
            {
                var testWorkflow = _workflow;
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
                        var testWorkflow = _workflow;
                        var token = default(CancellationToken);
                        await testWorkflow.WaitForAny(
                            () =>
                            {
                                Assert.NotEqual(cts.Token, Utilities.CurrentCancellationToken);
                                token = Utilities.CurrentCancellationToken;
                                return Task.Delay(10, Utilities.CurrentCancellationToken);
                            },
                            () =>
                            {
                                optionalTask = Task.Delay(1000, Utilities.CurrentCancellationToken);
                                return testWorkflow.Optional(optionalTask);
                            },
                            () =>
                            {
                                task = Task.Delay(1000, Utilities.CurrentCancellationToken);
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
            public async Task AnyShouldWaitUntilAllOtherTasksAreCompletedOrCanceledWhenWorkflowIsCanceled()
            {
                Task task1 = null;
                Task task2 = null;
                Task task3 = null;
                var cts = new CancellationTokenSource();

                // ReSharper disable MethodSupportsCancellation
                var task = Record.ExceptionAsync(
                    () => Utilities.SetCurrentCancellationTokenTemporarily(
                        cts.Token,
                        () => _workflow.WaitForAny(
                            () => task1 = Task.Delay(1000, Utilities.CurrentCancellationToken),
                            () => task2 = Task.Delay(1),
                            () => task3 = Task.Delay(100))));

                // ReSharper restore MethodSupportsCancellation
                cts.Cancel();

                // ReSharper disable once PossibleNullReferenceException
                var ex = await task;

                Assert.IsType<TaskCanceledException>(ex);
                Assert.Equal(TaskStatus.Canceled, task1.Status);
                Assert.Equal(TaskStatus.RanToCompletion, task2.Status);
                Assert.Equal(TaskStatus.RanToCompletion, task3.Status);
            }

            [Fact]
            public async Task AnyShouldNotStartOtherTasksIfAnyNonOptionalTaskIsCompletedImmediately()
            {
                var cts = new CancellationTokenSource();
                await Utilities.SetCurrentCancellationTokenTemporarily(
                    cts.Token,
                    async () =>
                    {
                        var testWorkflow = _workflow;
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
                            var testWorkflow = _workflow;
                            try
                            {
                                var t = testWorkflow.WaitForAny(
                                    () => task = Task.Delay(100, Utilities.CurrentCancellationToken));

                                Assert.NotEqual(TaskStatus.Canceled, t.Status);
                                cts.Cancel();
                                await t;
                            }
                            catch (TaskCanceledException)
                            {
                                Assert.Equal(TaskStatus.Canceled, task?.Status);
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
                            var testWorkflow = _workflow;
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
            public async Task AnyShouldFailIfAnyChildTaskCanceled()
            {
                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(
                    () => _workflow.WaitForAny(() => Task.Run(() => { throw new TaskCanceledException(); })));

                Assert.IsType<InvalidOperationException>(ex);
            }

            [Fact]
            public async Task AnyShouldFailIfAnyOptionalChildTaskCanceled()
            {
                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(
                    () => _workflow.WaitForAny(
                        () => _workflow.Optional(Task.Run(() => { throw new TaskCanceledException(); }))));

                Assert.IsType<InvalidOperationException>(ex);
            }
        }

        public class WaitForActionTests : BaseWorkflowTest<TestWorkflow>
        {
            public WaitForActionTests() => Workflow = new TestWorkflow(doInit: false);

            [Fact]
            public async Task WaitForActionShouldWaitUntilSpecifiedActionExecuted()
            {
                StartWorkflow();
                await Workflow.DoWorkflowTaskAsync(
                    async w =>
                    {
                        var parameters = new Dictionary<string, object> { ["Id"] = 3 };
                        var t = Workflow.WaitForAction("Contacted 2"); // Wait via synonym
                        Assert.NotEqual(TaskStatus.RanToCompletion, t.Status);
                        await Workflow.ExecuteActionAsync("Contacted", parameters);
                        await t;

                        Assert.Equal("Contacted", Workflow.Action);

                        parameters["Action"] = "Contacted";
                        Assert.Equal(parameters, Workflow.Parameters.Data);
                    }).Unwrap();

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task WaitForActionShouldBeCanceledIfWorkflowIsCanceled()
            {
                StartWorkflow();

                Task canceledTask = null;

                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(
                    () => Workflow.DoWorkflowTaskAsync(
                        async w =>
                        {
                            var t = Workflow.WaitForAction("Contacted");
                            Assert.NotEqual(TaskStatus.Canceled, t.Status);
                            canceledTask = CancelWorkflowAsync();
                            await t;
                        }).Unwrap());

                Assert.IsType<TaskCanceledException>(ex);
                await canceledTask;
            }

            [Fact]
            public async Task WaitForActionShouldExportOperationIfRequested()
            {
                StartWorkflow();
                await Workflow.DoWorkflowTaskAsync(
                    async w =>
                    {
                        var t = Workflow.WaitForAction("Contacted", exportOperation: true);
                        var actionTask = Workflow.ExecuteActionAsync("Contacted");
                        var parameters = await t;

                        var operation = parameters.GetDataField<IDisposable>("ActionOperation");
                        Assert.NotNull(operation);
                        var readyTask = Workflow.ReadyTask;
                        Assert.NotEqual(TaskStatus.RanToCompletion, readyTask.Status);

                        operation.Dispose();
                        await actionTask;
                        Assert.Equal(TaskStatus.RanToCompletion, readyTask.Status);
                    }).Unwrap();

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task MultipleWaitForActionsWithExportOperationShouldExecuteSequentially()
            {
                async Task WaitForAction(Task subTask)
                {
                    var parameters = await Workflow.WaitForAction("Contacted", exportOperation: true);
                    using (parameters.GetDataField<IDisposable>("ActionOperation"))
                    {
                        await subTask;
                    }
                }

                async Task AssertParentOperationCompletedAfterChildren(
                    TaskCompletionSource<bool> tcs1, Task t1, TaskCompletionSource<bool> tcs2, Task t2, Task t3)
                {
                    await Task.Delay(1);
                    tcs1.SetResult(true);
                    await Task.Delay(1);
                    Assert.NotEqual(TaskStatus.RanToCompletion, t3.Status);
                    await Task.Delay(1);
                    Assert.NotEqual(TaskStatus.RanToCompletion, t3.Status);
                    tcs2.SetResult(true);
                    await Task.Delay(1);
                    Assert.Equal(TaskStatus.RanToCompletion, t3.Status);
                    await t1;
                    await t2;
                    await t3;
                }

                StartWorkflow();
                await Workflow.DoWorkflowTaskAsync(
                    async w =>
                    {
                        var tcs1 = new TaskCompletionSource<bool>();
                        var t1 = WaitForAction(tcs1.Task);
                        var tcs2 = new TaskCompletionSource<bool>();
                        var t2 = WaitForAction(tcs2.Task);
                        var t3 = Workflow.ExecuteActionAsync("Contacted");
                        await AssertParentOperationCompletedAfterChildren(tcs1, t1, tcs2, t2, t3);

                        // Try in another order
                        tcs1 = new TaskCompletionSource<bool>();
                        t1 = WaitForAction(tcs1.Task);
                        tcs2 = new TaskCompletionSource<bool>();
                        t2 = WaitForAction(tcs2.Task);
                        t3 = Workflow.ExecuteActionAsync("Contacted");
                        await AssertParentOperationCompletedAfterChildren(tcs2, t2, tcs1, t1, t3);
                    }).Unwrap();

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task WaitForActionWithCheckWasExecutedShouldReturnImmediatelyIfActionWasExecutedBefore()
            {
                StartWorkflow();
                await Workflow.DoWorkflowTaskAsync(
                    async w =>
                    {
                        await Workflow.ExecuteActionAsync("Contacted");
                        var t = Workflow.WaitForActionWithWasExecutedCheck("Contacted");
                        Assert.Equal(TaskStatus.RanToCompletion, t.Status);
                        Assert.Equal("Contacted", Workflow.Action);
                    }).Unwrap();

                await CancelWorkflowAsync();
            }
        }

        public class StateRelatedTests
        {
            private readonly TestWorkflowWithState _workflow = new TestWorkflowWithState();

            [Fact]
            public async Task WaitForStateShouldWaitUntilWorkflowEntersSpecifiedState()
            {
                await _workflow.DoWorkflowTaskAsync(
                    async () =>
                    {
                        var t = _workflow.WaitForState(States.Due);
                        Assert.NotEqual(TaskStatus.RanToCompletion, t.Status);
                        _workflow.SetState(States.Due);
                        await t;
                    },
                    forceExecution: true).Unwrap();
            }

            [Fact]
            public async Task WaitForStateCalledWithAnyStateAsTrueShouldWaitUntilWorkflowStateIsChanged()
            {
                await _workflow.DoWorkflowTaskAsync(
                    async () =>
                    {
                        var t = _workflow.WaitForState(anyState: true);
                        Assert.NotEqual(TaskStatus.RanToCompletion, t.Status);
                        _workflow.SetState(States.Due);
                        await t;
                        Assert.Equal(States.Due, _workflow.State);
                    },
                    forceExecution: true).Unwrap();
            }

            [Fact]
            public async Task WaitForStateShouldBeCompletedImmediatelyIfWorkflowIsAlreayInRequiredState()
            {
                await _workflow.DoWorkflowTaskAsync(
                    () =>
                    {
                        _workflow.SetState(States.Due);

                        var t = _workflow.WaitForState(States.Due);
                        Assert.Equal(TaskStatus.RanToCompletion, t.Status);
                    },
                    forceExecution: true);
            }

            [Fact]
            public async Task WaitForStateflowShouldBeCanceledImmediatelyIfWorkflowIsAlreadyCanceled()
            {
                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(
                    () => _workflow.DoWorkflowTaskAsync(
                        async () =>
                        {
                            _workflow.CancelWorkflow();
                            var t = _workflow.WaitForState(States.Due);
                            Assert.Equal(TaskStatus.Canceled, t.Status);
                            await t;
                        },
                        forceExecution: true).Unwrap());

                Assert.IsType<TaskCanceledException>(ex);
            }

            [Fact]
            public async Task WaitForStateflowShouldBeCanceledIfWorkflowIsCanceled()
            {
                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(
                    () => _workflow.DoWorkflowTaskAsync(
                        async w =>
                        {
                            var t = _workflow.WaitForState(States.Due);
                            Assert.NotEqual(TaskStatus.Canceled, t.Status);
                            _workflow.CancelWorkflow();
                            await t;
                        },
                        forceExecution: true).Unwrap());

                Assert.IsType<TaskCanceledException>(ex);
            }
        }

        public class MiscTests
        {
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
                var ex = await Record.ExceptionAsync(() => Task.Delay(1000).WaitWithTimeout(1));

                Assert.IsType<TimeoutException>(ex);
            }

            [Fact]
            public async Task WaitForTimeoutShouldWaitUntilTaskFinished()
            {
                var task = Task.Delay(1);
                await task.WaitWithTimeout(1000);
                Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            }

            [Fact]
            public async Task WaitForTimeoutShouldWaitUntilTaskCanceled()
            {
                var task = Task.Delay(Timeout.Infinite, new CancellationTokenSource(1).Token);

                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(() => task.WaitWithTimeout(1000));
                Assert.IsType<TaskCanceledException>(ex);
                Assert.True(task.IsCanceled);
            }

            [Fact]
            public async Task WaitForTimeout2ShouldThrowTimeoutExceptionIfTimeoutOccurrs()
            {
                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(
                    () => new Func<Task<int>>(
                        async () =>
                        {
                            await Task.Delay(1000);
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
                        await Task.Delay(1);
                        return 1;
                    })();
                await task.WaitWithTimeout(1000);
                Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            }
        }

        public class WaitForReadyAndStartOperationTests : BaseWorkflowTest<TestWorkflow>
        {
            public WaitForReadyAndStartOperationTests()
            {
                Workflow = new TestWorkflow(doInit: false);
            }

            [Fact]
            public async Task WaitForReadyShouldStartOperationIfWorkflowIsReady()
            {
                StartWorkflow();
                await Workflow.StartedTask;

                Assert.Equal(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);
                using (await Workflow.WaitForReadyAndStartOperation())
                {
                    Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);
                }

                Assert.Equal(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task WaitForReadyShouldWaitUntilWorkflowReady()
            {
                StartWorkflow();
                var disposable = await Workflow.DoWorkflowTaskAsync(
                    () =>
                    {
                        Workflow.CreateOperation();
                        return Workflow.TryStartOperation();
                    });

                Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);
                var t = Workflow.WaitForReadyAndStartOperation();
                await Task.Delay(1);
                Assert.NotEqual(TaskStatus.RanToCompletion, t.Status);

                disposable.Dispose();
                await t;

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task InnerWaitForReadyShouldSucceed()
            {
                StartWorkflow();
                await Workflow.StartedTask;

                Assert.Equal(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);
                using (await Workflow.WaitForReadyAndStartOperation())
                {
                    using (await Workflow.WaitForReadyAndStartOperation())
                    {
                    }

                    Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);
                }

                Assert.Equal(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task WaitForReadyShouldBeCanceledIfParentCancellationTokenCanceled()
            {
                StartWorkflow();

                var cts = new CancellationTokenSource();

                await Workflow.DoWorkflowTaskAsync(() => Workflow.WaitForReadyAndStartOperation());

                var task = Utilities.SetCurrentCancellationTokenTemporarily(
                    cts.Token,
                    () => Workflow.WaitForReadyAndStartOperation());

                cts.Cancel();

                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(() => task);
                Assert.IsType<TaskCanceledException>(ex);

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task WaitForReadySimpleShouldWaitUntilWorkflowReady()
            {
                StartWorkflow();
                var disposable = await Workflow.DoWorkflowTaskAsync(
                    () =>
                    {
                        Workflow.CreateOperation();
                        return Workflow.TryStartOperation();
                    });

                Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);
                var t = Workflow.WaitForReady();
                await Task.Delay(1);
                Assert.NotEqual(TaskStatus.RanToCompletion, t.Status);

                disposable.Dispose();
                await t;

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task WaitForReadySimpleShouldBeCanceledIfParentCancellationTokenCanceled()
            {
                StartWorkflow();

                var cts = new CancellationTokenSource();

                await Workflow.DoWorkflowTaskAsync(() => Workflow.WaitForReadyAndStartOperation());

                var task = Utilities.SetCurrentCancellationTokenTemporarily(
                    cts.Token,
                    () => Workflow.WaitForReady());

                cts.Cancel();

                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(() => task);
                Assert.IsType<TaskCanceledException>(ex);

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task OnlySingleInnerOperationShouldBeActiveAtOnce()
            {
                StartWorkflow();

                using (await Workflow.WaitForReadyAndStartOperation())
                {
                    IDisposable innerOperation = null;

                    await Workflow.WaitForAny(
                        async () =>
                        {
                            innerOperation = await Workflow.WaitForReadyAndStartOperation();
                            await Task.Delay(1);
                        },
                        async () =>
                        {
                            using (await Workflow.WaitForReadyAndStartOperation())
                            {
                                throw new InvalidOperationException("This should never be executed");
                            }
                        });

                    innerOperation.Dispose();
                    Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);
                }

                Assert.Equal(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task SubsequentInnerOperationsShouldSucceed()
            {
                StartWorkflow();

                using (var operation = await Workflow.WaitForReadyAndStartOperation())
                {
                    using (await Workflow.WaitForReadyAndStartOperation())
                    {
                        await Task.Delay(1);
                    }

                    await operation.WaitForAllInnerOperationsCompletion();
                    Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);

                    using (await Workflow.WaitForReadyAndStartOperation())
                    {
                        await Task.Delay(1);
                    }

                    Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);
                    await operation.WaitForAllInnerOperationsCompletion();
                }

                Assert.Equal(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);

                await CancelWorkflowAsync();
            }
        }

        public class WaitForEventTests : BaseWorkflowTest<TestWorkflow>
        {
            public WaitForEventTests() => Workflow = new TestWorkflow(doInit: false);

            [Fact]
            public async Task WaitForEventShouldWaitUntilEventHappens()
            {
                StartWorkflow();
                await Workflow.DoWorkflowTaskAsync(
                    async w =>
                    {
                        var t = Workflow.WaitForEvent(e => e.EventName == WorkflowBase.ActionExecutedEvent && e.Parameters["Action"] == "Contacted");
                        Assert.NotEqual(TaskStatus.RanToCompletion, t.Status);
                        await Workflow.ExecuteActionAsync("Contacted");
                        await t;
                    }).Unwrap();

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task WaitForEventShouldBeCanceledIfWorkflowIsCanceled()
            {
                StartWorkflow();

                Task canceledTask = null;

                var ex = await Record.ExceptionAsync(
                    () => Workflow.DoWorkflowTaskAsync(
                        async w =>
                        {
                            var t = Workflow.WaitForEvent(e => e.EventName == WorkflowBase.ActionExecutedEvent && e.Parameters["Action"] == "Contacted");
                            Assert.NotEqual(TaskStatus.Canceled, t.Status);
                            canceledTask = CancelWorkflowAsync();
                            await t;
                        }).Unwrap());

                Assert.IsType<TaskCanceledException>(ex);
                await canceledTask;
            }

            [Fact]
            public async Task WaitForEventShouldHonorIgnorePastEventsOption()
            {
                StartWorkflow();
                await Workflow.ExecuteActionAsync("Contacted");
                await Workflow.DoWorkflowTaskAsync(
                    w =>
                    {
                        var t1 = Workflow.WaitForEvent(e => e.EventName == WorkflowBase.ActionExecutedEvent && e.Parameters["Action"] == "Contacted", ignorePastEvents: false);
                        Assert.Equal(TaskStatus.RanToCompletion, t1.Status);

                        var t2 = Workflow.WaitForEvent(e => e.EventName == WorkflowBase.ActionExecutedEvent && e.Parameters["Action"] == "Contacted", ignorePastEvents: true);
                        Assert.NotEqual(TaskStatus.RanToCompletion, t2.Status);
                    });

                await CancelWorkflowAsync();
            }
        }

        public class UnwrapTests : BaseWorkflowTest<TestWorkflowWithFailingAction>
        {
            public UnwrapTests() => Workflow = new TestWorkflowWithFailingAction();

            [Fact]
            public async Task UnwrapShouldReturnResultOnTargetTaskSuccess()
            {
                StartWorkflow();

                var res = await Workflow.ExecuteActionAsync<int>("Contacted").Unwrap(Workflow);

                Assert.Equal(1, res);

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task UnwrapShouldRethrowExceptionOnTargetTaskException()
            {
                StartWorkflow();

                var ex = await Record.ExceptionAsync(() => Workflow.ExecuteActionAsync<int>("BadAction").Unwrap(Workflow));

                Assert.IsType<ArgumentOutOfRangeException>(ex);
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task UnwrapShouldRethrowWorkflowCompletedTaskExceptionOnTargetTaskCancellation()
            {
                StartWorkflow();

                var ex = await Record.ExceptionAsync(() => Workflow.ExecuteActionAsync<int>("FailingAction").Unwrap(Workflow));

                Assert.IsType<NotSupportedException>(ex);
            }

            [Fact]
            public async Task UnwrapShouldThrowIoeOnTargetTaskCancellationIfWorkflowIsNotInCancellation()
            {
                StartWorkflow();

                var ex = await Record.ExceptionAsync(() => Workflow.ExecuteActionAsync<int>("CancelingAction").Unwrap(Workflow));

                Assert.IsType<InvalidOperationException>(ex);
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task UnwrapShouldReturnCanceledTaskOnTargetTaskCancellationIfWorkflowIsCanceled()
            {
                StartWorkflow();
                await Workflow.StartedTask;
                await CancelWorkflowAsync();

                var ex = await Record.ExceptionAsync(() => Workflow.ExecuteActionAsync<int>("Contacted").Unwrap(Workflow));

                Assert.IsType<TaskCanceledException>(ex);
            }

            [Fact]
            public async Task UnwrapShouldReturnCanceledTaskOnTargetTaskCancellationIfWorkflowIsCompleted()
            {
                StartWorkflow();

                var ex = await Record.ExceptionAsync(() => Workflow.ExecuteActionAsync<int>("CompleteAction").Unwrap(Workflow));

                Assert.IsType<TaskCanceledException>(ex);
                await WaitUntilWorkflowCompleted();
            }
        }

        public class Unwrap2Tests : BaseWorkflowTest<TestWorkflowWithFailingAction>
        {
            public Unwrap2Tests() => Workflow = new TestWorkflowWithFailingAction();

            [Fact]
            public async Task UnwrapShouldReturnResultOnTargetTaskSuccess()
            {
                StartWorkflow();

                await Workflow.ExecuteActionAsync("Contacted").Unwrap(Workflow);

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task UnwrapShouldRethrowExceptionOnTargetTaskException()
            {
                StartWorkflow();

                var ex = await Record.ExceptionAsync(() => Workflow.ExecuteActionAsync("BadAction").Unwrap(Workflow));

                Assert.IsType<ArgumentOutOfRangeException>(ex);
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task UnwrapShouldRethrowWorkflowCompletedTaskExceptionOnTargetTaskCancellation()
            {
                StartWorkflow();

                var ex = await Record.ExceptionAsync(() => Workflow.ExecuteActionAsync("FailingAction").Unwrap(Workflow));

                Assert.IsType<NotSupportedException>(ex);
            }

            [Fact]
            public async Task UnwrapShouldThrowIoeOnTargetTaskCancellationIfWorkflowIsNotInCancellation()
            {
                StartWorkflow();

                var ex = await Record.ExceptionAsync(() => Workflow.ExecuteActionAsync("CancelingAction").Unwrap(Workflow));

                Assert.IsType<InvalidOperationException>(ex);
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task UnwrapShouldReturnCanceledTaskOnTargetTaskCancellationIfWorkflowIsCanceled()
            {
                StartWorkflow();
                await Workflow.StartedTask;
                await CancelWorkflowAsync();

                var ex = await Record.ExceptionAsync(() => Workflow.ExecuteActionAsync("Contacted").Unwrap(Workflow));

                Assert.IsType<TaskCanceledException>(ex);
            }

            [Fact]
            public async Task UnwrapShouldReturnCanceledTaskOnTargetTaskCancellationIfWorkflowIsCompleted()
            {
                StartWorkflow();

                var ex = await Record.ExceptionAsync(() => Workflow.ExecuteActionAsync("CompleteAction").Unwrap(Workflow));

                Assert.IsType<TaskCanceledException>(ex);
                await WaitUntilWorkflowCompleted();
            }
        }

        public class TestWorkflow : WorkflowBase
        {
            public TestWorkflow(bool doInit = true)
                : base(null)
            {
                if (doInit)
                {
                    OnInit();
                }
            }

            public string Action { get; private set; }

            public NamedValues Parameters { get; private set; }

            public void CreateOperation() => base.CreateOperation();

            public new IDisposable TryStartOperation() => base.TryStartOperation();

            protected override void OnActionsInit()
            {
                base.OnActionsInit();
                ConfigureAction(
                    "Contacted",
                    p =>
                    {
                        Action = "Contacted";
                        Parameters = p;
                        ActionResult = 1;
                    },
                    synonyms: new[] { "Contacted 2" });
            }

            protected override Task RunAsync() => Task.Delay(Timeout.Infinite, Utilities.CurrentCancellationToken);
        }

        public sealed class TestWorkflowWithFailingAction : TestWorkflow
        {
            public TestWorkflowWithFailingAction()
                : base(doInit: false)
            {
            }

            protected override void OnActionsInit()
            {
                base.OnActionsInit();
                ConfigureAction("FailingAction");
                ConfigureAction("CancelingAction", () => throw new TaskCanceledException());
                ConfigureAction("CompleteAction");
            }

            protected override async Task RunAsync()
            {
                await this.WaitForAny(
                    () => this.WaitForAction("CompleteAction", exportOperation: true),
                    async () =>
                    {
                        await this.WaitForAction("FailingAction", exportOperation: true);
                        throw new NotSupportedException();
                    });
            }
        }

        public sealed class TestWorkflowWithState : WorkflowBase<States>
        {
            public TestWorkflowWithState()
            {
                OnInit();
            }

            public string Action { get; private set; }

            public NamedValues Parameters { get; private set; }

            public new States State => base.State;

            // ReSharper disable once UnusedParameter.Local
            public new void SetState(States state, bool isStateRestored = false) =>
                base.SetState(state, isStateRestored);

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

            protected override Task RunAsync() => throw new NotImplementedException();

            protected override void OnStatesInit()
            {
            }
        }
    }
}
