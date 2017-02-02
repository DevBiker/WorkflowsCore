using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WorkflowsCore
{
    public sealed class SingleThreadTaskScheduler : TaskScheduler, IDisposable
    {
        private readonly Thread _thread;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly BlockingCollection<Task> _tasks;
        private volatile Exception _exception;

        public SingleThreadTaskScheduler()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _tasks = new BlockingCollection<Task>();

            _thread = new Thread(ThreadStart) { IsBackground = true };
            _thread.Start();
        }

        public override int MaximumConcurrencyLevel => 1;

        public void Wait()
        {
            if (_thread == Thread.CurrentThread)
            {
                throw new InvalidOperationException();
            }

            if (_cancellationTokenSource.IsCancellationRequested)
            {
                throw new TaskSchedulerException("Cannot wait after disposal.");
            }

            _tasks.CompleteAdding();
            _thread.Join();

            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _tasks.Dispose();

            if (_exception != null)
            {
                throw new AggregateException(_exception);
            }
        }

        public void Dispose()
        {
            if (_cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            _tasks.CompleteAdding();
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _tasks.Dispose();
        }

        protected override void QueueTask(Task task)
        {
            VerifyNotDisposed();

            _tasks.Add(task, _cancellationTokenSource.Token);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            VerifyNotDisposed();

            if (_thread != Thread.CurrentThread || _cancellationTokenSource.IsCancellationRequested)
            {
                return false;
            }

            TryExecuteTask(task);
            return true;
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            VerifyNotDisposed();

            return _tasks.ToArray();
        }

        private void ThreadStart()
        {
            try
            {
                var token = _cancellationTokenSource.Token;

                foreach (var task in _tasks.GetConsumingEnumerable(token))
                {
                    TryExecuteTask(task);
                }
            }
            catch (Exception ex)
            {
                _exception = ex;
            }
        }

        private void VerifyNotDisposed()
        {
            if (_cancellationTokenSource.IsCancellationRequested)
            {
                throw new ObjectDisposedException(typeof(SingleThreadTaskScheduler).Name);
            }
        }
    }
}
