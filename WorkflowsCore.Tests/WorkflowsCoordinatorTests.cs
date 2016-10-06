using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkflowsCore.Tests
{
    [TestClass]
    public class WorkflowsCoordinatorTests
    {
        private WorkflowsCoordinator<WorkflowNames> _workflowsCoordinator;

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

        [TestInitialize]
        public void TestInitialize()
        {
            _workflowsCoordinator = new WorkflowsCoordinator<WorkflowNames>();
        }

        [TestMethod]
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
            Assert.AreSame(workflow, newWorkflow);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task WorkflowWithSameNameCannotBeAddedTwice()
        {
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, new TestWorkflow());
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, new TestWorkflow());
        }

        [TestMethod]
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
                    Assert.AreSame(srcWorkflow, s);
                    Assert.AreSame(dstWorkflow, d);
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

        [TestMethod]
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
                    Assert.AreSame(srcWorkflow, s);
                    Assert.AreSame(dstWorkflow, d);
                    Interlocked.Exchange(ref called, 1);
                    return Task.CompletedTask;
                });

            srcWorkflow.StartWorkflow();
            dstWorkflow.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, srcWorkflow);
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, dstWorkflow);

            await _workflowsCoordinator.CancelWorkflowAsync(WorkflowNames.Name1);
            await Task.Delay(30);
            Assert.AreEqual(1, called);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CannotRegisterTwoCancelHandlers()
        {
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
                    throw new NotImplementedException();
                },
                onSrcWorkflowCanceledClearTimesExecutedForAction: "SomeAction");
        }

        [TestMethod]
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

            Assert.AreEqual(false, wasExecuted);

            try
            {
                await srcWorkflow.CompletedTask;
            }
            catch (TaskCanceledException)
            {
            }

            await dstWorkflow.CompletedTask;
        }

        [TestMethod]
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
                    Assert.AreSame(srcWorkflow, s);
                    Assert.AreSame(dstWorkflow, d);
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

        [TestMethod]
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

            Assert.AreEqual(1, dependencyHandlerWasCalled);
        }

        [TestMethod]
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
                    Assert.AreSame(srcWorkflow, s);
                    Assert.AreSame(dstWorkflow, d);
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

        [TestMethod]
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
                    Assert.AreSame(srcWorkflow, s);
                    Assert.AreSame(dstWorkflow, d);
                    Interlocked.Exchange(ref called, 1);
                    return Task.CompletedTask;
                });

            srcWorkflow.StartWorkflow();
            dstWorkflow.StartWorkflow();
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name1, srcWorkflow);
            await _workflowsCoordinator.AddWorkflowAsync(WorkflowNames.Name2, dstWorkflow);

            await _workflowsCoordinator.CancelWorkflowAsync(WorkflowNames.Name1);
            await Task.Delay(30);
            Assert.AreEqual(1, called);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CannotRegisterTwoCancelHandlersForStateDependencies()
        {
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
                    throw new NotImplementedException();
                },
                onSrcWorkflowCanceledClearTimesExecutedForAction: "SomeAction");
        }

        [TestMethod]
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

            Assert.AreEqual(false, wasExecuted);

            try
            {
                await srcWorkflow.CompletedTask;
            }
            catch (TaskCanceledException)
            {
            }

            await dstWorkflow.CompletedTask;
        }

        [TestMethod]
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
                    Assert.AreSame(srcWorkflow, s);
                    Assert.AreSame(dstWorkflow, d);
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

        [TestMethod]
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

            Assert.IsNull(exception);
            await srcWorkflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100);
            await Task.Delay(10);

            await srcWorkflow.CompletedTask;
            await dstWorkflow.CompletedTask;

            // ReSharper disable once IsExpressionAlwaysTrue
            Assert.IsTrue(exception is InvalidOperationException);
        }

        private sealed class TestWorkflow : WorkflowBase<WorkflowStates>
        {
            public const string Action1 = nameof(Action1);
            public const string Action2 = nameof(Action2);

            public TestWorkflow()
                : base(() => new WorkflowRepository())
            {
            }

            // ReSharper disable once UnusedParameter.Local
            public new bool WasExecuted(string action) => base.WasExecuted(action);

            protected override void OnInit()
            {
                base.OnInit();
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
