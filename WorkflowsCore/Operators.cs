﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WorkflowsCore
{
    public static class Operators
    {
        public static Task<int> WaitForAny(this WorkflowBase workflow, params Func<Task>[] tasksFactories)
        {
            var parentCancellationToken = Utilities.CurrentCancellationToken;
            if (parentCancellationToken.IsCancellationRequested)
            {
                var tcs = new TaskCompletionSource<int>();
                tcs.SetCanceled();
                return tcs.Task;
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(parentCancellationToken);
            return Utilities.SetCurrentCancellationTokenTemporarily(
                cts.Token,
                () =>
                {
                    var mapIdToIndex = new Dictionary<int, int>();
                    var tasks = new List<Task>();
                    var index = 0;
                    var tcs = new TaskCompletionSource<int>(mapIdToIndex);
                    try
                    {
                        foreach (var task in tasksFactories.Select(taskFactory => taskFactory()))
                        {
                            mapIdToIndex[task.Id] = index++;
                            tasks.Add(task);
                            if (IsNonOptionalTaskCompleted(task))
                            {
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ex = RequestCancellation(cts, ex);

                        // ReSharper disable once MethodSupportsCancellation
                        Task.WhenAll(tasks).ContinueWith(
                            t =>
                            {
                                if (t.IsFaulted)
                                {
                                    // ReSharper disable once PossibleNullReferenceException
                                    ex = WorkflowBase.GetAggregatedExceptions(ex, t.Exception.GetBaseException());
                                }

                                tcs.SetException(ex);
                            },
                            TaskContinuationOptions.ExecuteSynchronously);
                        return tcs.Task;
                    }

                    WaitAnyCore(tasks, tcs, cts, parentCancellationToken);
                    return tcs.Task;
                });
        }

        public static Task Optional(this WorkflowBase workflow, Task task)
        {
            var tcs = new TaskCompletionSource<bool>(new OptionalTask());
            task.ContinueWith(
                t =>
                {
                    if (t.IsCanceled)
                    {
                        tcs.SetCanceled();
                        return;
                    }

                    if (t.IsFaulted)
                    {
                        // ReSharper disable once PossibleNullReferenceException
                        tcs.SetException(t.Exception.GetBaseException());
                        return;
                    }

                    tcs.SetResult(true);
                },
                TaskContinuationOptions.ExecuteSynchronously);
            return tcs.Task;
        }

        public static Task<NamedValues> WaitForAction(
            this WorkflowBase workflow,
            string action,
            bool exportOperation = false)
        {
            var tcs = new TaskCompletionSource<NamedValues>();

            var currentCancellationToken = Utilities.CurrentCancellationToken;
            if (currentCancellationToken.IsCancellationRequested)
            {
                tcs.SetCanceled();
                return tcs.Task;
            }

            EventHandler<WorkflowBase.ActionExecutedEventArgs> handler = null;
            var registration = currentCancellationToken.Register(
                () =>
                {
                    // ReSharper disable once AccessToModifiedClosure
                    workflow.ActionExecuted -= handler;
                    tcs.TrySetCanceled();
                },
                false);
            handler = (o, args) =>
            {
                if (currentCancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (args.Synonyms.Any(a => string.Equals(action, a, StringComparison.Ordinal)))
                {
                    // ReSharper disable once AccessToModifiedClosure
                    workflow.ActionExecuted -= handler;

                    IDisposable operation = null;
                    if (exportOperation)
                    {
                        workflow.CreateOperation();
                        operation = workflow.TryStartOperation();
                        if (operation == null)
                        {
                            throw new InvalidOperationException();
                        }

                        args.Parameters.SetData("ActionOperation", operation);
                    }

                    if (tcs.TrySetResult(args.Parameters))
                    {
                        registration.Dispose();
                    }
                    else
                    {
                        operation?.Dispose();
                    }
                }
            };

            workflow.ActionExecuted += handler;
            return tcs.Task;
        }

        public static async Task WaitForActionWithWasExecutedCheck(this WorkflowBase workflow, string action)
        {
            var currentCancellationToken = Utilities.CurrentCancellationToken;
            if (currentCancellationToken.IsCancellationRequested)
            {
                await Task.FromCanceled(currentCancellationToken);
            }

            var wasExecuted = await workflow.RunViaWorkflowTaskScheduler(() => workflow.WasExecuted(action));
            if (wasExecuted)
            {
                return;
            }

            await workflow.WaitForAction(action);
        }

        public static Task WaitForAction<TState>(
            this WorkflowBase<TState> workflow,
            string action,
            TState state)
            where TState : struct
        {
            workflow.EnsureWorkflowTaskScheduler();

            var tcs = new TaskCompletionSource<bool>();

            if (Utilities.CurrentCancellationToken.IsCancellationRequested)
            {
                tcs.SetCanceled();
                return tcs.Task;
            }

            var statesHistory = workflow.TransientStatesHistory;
            if (EqualityComparer<TState?>.Default.Equals(statesHistory?.FirstOrDefault(), state))
            {
                tcs.SetResult(true);
                return tcs.Task;
            }

            return workflow.WaitForAction(action);
        }

        public static Task WaitForState<TState>(
            this WorkflowBase<TState> workflow,
            TState state = default(TState),
            bool checkInitialState = true,
            bool anyState = false)
        {
            var tcs = new TaskCompletionSource<bool>();

            var currentCancellationToken = Utilities.CurrentCancellationToken;
            if (currentCancellationToken.IsCancellationRequested)
            {
                tcs.SetCanceled();
                return tcs.Task;
            }

            EventHandler<WorkflowBase<TState>.StateChangedEventArgs> handler = null;
            var registration = currentCancellationToken.Register(
                () =>
                {
                    // ReSharper disable once AccessToModifiedClosure
                    workflow.StateChanged -= handler;
                    tcs.TrySetCanceled();
                },
                false);
            handler = (o, args) =>
            {
                if (currentCancellationToken.IsCancellationRequested || workflow.IsRestoringState)
                {
                    return;
                }

                if (anyState || EqualityComparer<TState>.Default.Equals(args.NewState, state))
                {
                    // ReSharper disable once AccessToModifiedClosure
                    workflow.StateChanged -= handler;
                    if (tcs.TrySetResult(true))
                    {
                        registration.Dispose();
                    }
                }
            };

            workflow.StateChanged += handler;
            if (checkInitialState && !anyState)
            {
                workflow.RunViaWorkflowTaskScheduler(
                    () => handler(
                        workflow,
                        new WorkflowBase<TState>.StateChangedEventArgs(workflow.PreviousState, workflow.State)));
            }

            return tcs.Task;
        }

        public static Task<IDisposable> WaitForReadyAndStartOperation(this WorkflowBase workflow)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(Utilities.CurrentCancellationToken);
            workflow.CreateOperation();
            return workflow.RunViaWorkflowTaskScheduler(
                async w =>
                {
                    try
                    {
                        while (true)
                        {
                            var operation = w.TryStartOperation();
                            if (operation != null)
                            {
                                return operation;
                            }

                            var t = await Task.WhenAny(workflow.ReadyTask, Task.Delay(Timeout.Infinite, cts.Token));
                            await t;
                        }
                    }
                    finally
                    {
                        cts.Cancel();
                        cts.Dispose();
                    }
                }).Unwrap();
        }

        public static async Task WaitForReady(this WorkflowBase workflow)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(Utilities.CurrentCancellationToken);
            var t = await Task.WhenAny(workflow.ReadyTask, Task.Delay(Timeout.Infinite, cts.Token));
            cts.Cancel();
            await t;
        }

        public static Task Then(this Task task, Action action)
        {
            var tcs = new TaskCompletionSource<bool>();
            task.ContinueWith(
                t =>
                {
                    if (t.IsCanceled)
                    {
                        tcs.SetCanceled();
                        return;
                    }

                    if (t.IsFaulted)
                    {
                        // ReSharper disable once PossibleNullReferenceException
                        tcs.SetException(t.Exception.GetBaseException());
                        return;
                    }

                    try
                    {
                        action();
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                },
                TaskContinuationOptions.ExecuteSynchronously);

            return tcs.Task;
        }

        public static async Task WaitWithTimeout(
            this Task task,
            int millisecondsTimeout,
            string description = null)
        {
            var cts = new CancellationTokenSource();
            if (task != await Task.WhenAny(task, Task.Delay(millisecondsTimeout, cts.Token)))
            {
                throw new TimeoutException(description);
            }

            cts.Cancel();
            await task;
        }

        public static async Task<T> WaitWithTimeout<T>(
            this Task<T> task,
            int millisecondsTimeout,
            string description = null)
        {
            var cts = new CancellationTokenSource();
            if (task != await Task.WhenAny(task, Task.Delay(millisecondsTimeout, cts.Token)))
            {
                throw new TimeoutException(description);
            }

            cts.Cancel();
            return await task;
        }

        private static void WaitAnyCore(
            IList<Task> tasks,
            TaskCompletionSource<int> tcs,
            CancellationTokenSource cts,
            CancellationToken parentCancellationToken)
        {
            // ReSharper disable once MethodSupportsCancellation
            Task.WhenAny(tasks).ContinueWith(
                _ =>
                {
                    if (cts.IsCancellationRequested)
                    {
                        Task.WhenAll(tasks).ContinueWith(
                            t =>
                            {
                                if (t.IsFaulted)
                                {
                                    // ReSharper disable once PossibleNullReferenceException
                                    tcs.SetException(t.Exception.GetBaseException());
                                    return;
                                }

                                tcs.SetCanceled();
                            },
                            TaskContinuationOptions.ExecuteSynchronously);
                        return;
                    }

                    var faultedTask = tasks.FirstOrDefault(t => t.IsFaulted);
                    if (faultedTask != null)
                    {
                        // ReSharper disable once PossibleNullReferenceException
                        var exception = RequestCancellation(cts);

                        // ReSharper disable once MethodSupportsCancellation
                        // ReSharper disable once PossibleNullReferenceException
                        Task.WhenAll(tasks)
                            .ContinueWith(
                                t => tcs.SetException(
                                    WorkflowBase.GetAggregatedExceptions(exception, t.Exception.GetBaseException())),
                                TaskContinuationOptions.ExecuteSynchronously);
                        return;
                    }

                    // TODO: Handle cancellation initiated by children
                    var completedTask = tasks.FirstOrDefault(IsNonOptionalTaskCompleted);
                    if (completedTask != null)
                    {
                        var exception = RequestCancellation(cts);

                        // ReSharper disable once MethodSupportsCancellation
                        Task.WhenAll(tasks).ContinueWith(
                            t =>
                            {
                                if (t.IsFaulted)
                                {
                                    // ReSharper disable once PossibleNullReferenceException
                                    tcs.SetException(
                                        WorkflowBase.GetAggregatedExceptions(exception, t.Exception.GetBaseException()));
                                    return;
                                }

                                if (exception != null)
                                {
                                    tcs.SetException(exception);
                                    return;
                                }

                                if (parentCancellationToken.IsCancellationRequested)
                                {
                                    tcs.SetCanceled();
                                    return;
                                }

                                tcs.SetResult(((Dictionary<int, int>)tcs.Task.AsyncState)[completedTask.Id]);
                            },
                            TaskContinuationOptions.ExecuteSynchronously);
                        return;
                    }

                    // TODO:
                    ////if (tasks.Any(t => t.IsCanceled))
                    ////{
                    ////    tcs.SetException(new InvalidOperationException($"WaitForAny() canceled child {cts.IsCancellationRequested}, {parentCancellationToken.IsCancellationRequested}"));
                    ////    return;
                    ////}

                    WaitAnyCore(
                        tasks.Where(t => t.Status != TaskStatus.RanToCompletion).ToList(),
                        tcs,
                        cts,
                        parentCancellationToken);
                },
                TaskContinuationOptions.ExecuteSynchronously);
        }

        private static bool IsNonOptionalTaskCompleted(Task t) => 
            t.Status == TaskStatus.RanToCompletion && !(t.AsyncState is OptionalTask);

        private static Exception RequestCancellation(CancellationTokenSource cts, Exception exception = null)
        {
            try
            {
                cts.Cancel();
            }
            catch (Exception ex)
            {
                exception = WorkflowBase.GetAggregatedExceptions(exception, ex);
            }

            return exception;
        }

        private class OptionalTask
        {
        }
    }
}
