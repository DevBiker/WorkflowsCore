using System;
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
        public void AddWorkflowShouldAddWorkflow()
        {
            var workflow = new TestWorkflow();
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                TestWorkflow.Action1,
                WorkflowNames.Name2,
                (s, d) => Task.CompletedTask);
            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name1, workflow);
            var newWorkflow = _workflowsCoordinator.GetWorkflow(WorkflowNames.Name1);
            Assert.AreSame(workflow, newWorkflow);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void WorkflowWithSameNameCannotBeAddedTwice()
        {
            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name1, new TestWorkflow());
            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name1, new TestWorkflow());
        }

        [TestMethod]
        public void IfActionOnSrcWorkflowIsExecutedForRegisteredDependencyThenDependencyHandlerShouldBeCalled()
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

            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name1, srcWorkflow);
            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name2, dstWorkflow);

            srcWorkflow.StartWorkflow();
            dstWorkflow.StartWorkflow();
            srcWorkflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100).Wait();
            dstWorkflow.WaitForState(WorkflowStates.State2).WaitWithTimeout(100).Wait();

            srcWorkflow.CompletedTask.Wait();
            dstWorkflow.CompletedTask.Wait();
        }

        [TestMethod]
        public async Task IfSrcWorkflowIsCanceledForRegisteredDependencyThenCancelHandlerShouldBeCalled()
        {
            var srcWorkflow = new TestWorkflow();
            var dstWorkflow = new TestWorkflow();
            var called = false;
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
                    called = true;
                    return Task.CompletedTask;
                });

            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name1, srcWorkflow);
            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name2, dstWorkflow);

            _workflowsCoordinator.CancelWorkflow(WorkflowNames.Name1);
            await Task.Delay(10);
            Assert.IsTrue(called);
        }

        [TestMethod]
        public void IfActionOnSrcWorkflowWasExecutedForRegisteredDependencyBeforeWorkflowAddedThenDependencyHandlerShouldBeCalled()
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

            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name1, srcWorkflow);

            srcWorkflow.StartWorkflow();
            srcWorkflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100).Wait();

            dstWorkflow.StartWorkflow();
            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name2, dstWorkflow);
            dstWorkflow.WaitForState(WorkflowStates.State2).WaitWithTimeout(100).Wait();

            srcWorkflow.CompletedTask.Wait();
            dstWorkflow.CompletedTask.Wait();
        }

        [TestMethod]
        public void IfActionOnSrcWorkflowWasExecutedForRegisteredDependencyBeforeWorkflowAddedThenDependencyHandlerShouldNotBeCalledIfInitializeDependenciesIsFalse()
        {
            var srcWorkflow = new TestWorkflow();
            var dstWorkflow = new TestWorkflow();
            var dependencyHandlerWasCalled = false;
            _workflowsCoordinator.RegisterWorkflowDependency(
                WorkflowNames.Name1,
                TestWorkflow.Action1,
                WorkflowNames.Name2,
                (s, d) =>
                {
                    dependencyHandlerWasCalled = true;
                    return Task.CompletedTask;
                });

            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name1, srcWorkflow);

            srcWorkflow.StartWorkflow();
            srcWorkflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100).Wait();

            dstWorkflow.StartWorkflow();
            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name2, dstWorkflow, initializeDependencies: false);

            srcWorkflow.CompletedTask.Wait();
            dstWorkflow.CompletedTask.Wait();

            Assert.IsFalse(dependencyHandlerWasCalled);
        }

        [TestMethod]
        public void IfSrcWorkflowEntersRequiredStateForRegisteredDependencyThenDependencyHandlerShouldBeCalled()
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

            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name1, srcWorkflow);
            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name2, dstWorkflow);

            srcWorkflow.StartWorkflow();
            dstWorkflow.StartWorkflow();
            srcWorkflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100).Wait();
            dstWorkflow.WaitForState(WorkflowStates.State2).WaitWithTimeout(100).Wait();

            srcWorkflow.CompletedTask.Wait();
            dstWorkflow.CompletedTask.Wait();
        }

        [TestMethod]
        public async Task IfSrcWorkflowIsCanceledForRegisteredStateDependencyThenCancelHandlerShouldBeCalled()
        {
            var srcWorkflow = new TestWorkflow();
            var dstWorkflow = new TestWorkflow();
            var called = false;
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
                    called = true;
                    return Task.CompletedTask;
                });

            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name1, srcWorkflow);
            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name2, dstWorkflow);

            _workflowsCoordinator.CancelWorkflow(WorkflowNames.Name1);
            await Task.Delay(10);
            Assert.IsTrue(called);
        }

        [TestMethod]
        public void IfSrcWorkflowEnteredRequiredStateForRegisteredDependencyBeforeWorkflowAddedThenDependencyHandlerShouldBeCalled()
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

            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name1, srcWorkflow);

            srcWorkflow.StartWorkflow();            
            srcWorkflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100).Wait();
            srcWorkflow.WaitForState(WorkflowStates.State1).WaitWithTimeout(100).Wait();

            dstWorkflow.StartWorkflow();
            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name2, dstWorkflow);
            dstWorkflow.WaitForState(WorkflowStates.State2).WaitWithTimeout(100).Wait();

            srcWorkflow.CompletedTask.Wait();
            dstWorkflow.CompletedTask.Wait();
        }

        [TestMethod]
        public void UnhandledExceptionEventShouldBeFiredInCaseOfUnhandledException()
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

            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name1, srcWorkflow);
            _workflowsCoordinator.AddWorkflow(WorkflowNames.Name2, dstWorkflow);

            Assert.IsNull(exception);
            srcWorkflow.StartWorkflow();
            dstWorkflow.StartWorkflow();
            srcWorkflow.ExecuteActionAsync(TestWorkflow.Action1).WaitWithTimeout(100).Wait();
            Task.Delay(10).Wait();

            srcWorkflow.CompletedTask.Wait();
            dstWorkflow.CompletedTask.Wait();

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
                return Task.Delay(20);
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
