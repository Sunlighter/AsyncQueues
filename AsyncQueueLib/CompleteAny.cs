using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncQueueLib
{
    public abstract class CancellableResult<V>
    {
        public abstract V Complete();
        public abstract void Cancel();
    }

    public class CancellableOperation<V> : IDisposable
    {
        private Task<CancellableResult<V>> task;
        private CancellationTokenSource cts;

        public CancellableOperation(Task<CancellableResult<V>> task, CancellationTokenSource cts)
        {
            this.task = task;
            this.cts = cts;
        }

        public Task<CancellableResult<V>> Task { get { return task; } }

        public bool HasEnded { get { return task.IsCompleted || task.IsFaulted || task.IsCanceled; } }

        public void Cancel() { cts.Cancel(); }

        public void Dispose()
        {
            cts.Dispose();
        }
    }

    public delegate CancellableOperation<V> CancellableOperationStarter<V>(CancellationToken ctoken);

    public static partial class Utils
    {
        private class GetCancellableResult<U, V> : CancellableResult<V>
        {
            private AsyncQueue<U> queue;
            private U value;
            private Func<U, V> convertResult;

            public GetCancellableResult(AsyncQueue<U> queue, U value, Func<U, V> convertResult)
            {
                this.queue = queue;
                this.value = value;
                this.convertResult = convertResult;
            }

            public override V Complete()
            {
                queue.ReleaseRead(1);
                return convertResult(value);
            }

            public override void Cancel()
            {
                queue.ReleaseRead(0);
            }
        }

        private class GetEofCancellableResult<U, V> : CancellableResult<V>
        {
            private AsyncQueue<U> queue;
            private V eofResult;

            public GetEofCancellableResult(AsyncQueue<U> queue, V eofResult)
            {
                this.queue = queue;
                this.eofResult = eofResult;
            }

            public override V Complete()
            {
                queue.ReleaseRead(0);
                return eofResult;
            }

            public override void Cancel()
            {
                queue.ReleaseRead(0);
            }
        }

        public static CancellableOperationStarter<V> StartableGet<U, V>(this AsyncQueue<U> queue, Func<U, V> convertResult, V eofResult)
        {
            return delegate (CancellationToken ctoken)
            {
                CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ctoken);

                Func<Task<CancellableResult<V>>> taskSource = async delegate ()
                {
                    AcquireReadResult arr = await queue.AcquireReadAsync(1, cts.Token);

                    if (arr is AcquireReadSucceeded<U>)
                    {
                        AcquireReadSucceeded<U> succeededResult = (AcquireReadSucceeded<U>)arr;

                        if (succeededResult.ItemCount == 1)
                        {
                            return new GetCancellableResult<U, V>(queue, succeededResult.Items[0], convertResult);
                        }
                        else
                        {
                            System.Diagnostics.Debug.Assert(succeededResult.ItemCount == 0);
                            return new GetEofCancellableResult<U, V>(queue, eofResult);
                        }
                    }
                    else if (arr is AcquireReadFaulted)
                    {
                        AcquireReadFaulted faultedResult = (AcquireReadFaulted)arr;

                        throw faultedResult.Exception;
                    }
                    else
                    {
                        AcquireReadCancelled cancelledResult = (AcquireReadCancelled)arr;

                        throw new OperationCanceledException();
                    }
                };

                Task<CancellableResult<V>> task = null;
                try
                {
                    task = Task.Run(taskSource);
                }
                catch (OperationCanceledException e)
                {
                    task = Task.FromCanceled<CancellableResult<V>>(e.CancellationToken);
                }
                catch (Exception exc)
                {
                    task = Task.FromException<CancellableResult<V>>(exc);
                }

                return new CancellableOperation<V>(task, cts);
            };
        }

        private class PutCancellableResult<U, V> : CancellableResult<V>
        {
            private AsyncQueue<U> queue;
            private U valueToPut;
            private V successfulPut;

            public PutCancellableResult(AsyncQueue<U> queue, U valueToPut, V successfulPut)
            {
                this.queue = queue;
                this.valueToPut = valueToPut;
                this.successfulPut = successfulPut;
            }

            public override V Complete()
            {
                queue.ReleaseWrite(valueToPut);
                return successfulPut;
            }

            public override void Cancel()
            {
                queue.ReleaseWrite();
            }
        }

        public static CancellableOperationStarter<V> StartablePut<U, V>(this AsyncQueue<U> queue, U valueToPut, V successfulPut)
        {
            return delegate (CancellationToken ctoken)
            {
                CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ctoken);

                Func<Task<CancellableResult<V>>> taskSource = async delegate ()
                {
                    AcquireWriteResult awr = await queue.AcquireWriteAsync(1, cts.Token);

                    if (awr is AcquireWriteSucceeded)
                    {
                        AcquireWriteSucceeded aws = (AcquireWriteSucceeded)awr;

                        System.Diagnostics.Debug.Assert(aws.ItemCount == 1);

                        return new PutCancellableResult<U, V>(queue, valueToPut, successfulPut);
                    }
                    else if (awr is AcquireWriteFaulted)
                    {
                        AcquireWriteFaulted awf = (AcquireWriteFaulted)awr;

                        throw awf.Exception;
                    }
                    else
                    {
                        AcquireWriteCancelled awc = (AcquireWriteCancelled)awr;

                        throw new OperationCanceledException();
                    }
                };

                Task<CancellableResult<V>> task = null;
                try
                {
                    task = taskSource();
                }
                catch (OperationCanceledException e)
                {
                    task = Task.FromCanceled<CancellableResult<V>>(e.CancellationToken);
                }
                catch (Exception exc)
                {
                    task = Task.FromException<CancellableResult<V>>(exc);
                }

                return new CancellableOperation<V>(task, cts);
            };
        }

        public static ImmutableList<Tuple<K, CancellableOperationStarter<V>>> OperationStarters<K, V>()
        {
            return ImmutableList<Tuple<K, CancellableOperationStarter<V>>>.Empty;
        }

        public static ImmutableList<Tuple<K, CancellableOperationStarter<V>>> Add<K, V>
        (
            this ImmutableList<Tuple<K, CancellableOperationStarter<V>>> list,
            K key,
            CancellableOperationStarter<V> value
        )
        {
            return list.Add(new Tuple<K, CancellableOperationStarter<V>>(key, value));
        }

        public static Task<Tuple<K, V>> CompleteAny<K, V>(this ImmutableList<Tuple<K, CancellableOperationStarter<V>>> operationStarters, CancellationToken ctoken)
        {
            object syncRoot = new object();
            TaskCompletionSource<Tuple<K, V>> tcs = new TaskCompletionSource<Tuple<K, V>>();

            int waits = 0;
            CancellableOperation<V>[] operations = new CancellableOperation<V>[operationStarters.Count];
            int? firstIndex = null;
            V firstResult = default(V);

            CancellationTokenRegistration? ctr = null;

            Action<CancellationTokenRegistration> setRegistration = delegate (CancellationTokenRegistration value)
            {
                lock (syncRoot)
                {
                    if (firstIndex.HasValue && waits == 0)
                    {
                        value.Dispose();
                    }
                    else
                    {
                        ctr = value;
                    }
                }
            };

            Action deliverFinalResult = delegate ()
            {
                #region

                List<Exception> exc = new List<Exception>();

                foreach (int i in Enumerable.Range(0, operations.Length))
                {
                    if (operations[i] == null) continue;

                    System.Diagnostics.Debug.Assert(operations[i].HasEnded);

                    if (operations[i].Task.IsFaulted)
                    {
                        Exception eTask = operations[i].Task.Exception;
                        if (!(eTask is OperationCanceledException))
                        {
                            exc.Add(eTask);
                        }
                    }
                }

                System.Diagnostics.Debug.Assert(firstIndex.HasValue);
                int fi = firstIndex.Value;

                if (exc.Count == 0)
                {
                    if (fi >= 0)
                    {
                        tcs.SetResult(new Tuple<K, V>(operationStarters[fi].Item1, firstResult));
                    }
                    else
                    {
                        tcs.SetException(new OperationCanceledException(ctoken));
                    }
                }
                else if (exc.Count == 1)
                {
                    tcs.SetException(exc[0]);
                }
                else
                {
                    tcs.SetException(new AggregateException(exc));
                }

                if (ctr.HasValue) { ctr.Value.Dispose(); }

                #endregion
            };

            Action unwind = delegate ()
            {
                int j = operationStarters.Count;
                while (j > 0)
                {
                    --j;
                    if (!firstIndex.HasValue || j != firstIndex.Value)
                    {
                        if (operations[j] != null)
                        {
                            operations[j].Cancel();
                        }
                    }
                }
            };

            Action handleCancellation = delegate ()
            {
                lock (syncRoot)
                {
                    if (!firstIndex.HasValue)
                    {
                        firstIndex = -1;
                        unwind();
                    }
                }
            };

            Action<int> handleItemCompletion = delegate (int i)
            {
                lock(syncRoot)
                {
                    --waits;

                    if (firstIndex.HasValue)
                    {
                        if (operations[i].Task.IsCompletedNormally())
                        {
                            operations[i].Task.Result.Cancel();
                        }
                    }
                    else
                    {
                        firstIndex = i;

                        if (operations[i].Task.IsCompletedNormally())
                        {
                            firstResult = operations[i].Task.Result.Complete();
                        }

                        unwind();
                    }

                    if (waits == 0)
                    {
                        deliverFinalResult();
                    }
                }
            };

            lock(syncRoot)
            {
                bool shouldUnwind = false;

                ctoken.PostRegistration(setRegistration, handleCancellation);

                foreach(int i in Enumerable.Range(0, operationStarters.Count))
                {
                    CancellableOperation<V> op = operationStarters[i].Item2(ctoken);
                    operations[i] = op;
                    if (op.HasEnded)
                    {
                        shouldUnwind = true;
                        firstIndex = i;
                        if (op.Task.IsCompleted)
                        {
                            firstResult = op.Task.Result.Complete();
                        }
                        break;
                    }
                    else
                    {
                        ++waits;
                        op.Task.PostContinueWith
                        (
                            taskUnused =>
                            {
                                handleItemCompletion(i);
                            }
                        );
                    }
                }

                if (shouldUnwind)
                {
                    if (waits > 0)
                    {
                        unwind();
                    }
                    else
                    {
                        deliverFinalResult();
                    }
                }
            }

            return tcs.Task;
        }

#if false
        private abstract class TaskResult<V>
        {

        }

        private class TaskCompleted<V> : TaskResult<V>
        {
            private V value;

            public TaskCompleted(V value)
            {
                this.value = value;
            }

            public V Value { get { return value; } }
        }

        private class TaskFaulted<V> : TaskResult<V>
        {
            private Exception exc;

            public TaskFaulted(Exception exc)
            {
                this.exc = exc;
            }

            public Exception Exception { get { return exc; } }
        }

        private class TaskCancelled<V> : TaskResult<V>
        {
            public TaskCancelled()
            {

            }
        }

        private static void SetResultHandler<V>(this Task<V> task, Action<TaskResult<V>> resultHandler)
        {
            task.PostContinueWith
            (
                task2 =>
                {
                    switch (task2.Status)
                    {
                        case TaskStatus.RanToCompletion:
                            resultHandler(new TaskCompleted<V>(task2.Result));
                            break;
                        case TaskStatus.Faulted:
                            resultHandler(new TaskFaulted<V>(task2.Exception));
                            break;
                        case TaskStatus.Canceled:
                            resultHandler(new TaskCancelled<V>());
                            break;
                        default:
                            System.Diagnostics.Debug.Assert(false, "should never happen");
                            resultHandler(new TaskFaulted<V>(new InvalidOperationException($"Unexpected task status: {task2.Status}")));
                            break;
                    }
                }
            );
        }

        private static async Task HandleSynchronous<V>(Func<Task<V>> startTask, Action<TaskResult<V>> resultHandler)
        {
            try
            {
                V result = await startTask();
                resultHandler(new TaskCompleted<V>(result));
            }
            catch(OperationCanceledException)
            {
                resultHandler(new TaskCancelled<V>());
            }
            catch(Exception exc)
            {
                resultHandler(new TaskFaulted<V>(exc));
            }
        }
#endif

#if false
        private abstract class CompletionState<K, V>
        {

        }

        private class InitializingState<K, V> : CompletionState<K, V>
        {
            private readonly ImmutableList<CancellableOperation<V>> started;
            private readonly ImmutableList<Tuple<K, CancellableOperationStarter<V>>> notYetStarted;

            public InitializingState(ImmutableList<CancellableOperation<V>> started, ImmutableList<Tuple<K, CancellableOperationStarter<V>>> notYetStarted)
            {
                this.started = started;
                this.notYetStarted = notYetStarted;
            }

            public ImmutableList<CancellableOperation<V>> Started { get { return started; } }
            public ImmutableList<Tuple<K, CancellableOperationStarter<V>>> NotYetStarted { get { return notYetStarted; } }
        }

        private class WaitingState<K, V> : CompletionState<K, V>
        {
            private readonly ImmutableList<CancellableOperation<V>> operations;

            public WaitingState(ImmutableList<CancellableOperation<V>> operations)
            {
                this.operations = operations;
            }

            public ImmutableList<CancellableOperation<V>> Operations { get { return operations; } }
        }

        private class UnwindingState<K, V> : CompletionState<K, V>
        {
            private readonly int resultIndex;
            private readonly ImmutableList<CancellableOperation<V>> operations;

            public UnwindingState(int resultIndex, ImmutableList<CancellableOperation<V>> operations)
            {
                this.resultIndex = resultIndex;
                this.operations = operations;
            }

            public int ResultIndex { get { return resultIndex; } }
            public ImmutableList<CancellableOperation<V>> Operations { get { return operations; } }
        }

        private static bool CanStartNext<K, V>(this CompletionState<K, V> state)
        {
            if (!(state is InitializingState<K, V>)) return false;

            InitializingState<K, V> initState = (InitializingState<K, V>)state;

            return initState.NotYetStarted.Count > 0;
        }

        private static CompletionState<K, V> StartNext<K, V>(this CompletionState<K, V> state, Func<Tuple<K, CancellableOperationStarter<V>>, CancellableOperation<V>> startFunc)
        {
            InitializingState<K, V> initState = (InitializingState<K, V>)state;

            CancellableOperation<V> operation = startFunc(initState.NotYetStarted[0]);

            Task task = operation.Task;

            if (task.IsCompleted || task.IsFaulted || task.IsCanceled)
            {
                return new UnwindingState<K, V>(initState.Started.Count, initState.Started);
            }
            else if (initState.NotYetStarted.Count > 0)
            {
                return new InitializingState<K, V>(initState.Started.Add(operation), initState.NotYetStarted.RemoveAt(0));
            }
            else
            {
                return new WaitingState<K, V>(initState.Started.Add(operation));
            }
        }

        public static Task<Tuple<K, V>> CompleteAny<K, V>(ImmutableList<Tuple<K, CancellableOperationStarter<V>>> operations, CancellationToken ctoken)
        {
            object syncRoot = new object();

            CompletionState<K, V> state = new InitializingState<K, V>(ImmutableList<CancellableOperation<V>>.Empty, operations);

            while (state.CanStartNext())
            {
                state = state.StartNext
                (
                    operation =>
                    {
                        return operation.Item2(ctoken);
                    }
                );
            }

            TaskCompletionSource<Tuple<K, V>> tcs = new TaskCompletionSource<Tuple<K, V>>();
            CancellableOperation<V>[] inProgress = new CancellableOperation<V>[operations.Count];
            int outstandingCount = 0;
            int? firstCompletion = null;

            Action allCompleted = delegate ()
            {
                List<Exception> exc = new List<Exception>();
                foreach(int i in Enumerable.Range(0, operations.Count))
                {
                    if (inProgress[i] != null)
                    {
                        if (inProgress[i].Task.IsCompleted)
                        {
                            inProgress[i].Task.Result.Cancel();
                        }
                    }
                }
            };

            Action<int> completedItem = delegate (int i)
            {
                lock(syncRoot)
                {
                    if (firstCompletion.HasValue)
                    {

                        inProgress[i].Task.Result.Cancel();
                    }
                    else
                    {
                        firstCompletion = i;
                    }

                    --outstandingCount;

                    if (outstandingCount == 0)
                    {
                        allCompleted();
                    }
                }
            };

            lock(syncRoot)
            {
                foreach (int i in Enumerable.Range(0, operations.Count))
                {
                    CancellableOperation<V> itemInProgress = operations[i].Item2(ctoken);
                    inProgress[i] = itemInProgress;
                    Task<CancellableResult<V>> task = itemInProgress.Task;
                    if (task.IsCompleted || task.IsFaulted || task.IsCanceled)
                    {
                        firstCompletion = i;
                        break;
                    }
                    else
                    {
                        task.PostContinueWith
                        (
                            task2 =>
                            {
                                lock (syncRoot)
                                {
                                    if (!firstCompletion.HasValue)
                                    {
                                        firstCompletion.Value = i;
                                    }
                                }
                            }
                        );
                    }
                    ++outstandingCount;
                }

                if (firstCompletion.HasValue)
                {
                    int i = firstCompletion.Value;
                    while (i > 0)
                    {
                        --i;
                        inProgress[i].Cancel();
                    }
                }

                if (outstandingCount == 0)
                {

                }
            }


            Action<int, CancellableResult<V>> setCompleted = delegate (int i, CancellableResult<V> result)
            {
                if (firstCompletion.HasValue)
                {
                    result.Cancel();
                }
                else
                {
                    firstCompletion = i;
                }

                    value = new Some<V>(result.Complete());

                    foreach (int j in Enumerable.Range(0, inProgress.Count))
                    {
                        if (j != i)
                        {
                            inProgress[j].Cancel();
                        }
                    }
                }
            };

            Action<int, Exception> faulted = delegate(int i, Exception exc)
            {
                lock(syncRoot)
                {
                    if (firstCompletion.HasValue)
                    {

                    }
                }
            }
            
            
            lock(syncRoot)
            {
                for(int i in Enumerable.Range(0, operations.Count))
                {
                    try
                    {
                        var inProgressItem = operations[i].Item2(ctoken);

                        inProgress = inProgress.Add(inProgressItem);
                        
                        inProgressItem.Task.SetResultHandler
                        (
                            r =>
                            {
                                inProgress[]
                            }
                        )

                        inProgress[i].Task.PostContinueWith
                        (
                            task =>
                            {
                                lock(syncRoot)
                                {

                                }
                            }
                        );
                    }
                    catch(Exception exc)
                    {
                        for (int j in Enumerable.Range(0, i))
                        {
                            inProgress[j].Cancel();
                            try
                            {
                                await 
                            }
                        }
                    }
                }
            }
        }
#endif

        }
    }
