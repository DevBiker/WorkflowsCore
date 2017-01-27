using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class WorkflowsCoordinatorTests : IDisposable
    {
        private readonly WorkflowsCoordinator<WorkflowNames> _workflowsCoordinator = 
            new WorkflowsCoordinator<WorkflowNames>();

        private readonly BaseWorkflowTest<TestWorkflow> _src = new BaseWorkflowTest<TestWorkflow>();
        private readonly BaseWorkflowTest<TestWorkflow> _dst = new BaseWorkflowTest<TestWorkflow>();

        public WorkflowsCoordinatorTests()
        {
            _src.Workflow = new TestWorkflow();
            _dst.Workflow = new TestWorkflow();
        }

        private enum WorkflowNames
        {
            Name1,
            Name2
        }

        private enum WorkflowStates
        {
            None,
            State1,
            State2
        }

        public void Dispose()
        {
            _src.Dispose();
            _dst.Dispose();
        }

        [Fact]
        public async Task AddWorkflowShouldAddWorkflow()
        {
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, _src.Workflow);

            var newWorkflow = _workflowsCoordinator.GetWorkflow(WorkflowNames.Name1);

            Assert.Same(_src.Workflow, newWorkflow);
        }

        [Fact]
        public async Task WorkflowWithSameNameCannotBeAddedTwice()
        {
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, _src.Workflow);

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, _dst.Workflow));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public async Task IfActionOnSrcWorkflowIsExecutedForRegisteredDependencyThenDependencyHandlerShouldBeCalled()
        {
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                TestWorkflow.Action1,
                WorkflowNames.Name2,
                (s, d) =>
                {
                    Assert.Same(_src.Workflow, s);
                    Assert.Same(_dst.Workflow, d);
                    return d.ExecuteActionAsync(TestWorkflow.Action2);
                });

            _src.StartWorkflow();
            _dst.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, _src.Workflow);
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, _dst.Workflow);

            await _src.Workflow.ExecuteActionAsync(TestWorkflow.Action1);
            await _dst.Workflow.WaitForState(WorkflowStates.State2).WaitWithTimeout(1000);

            await _src.CancelWorkflowAsync();
            await _dst.CancelWorkflowAsync();
        }

        [Fact]
        public async Task IfSrcWorkflowIsCanceledForRegisteredDependencyThenCancelHandlerShouldBeCalled()
        {
            var tcs = new TaskCompletionSource<bool>();
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                TestWorkflow.Action1,
                WorkflowNames.Name2,
                (s, d) =>
                {
                    throw new NotImplementedException();
                },
                (s, d) =>
                {
                    Assert.Same(_src.Workflow, s);
                    Assert.Same(_dst.Workflow, d);
                    tcs.SetResult(true);
                    return Task.CompletedTask;
                });

            _src.StartWorkflow();
            _dst.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, _src.Workflow);
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, _dst.Workflow);

            await _src.Workflow.StartedTask;
            await _workflowsCoordinator.CancelWorkflowAsync(WorkflowNames.Name1);
            await tcs.Task.WaitWithTimeout(100);

            await _src.CancelWorkflowAsync();
            await _dst.CancelWorkflowAsync();
        }

        [Fact]
        public async Task IfSrcWorkflowIsCanceledForRegisteredDependencyThenTimesExecutedShouldBeClearedIfActionToClearSpecified()
        {
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                TestWorkflow.Action1,
                WorkflowNames.Name2,
                (s, d) => d.ExecuteActionAsync(TestWorkflow.Action2),
                onSrcWorkflowCanceledClearTimesExecutedForAction: TestWorkflow.Action2);

            _src.StartWorkflow();
            _dst.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, _src.Workflow);
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, _dst.Workflow);

            await _src.Workflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100);
            await _dst.Workflow.WaitForState(WorkflowStates.State2).WaitWithTimeout(100);

            var wasExecuted =
                await _dst.Workflow.DoWorkflowTaskAsync(() => _dst.Workflow.WasExecuted(TestWorkflow.Action2));

            Assert.True(wasExecuted);

            await _workflowsCoordinator.CancelWorkflowAsync(WorkflowNames.Name1);

            wasExecuted = await _dst.Workflow.DoWorkflowTaskAsync(() => _dst.Workflow.WasExecuted(TestWorkflow.Action2));

            Assert.False(wasExecuted);

            await _src.CancelWorkflowAsync();
            await _dst.CancelWorkflowAsync();
        }

        [Fact]
        public async Task IfActionOnSrcWorkflowWasExecutedForRegisteredDependencyBeforeWorkflowAddedThenDependencyHandlerShouldBeCalled()
        {
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                TestWorkflow.Action1,
                WorkflowNames.Name2,
                (s, d) =>
                {
                    Assert.Same(_src.Workflow, s);
                    Assert.Same(_dst.Workflow, d);
                    return d.ExecuteActionAsync(TestWorkflow.Action2);
                });

            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, _src.Workflow);

            _src.StartWorkflow();
            await _src.Workflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100);
            await _src.Workflow.WaitForState(WorkflowStates.State1).WaitWithTimeout(100);

            _dst.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, _dst.Workflow);
            await _dst.Workflow.WaitForState(WorkflowStates.State2).WaitWithTimeout(100);

            await _src.CancelWorkflowAsync();
            await _dst.CancelWorkflowAsync();
        }

        [Fact]
        public async Task IfActionOnSrcWorkflowWasExecutedForRegisteredDependencyBeforeWorkflowAddedThenDependencyHandlerShouldNotBeCalledIfInitializeDependenciesIsFalse()
        {
            var dependencyHandlerWasCalled = 0;
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                TestWorkflow.Action1,
                WorkflowNames.Name2,
                (s, d) =>
                {
                    Interlocked.Exchange(ref dependencyHandlerWasCalled, 1);
                    return Task.CompletedTask;
                });

            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, _src.Workflow);

            _src.StartWorkflow();
            await _src.Workflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100);

            _dst.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, _dst.Workflow, initializeDependencies: false);
            await _dst.Workflow.StartedTask;

            await _src.CancelWorkflowAsync();
            await _dst.CancelWorkflowAsync();

            Assert.Equal(0, dependencyHandlerWasCalled);
        }

        [Fact]
        public async Task IfSrcWorkflowEntersRequiredStateForRegisteredDependencyThenDependencyHandlerShouldBeCalled()
        {
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                WorkflowStates.State1,
                WorkflowNames.Name2,
                (s, d) =>
                {
                    Assert.Same(_src.Workflow, s);
                    Assert.Same(_dst.Workflow, d);
                    return d.ExecuteActionAsync(TestWorkflow.Action2);
                });

            _src.StartWorkflow();
            _dst.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, _src.Workflow);
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, _dst.Workflow);
            
            await _src.Workflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100);
            await _dst.Workflow.WaitForState(WorkflowStates.State2).WaitWithTimeout(100);

            await _src.CancelWorkflowAsync();
            await _dst.CancelWorkflowAsync();
        }

        [Fact]
        public async Task IfSrcWorkflowIsCanceledForRegisteredStateDependencyThenCancelHandlerShouldBeCalled()
        {
            var tcs = new TaskCompletionSource<bool>();
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                WorkflowStates.State1, 
                WorkflowNames.Name2,
                (s, d) =>
                {
                    throw new NotImplementedException();
                },
                (s, d) =>
                {
                    Assert.Same(_src.Workflow, s);
                    Assert.Same(_dst.Workflow, d);
                    tcs.SetResult(true);
                    return Task.CompletedTask;
                });

            _src.StartWorkflow();
            _dst.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, _src.Workflow);
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, _dst.Workflow);

            await _workflowsCoordinator.CancelWorkflowAsync(WorkflowNames.Name1);
            await tcs.Task.WaitWithTimeout(100);

            await _src.CancelWorkflowAsync();
            await _dst.CancelWorkflowAsync();
        }

        [Fact]
        public async Task IfSrcWorkflowIsCanceledForRegisteredStateDependencyThenTimesExecutedShouldBeClearedIfActionToClearSpecified()
        {
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                WorkflowStates.State1,
                WorkflowNames.Name2,
                (s, d) => d.ExecuteActionAsync(TestWorkflow.Action2),
                onSrcWorkflowCanceledClearTimesExecutedForAction: TestWorkflow.Action2);

            _src.StartWorkflow();
            _dst.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, _src.Workflow);
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, _dst.Workflow);

            await _src.Workflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100);
            await _dst.Workflow.WaitForState(WorkflowStates.State2).WaitWithTimeout(100);

            var wasExecuted =
                await _dst.Workflow.DoWorkflowTaskAsync(() => _dst.Workflow.WasExecuted(TestWorkflow.Action2));

            Assert.True(wasExecuted);

            await _workflowsCoordinator.CancelWorkflowAsync(WorkflowNames.Name1);

            wasExecuted =
                await _dst.Workflow.DoWorkflowTaskAsync(() => _dst.Workflow.WasExecuted(TestWorkflow.Action2));

            Assert.False(wasExecuted);

            await _src.CancelWorkflowAsync();
            await _dst.CancelWorkflowAsync();
        }

        [Fact]
        public async Task IfSrcWorkflowEnteredRequiredStateForRegisteredDependencyBeforeWorkflowAddedThenDependencyHandlerShouldBeCalled()
        {
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                WorkflowStates.State1, 
                WorkflowNames.Name2,
                (s, d) =>
                {
                    Assert.Same(_src.Workflow, s);
                    Assert.Same(_dst.Workflow, d);
                    return d.ExecuteActionAsync(TestWorkflow.Action2);
                });

            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, _src.Workflow);

            _src.StartWorkflow();            
            await _src.Workflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100);
            await _src.Workflow.WaitForState(WorkflowStates.State1).WaitWithTimeout(100);

            _dst.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, _dst.Workflow);
            await _dst.Workflow.WaitForState(WorkflowStates.State2).WaitWithTimeout(100);

            await _src.CancelWorkflowAsync();
            await _dst.CancelWorkflowAsync();
        }

        [Fact]
        public async Task UnhandledExceptionEventShouldBeFiredInCaseOfUnhandledException()
        {
            var tcs = new TaskCompletionSource<Exception>();
            _workflowsCoordinator.UnhandledException += (sender, args) => tcs.SetResult(args.Exception);
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                WorkflowStates.State1,
                WorkflowNames.Name2,
                (s, d) =>
                {
                    throw new InvalidOperationException();
                });

            _src.StartWorkflow();
            _dst.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, _src.Workflow);
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, _dst.Workflow);

            Assert.False(tcs.Task.IsFaulted);
            await _src.Workflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100);
            var ex = await tcs.Task;

            Assert.IsType<InvalidOperationException>(ex);

            await _src.CancelWorkflowAsync();
            await _dst.Workflow.StartedTask;
            await _dst.CancelWorkflowAsync();
        }

        private sealed class TestWorkflow : WorkflowBase<WorkflowStates>
        {
            public const string Action1 = nameof(Action1);
            public const string Action2 = nameof(Action2);

            // ReSharper disable once UnusedParameter.Local
            public new bool WasExecuted(string action) => base.WasExecuted(action);

            protected override void OnActionsInit()
            {
                base.OnActionsInit();
                ConfigureAction(Action1, () => SetState(WorkflowStates.State1));
                ConfigureAction(Action2, () => SetState(WorkflowStates.State2));
            }

            protected override void OnStatesInit()
            {
            }

            protected override Task RunAsync()
            {
                SetState(WorkflowStates.None);
                return Task.Delay(Timeout.Infinite, Utilities.CurrentCancellationToken);
            }
        }
    }
}
