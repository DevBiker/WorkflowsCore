﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
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
                                cts.Dispose();
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

            var registration = currentCancellationToken.Register(
                () =>
                {
                    workflow.ActionExecuted -= OnActionExecuted;
                    tcs.TrySetCanceled();
                },
                false);

            workflow.ActionExecuted += OnActionExecuted;
            return tcs.Task;

            void OnActionExecuted(object sender, WorkflowBase.ActionExecutedEventArgs args)
            {
                if (currentCancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (args.Synonyms.All(a => !string.Equals(action, a, StringComparison.Ordinal)))
                {
                    return;
                }

                workflow.ActionExecuted -= OnActionExecuted;

                if (!exportOperation)
                {
                    if (tcs.TrySetResult(args.Parameters))
                    {
                        registration.Dispose();
                    }
                }
                else
                {
                    // This will create inner operation and wait for completion of other sibling operations
                    // when there are multiple WaitForAction() calls with export operation and awaiting the same action
                    workflow.WaitForReadyAndStartOperation()
                        .ContinueWith(
                            operationTask =>
                            {
                                if (operationTask.IsFaulted)
                                {
                                    if (tcs.TrySetException(operationTask.Exception.GetBaseException()))
                                    {
                                        registration.Dispose();
                                    }

                                    return;
                                }
                                else if (operationTask.IsCanceled)
                                {
                                    if (tcs.TrySetCanceled())
                                    {
                                        registration.Dispose();
                                    }

                                    return;
                                }

                                var operation = operationTask.Result;
                                args.Parameters.SetDataField("ActionOperation", operation);
                                if (tcs.TrySetResult(args.Parameters))
                                {
                                    registration.Dispose();
                                }
                                else
                                {
                                    operation.Dispose();
                                }
                            },
                            TaskContinuationOptions.ExecuteSynchronously);
                    workflow.ResetOperationToParent();
                }
            }
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

            var registration = currentCancellationToken.Register(
                () =>
                {
                    workflow.StateChanged -= OnStateChanged;
                    tcs.TrySetCanceled();
                },
                false);

            workflow.StateChanged += OnStateChanged;
            if (checkInitialState && !anyState)
            {
                workflow.RunViaWorkflowTaskScheduler(
                    () => OnStateChanged(
                        workflow,
                        new WorkflowBase<TState>.StateChangedEventArgs(workflow.PreviousState, workflow.State)));
            }

            return tcs.Task;

            void OnStateChanged(object sender, WorkflowBase<TState>.StateChangedEventArgs args)
            {
                if (currentCancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (anyState || EqualityComparer<TState>.Default.Equals(args.NewState, state))
                {
                    workflow.StateChanged -= OnStateChanged;
                    if (tcs.TrySetResult(true))
                    {
                        registration.Dispose();
                    }
                }
            }
        }

        public static Task<IDisposable> WaitForReadyAndStartOperation(
            this WorkflowBase workflow,
            [CallerFilePath] string filePath = null,
            [CallerLineNumber] int lineNumber = 0)
        {
            /* ReSharper disable ExplicitCallerInfoArgument */
            workflow.CreateOperation(filePath, lineNumber);
            /* ReSharper restore ExplicitCallerInfoArgument */
            return WaitForReadyAndStartOperationCore(workflow);
        }

        public static Task WaitForAllInnerOperationsCompletion(this IDisposable operation) =>
            WorkflowBase.WaitForAllInnerOperationsCompletion(operation);

        public static async Task WaitForReady(this WorkflowBase workflow)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(Utilities.CurrentCancellationToken))
            {
                var t = await Task.WhenAny(workflow.ReadyTask, Task.Delay(Timeout.Infinite, cts.Token));
                cts.Cancel();
                await t;
            }
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
            using (var cts = new CancellationTokenSource())
            {
                if (task != await Task.WhenAny(task, Task.Delay(millisecondsTimeout, cts.Token)))
                {
                    throw new TimeoutException(description);
                }

                cts.Cancel();
                await task;
            }
        }

        public static async Task<T> WaitWithTimeout<T>(
            this Task<T> task,
            int millisecondsTimeout,
            string description = null)
        {
            using (var cts = new CancellationTokenSource())
            {
                if (task != await Task.WhenAny(task, Task.Delay(millisecondsTimeout, cts.Token)))
                {
                    throw new TimeoutException(description);
                }

                cts.Cancel();
                return await task;
            }
        }

        public static async Task<Event> WaitForEvent(this WorkflowBase workflow, Func<Event, bool> eventCondition, bool ignorePastEvents = false)
        {
            if (ignorePastEvents)
            {
                return await WaitForEventCore(workflow, eventCondition);
            }

            Task<Event> t1 = null;
            Task<Event> t2 = null;
            var index = await workflow.WaitForAny(
                () => t1 = WaitForEventCore(workflow, eventCondition),
                () => t2 = FindLatestEventOrWaitForever());
            return await (index == 0 ? t1 : t2);

            async Task<Event> FindLatestEventOrWaitForever()
            {
                var res = await workflow.RunViaWorkflowTaskScheduler(w => w.FindLatestEvent(eventCondition), forceExecution: true);
                if (res == null)
                {
                    await Task.Delay(Timeout.Infinite, Utilities.CurrentCancellationToken);
                }

                return res;
            }
        }

        public static Task<T> Unwrap<T>(this Task<T> task, WorkflowBase workflow)
        {
            return task.ContinueWith(
                t =>
                {
                    if (t.IsFaulted || !t.IsCanceled)
                    {
                        return t;
                    }

                    if (!workflow.IsCancellationRequested)
                    {
                        throw new InvalidOperationException("Unexpected cancellation");
                    }

                    return workflow.CompletedTask.ContinueWith(
                        ct =>
                        {
                            if (ct.IsCanceled || !ct.IsFaulted)
                            {
                                throw new TaskCanceledException(t);
                            }

                            ExceptionDispatchInfo.Capture(ct.Exception.GetBaseException()).Throw();
                            return t.Result; // Never called
                        },
                        Utilities.CurrentCancellationToken);
                },
                Utilities.CurrentCancellationToken).Unwrap();
        }

        public static Task Unwrap(this Task task, WorkflowBase workflow)
        {
            return task.ContinueWith(
                t =>
                {
                    if (t.IsFaulted || !t.IsCanceled)
                    {
                        return t;
                    }

                    if (!workflow.IsCancellationRequested)
                    {
                        throw new InvalidOperationException("Unexpected cancellation");
                    }

                    return workflow.CompletedTask.ContinueWith(
                        ct =>
                        {
                            if (ct.IsCanceled || !ct.IsFaulted)
                            {
                                throw new TaskCanceledException(t);
                            }

                            ExceptionDispatchInfo.Capture(ct.Exception.GetBaseException()).Throw();
                        },
                        Utilities.CurrentCancellationToken);
                },
                Utilities.CurrentCancellationToken).Unwrap();
        }

        private static Task<Event> WaitForEventCore(this WorkflowBase workflow, Func<Event, bool> eventCondition)
        {
            var tcs = new TaskCompletionSource<Event>();

            var currentCancellationToken = Utilities.CurrentCancellationToken;
            if (currentCancellationToken.IsCancellationRequested)
            {
                tcs.SetCanceled();
                return tcs.Task;
            }

            var registration = currentCancellationToken.Register(
                () =>
                {
                    workflow.EventLogged -= OnEventLogged;
                    tcs.TrySetCanceled();
                },
                false);

            workflow.EventLogged += OnEventLogged;
            return tcs.Task;

            void OnEventLogged(object sender, WorkflowBase.EventLoggedEventArgs args)
            {
                if (currentCancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (eventCondition(args.Event))
                {
                    workflow.EventLogged -= OnEventLogged;
                    if (tcs.TrySetResult(args.Event))
                    {
                        registration.Dispose();
                    }
                }
            }
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
                                cts.Dispose();
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
                        var exception = RequestCancellation(cts);

                        // ReSharper disable once MethodSupportsCancellation
                        // ReSharper disable once PossibleNullReferenceException
                        Task.WhenAll(tasks).ContinueWith(
                            t =>
                            {
                                cts.Dispose();
                                tcs.SetException(
                                    WorkflowBase.GetAggregatedExceptions(exception, t.Exception.GetBaseException()));
                            },
                            TaskContinuationOptions.ExecuteSynchronously);
                        return;
                    }

                    var canceledTask = tasks.FirstOrDefault(t => t.IsCanceled);
                    if (canceledTask != null)
                    {
                        var message =
                            $"Child task {((Dictionary<int, int>)tcs.Task.AsyncState)[canceledTask.Id]} has been canceled";
                        var exception = RequestCancellation(cts, new InvalidOperationException(message));

                        Task.WhenAll(tasks).ContinueWith(
                            t =>
                            {
                                cts.Dispose();
                                if (t.IsFaulted)
                                {
                                    // ReSharper disable once PossibleNullReferenceException
                                    tcs.SetException(
                                        WorkflowBase.GetAggregatedExceptions(exception, t.Exception.GetBaseException()));
                                    return;
                                }

                                tcs.SetException(exception);
                            },
                            TaskContinuationOptions.ExecuteSynchronously);
                        return;
                    }

                    var completedTask = tasks.FirstOrDefault(IsNonOptionalTaskCompleted);
                    if (completedTask != null)
                    {
                        var exception = RequestCancellation(cts);

                        // ReSharper disable once MethodSupportsCancellation
                        Task.WhenAll(tasks).ContinueWith(
                            t =>
                            {
                                cts.Dispose();
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

                    WaitAnyCore(
                        tasks.Where(t => t.Status != TaskStatus.RanToCompletion).ToList(),
                        tcs,
                        cts,
                        parentCancellationToken);
                },
                TaskContinuationOptions.ExecuteSynchronously);
        }

        private static async Task<IDisposable> WaitForReadyAndStartOperationCore(WorkflowBase workflow)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(Utilities.CurrentCancellationToken))
            {
                return await workflow.RunViaWorkflowTaskScheduler(
                    async w =>
                    {
                        // ReSharper disable AccessToDisposedClosure
                        var cancellationTask = Task.Delay(Timeout.Infinite, cts.Token);
                        try
                        {
                            while (true)
                            {
                                var operation = w.TryStartOperation();
                                if (operation != null)
                                {
                                    return operation;
                                }

                                var t = await Task.WhenAny(
                                    workflow.WaitForOperationOrInnerOperationCompletion(),
                                    cancellationTask);

                                if (t == cancellationTask)
                                {
                                    workflow.CancelOperation();
                                }

                                await t;
                            }
                        }
                        finally
                        {
                            cts.Cancel(); // ReSharper restore AccessToDisposedClosure
                        }
                    }).Unwrap();
            }
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
