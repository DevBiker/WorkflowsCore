using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class WorkflowsCoordinatorTests
    {
        private readonly WorkflowsCoordinator<WorkflowNames> _workflowsCoordinator = 
            new WorkflowsCoordinator<WorkflowNames>();

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

        [Fact]
        public async Task AddWorkflowShouldAddWorkflow()
        {
            var workflow = new TestWorkflow();
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                TestWorkflow.Action1,
                WorkflowNames.Name2,
                (s, d) => Task.CompletedTask);
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, workflow);
            var newWorkflow = _workflowsCoordinator.GetWorkflow(WorkflowNames.Name1);
            Assert.Same(workflow, newWorkflow);
        }

        [Fact]
        public async Task WorkflowWithSameNameCannotBeAddedTwice()
        {
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, new TestWorkflow());

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, new TestWorkflow()));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public async Task IfActionOnSrcWorkflowIsExecutedForRegisteredDependencyThenDependencyHandlerShouldBeCalled()
        {
            var srcWorkflow = new TestWorkflow();
            var dstWorkflow = new TestWorkflow();
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                TestWorkflow.Action1,
                WorkflowNames.Name2,
                (s, d) =>
                {
                    Assert.Same(srcWorkflow, s);
                    Assert.Same(dstWorkflow, d);
                    return d.ExecuteActionAsync(TestWorkflow.Action2);
                });

            srcWorkflow.StartWorkflow();
            dstWorkflow.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, srcWorkflow);
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, dstWorkflow);

            await srcWorkflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100);
            await dstWorkflow.WaitForState(WorkflowStates.State2).WaitWithTimeout(100);

            await srcWorkflow.CompletedTask;
            await dstWorkflow.CompletedTask;
        }

        [Fact]
        public async Task IfSrcWorkflowIsCanceledForRegisteredDependencyThenCancelHandlerShouldBeCalled()
        {
            var srcWorkflow = new TestWorkflow();
            var dstWorkflow = new TestWorkflow();
            var called = 1;
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
                    Assert.Same(srcWorkflow, s);
                    Assert.Same(dstWorkflow, d);
                    Interlocked.Exchange(ref called, 1);
                    return Task.CompletedTask;
                });

            srcWorkflow.StartWorkflow();
            dstWorkflow.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, srcWorkflow);
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, dstWorkflow);

            await _workflowsCoordinator.CancelWorkflowAsync(WorkflowNames.Name1);
            await Task.Delay(30);
            Assert.Equal(1, called);
        }

        [Fact]
        public async Task IfSrcWorkflowIsCanceledForRegisteredDependencyThenTimesExecutedShouldBeClearedIfActionToClearSpecified()
        {
            var srcWorkflow = new TestWorkflow();
            var dstWorkflow = new TestWorkflow();
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                TestWorkflow.Action1,
                WorkflowNames.Name2,
                (s, d) => d.ExecuteActionAsync(TestWorkflow.Action2),
                onSrcWorkflowCanceledClearTimesExecutedForAction: TestWorkflow.Action2);

            srcWorkflow.StartWorkflow();
            dstWorkflow.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, srcWorkflow);
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, dstWorkflow);

            await srcWorkflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100);
            await dstWorkflow.WaitForState(WorkflowStates.State2).WaitWithTimeout(100);

            await _workflowsCoordinator.CancelWorkflowAsync(WorkflowNames.Name1);

            var wasExecuted = await srcWorkflow.DoWorkflowTaskAsync(
                () => dstWorkflow.WasExecuted(TestWorkflow.Action2),
                forceExecution: true);

            Assert.Equal(false, wasExecuted);

            try
            {
                await srcWorkflow.CompletedTask;
            }
            catch (TaskCanceledException)
            {
            }

            await dstWorkflow.CompletedTask;
        }

        [Fact]
        public async Task IfActionOnSrcWorkflowWasExecutedForRegisteredDependencyBeforeWorkflowAddedThenDependencyHandlerShouldBeCalled()
        {
            var srcWorkflow = new TestWorkflow();
            var dstWorkflow = new TestWorkflow();
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                TestWorkflow.Action1,
                WorkflowNames.Name2,
                (s, d) =>
                {
                    Assert.Same(srcWorkflow, s);
                    Assert.Same(dstWorkflow, d);
                    return d.ExecuteActionAsync(TestWorkflow.Action2);
                });

            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, srcWorkflow);

            srcWorkflow.StartWorkflow();
            await srcWorkflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100);
            await srcWorkflow.WaitForState(WorkflowStates.State1).WaitWithTimeout(100);

            dstWorkflow.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, dstWorkflow);
            await dstWorkflow.WaitForState(WorkflowStates.State2).WaitWithTimeout(100);

            await srcWorkflow.CompletedTask;
            await dstWorkflow.CompletedTask;
        }

        [Fact]
        public async Task IfActionOnSrcWorkflowWasExecutedForRegisteredDependencyBeforeWorkflowAddedThenDependencyHandlerShouldNotBeCalledIfInitializeDependenciesIsFalse()
        {
            var srcWorkflow = new TestWorkflow();
            var dstWorkflow = new TestWorkflow();
            var dependencyHandlerWasCalled = 1;
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                TestWorkflow.Action1,
                WorkflowNames.Name2,
                (s, d) =>
                {
                    Interlocked.Exchange(ref dependencyHandlerWasCalled, 1);
                    return Task.CompletedTask;
                });

            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, srcWorkflow);

            srcWorkflow.StartWorkflow();
            await srcWorkflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100);

            dstWorkflow.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, dstWorkflow, initializeDependencies: false);

            await srcWorkflow.CompletedTask;
            await dstWorkflow.CompletedTask;

            Assert.Equal(1, dependencyHandlerWasCalled);
        }

        [Fact]
        public async Task IfSrcWorkflowEntersRequiredStateForRegisteredDependencyThenDependencyHandlerShouldBeCalled()
        {
            var srcWorkflow = new TestWorkflow();
            var dstWorkflow = new TestWorkflow();
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                WorkflowStates.State1,
                WorkflowNames.Name2,
                (s, d) =>
                {
                    Assert.Same(srcWorkflow, s);
                    Assert.Same(dstWorkflow, d);
                    return d.ExecuteActionAsync(TestWorkflow.Action2);
                });

            srcWorkflow.StartWorkflow();
            dstWorkflow.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, srcWorkflow);
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, dstWorkflow);
            
            await srcWorkflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100);
            await dstWorkflow.WaitForState(WorkflowStates.State2).WaitWithTimeout(100);

            await srcWorkflow.CompletedTask;
            await dstWorkflow.CompletedTask;
        }

        [Fact]
        public async Task IfSrcWorkflowIsCanceledForRegisteredStateDependencyThenCancelHandlerShouldBeCalled()
        {
            var srcWorkflow = new TestWorkflow();
            var dstWorkflow = new TestWorkflow();
            var called = 1;
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
                    Assert.Same(srcWorkflow, s);
                    Assert.Same(dstWorkflow, d);
                    Interlocked.Exchange(ref called, 1);
                    return Task.CompletedTask;
                });

            srcWorkflow.StartWorkflow();
            dstWorkflow.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, srcWorkflow);
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, dstWorkflow);

            await _workflowsCoordinator.CancelWorkflowAsync(WorkflowNames.Name1);
            await Task.Delay(30);
            Assert.Equal(1, called);
        }

        [Fact]
        public async Task IfSrcWorkflowIsCanceledForRegisteredStateDependencyThenTimesExecutedShouldBeClearedIfActionToClearSpecified()
        {
            var srcWorkflow = new TestWorkflow();
            var dstWorkflow = new TestWorkflow();
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                WorkflowStates.State1,
                WorkflowNames.Name2,
                (s, d) => d.ExecuteActionAsync(TestWorkflow.Action2),
                onSrcWorkflowCanceledClearTimesExecutedForAction: TestWorkflow.Action2);

            srcWorkflow.StartWorkflow();
            dstWorkflow.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, srcWorkflow);
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, dstWorkflow);

            await srcWorkflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100);
            await dstWorkflow.WaitForState(WorkflowStates.State2).WaitWithTimeout(100);

            await _workflowsCoordinator.CancelWorkflowAsync(WorkflowNames.Name1);

            var wasExecuted = await srcWorkflow.DoWorkflowTaskAsync(
                () => dstWorkflow.WasExecuted(TestWorkflow.Action2),
                forceExecution: true);

            Assert.Equal(false, wasExecuted);

            try
            {
                await srcWorkflow.CompletedTask;
            }
            catch (TaskCanceledException)
            {
            }

            await dstWorkflow.CompletedTask;
        }

        [Fact]
        public async Task IfSrcWorkflowEnteredRequiredStateForRegisteredDependencyBeforeWorkflowAddedThenDependencyHandlerShouldBeCalled()
        {
            var srcWorkflow = new TestWorkflow();
            var dstWorkflow = new TestWorkflow();
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                WorkflowStates.State1, 
                WorkflowNames.Name2,
                (s, d) =>
                {
                    Assert.Same(srcWorkflow, s);
                    Assert.Same(dstWorkflow, d);
                    return d.ExecuteActionAsync(TestWorkflow.Action2);
                });

            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, srcWorkflow);

            srcWorkflow.StartWorkflow();            
            await srcWorkflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100);
            await srcWorkflow.WaitForState(WorkflowStates.State1).WaitWithTimeout(100);

            dstWorkflow.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, dstWorkflow);
            await dstWorkflow.WaitForState(WorkflowStates.State2).WaitWithTimeout(100);

            await srcWorkflow.CompletedTask;
            await dstWorkflow.CompletedTask;
        }

        [Fact]
        public async Task UnhandledExceptionEventShouldBeFiredInCaseOfUnhandledException()
        {
            var srcWorkflow = new TestWorkflow();
            var dstWorkflow = new TestWorkflow();
            Exception exception = null;
            _workflowsCoordinator.UnhandledException += (sender, args) => exception = args.Exception;
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                WorkflowStates.State1,
                WorkflowNames.Name2,
                (s, d) =>
                {
                    throw new InvalidOperationException();
                });

            srcWorkflow.StartWorkflow();
            dstWorkflow.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, srcWorkflow);
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, dstWorkflow);

            Assert.Null(exception);
            await srcWorkflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100);
            await Task.Delay(10);

            await srcWorkflow.CompletedTask;
            await dstWorkflow.CompletedTask;

            Assert.IsType<InvalidOperationException>(exception);
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
                ConfigureState(WorkflowStates.None, availableActions: new[] { Action1, Action2 });
                ConfigureState(WorkflowStates.State1);
                ConfigureState(WorkflowStates.State2);
            }

            protected override Task RunAsync()
            {
                SetState(WorkflowStates.None);
                return Task.Delay(30);
            }
        }
    }
}
