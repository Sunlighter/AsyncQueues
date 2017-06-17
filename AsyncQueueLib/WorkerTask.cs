using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sunlighter.AsyncQueueLib
{
    public static class WorkerTask
    {
        public static Func<Task> ForEach<T>(IQueueSource<T> source, Func<ForEachInfo<T>, Task> processAsync, Func<Task> onCloseAsync, CancellationToken ctoken)
        {
            Func<Task> t = async delegate ()
            {
                ExceptionAggregator ea = new ExceptionAggregator();
                try
                {
                    while (true)
                    {
                        var item = await source.Dequeue(ctoken);
                        if (!item.HasValue) break;
                        try
                        {
                            await processAsync(new ForEachInfo<T>(item.Value, 0, 0, ctoken));
                        }
                        catch(Exception exc)
                        {
                            ea.Add(exc);
                            break;
                        }
                    }
                }
                finally
                {
                    if (onCloseAsync != null)
                    {
                        try
                        {
                            await onCloseAsync();
                        }
                        catch(Exception exc)
                        {
                            ea.Add(exc);
                        }
                    }
                }

                ea.ThrowAll();
            };

            return t;
        }

        public static Func<Task> ForEach<T, U>(IQueueSource<T>[] sources, InputPriorities inputPriorities, Func<ForEachInfo<T>, Task> processAsync, Func<Task> onCloseAsync, CancellationToken ctoken)
        {
            Func<Task> t = async delegate ()
            {
                try
                {
                    int sourceCount = sources.Length;
                    bool[] atEof = new bool[sourceCount];
                    RoundRobinLoopGenerator loop = new RoundRobinLoopGenerator(sourceCount, inputPriorities);
                    while (!(atEof.All(e => e)))
                    {
                        var ops = Utils.OperationStarters<int, Option<T>>();

                        loop.ForEach
                        (
                            j => { ops = ops.AddIf(!atEof[j], j, Utils.StartableGet<T, Option<T>>(sources[j], a => new Some<T>(a), new None<T>())); }
                        );

                        Tuple<int, Option<T>> result = await ops.CompleteAny(ctoken);

                        if (result.Item2.HasValue)
                        {
                            await processAsync(new ForEachInfo<T>(result.Item2.Value, result.Item1, 0, ctoken));
                        }
                        else
                        {
                            atEof[result.Item1] = true;
                        }
                    }
                }
                finally
                {
                    if (onCloseAsync != null)
                    {
                        await onCloseAsync();
                    }
                }
            };

            return t;
        }

        public static Func<Task> ParallelForEach<T>(IQueueSource<T> source, ParallelWorker parallelWorker, Func<ForEachInfo<T>, Task> processAsync, Func<Task> onCloseAsync, CancellationToken ctoken)
        {
            Func<Task> t = async delegate ()
            {
                object syncRootExceptions = new object();
                List<Exception> exceptions = new List<Exception>();
                CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ctoken);
                CancellationToken ctokenInternal = cts.Token;
                IdleDetector idleDetector = new IdleDetector();
                try
                {
                    while (true)
                    {
                        var item = await source.Dequeue(ctokenInternal);
                        if (!item.HasValue) break;
                        T itemValue = item.Value;

                        idleDetector.Enter();

                        await parallelWorker.StartWorkItem
                        (
                            async workerId =>
                            {
                                try
                                {
                                    await processAsync(new ForEachInfo<T>(itemValue, 0, workerId, ctokenInternal));
                                }
                                catch(Exception exc)
                                {
                                    lock(syncRootExceptions)
                                    {
                                        exceptions.Add(exc);
                                    }
                                    cts.Cancel();
                                }
                                finally
                                {
                                    idleDetector.Leave();
                                }
                            },
                            ctokenInternal
                        );
                    }
                }
                finally
                {
                    await idleDetector.WaitForIdle(CancellationToken.None);
                    if (onCloseAsync != null)
                    {
                        try
                        {
                            await onCloseAsync();
                        }
                        catch(Exception exc)
                        {
                            exceptions.Add(exc);
                        }
                    }
                }
                if (exceptions.Count == 1)
                {
                    throw exceptions[0];
                }
                else if (exceptions.Count > 1)
                {
                    throw new AggregateException(exceptions);
                }
            };

            return t;
        }

        public static Func<Task> ParallelForEach<T, U>(IQueueSource<T>[] sources, InputPriorities inputPriorities, ParallelWorker parallelWorker, Func<ForEachInfo<T>, Task> processAsync, Func<Task> onCloseAsync, CancellationToken ctoken)
        {
            Func<Task> t = async delegate ()
            {
                object syncRootExceptions = new object();
                List<Exception> exceptions = new List<Exception>();
                CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ctoken);
                CancellationToken ctokenInternal = cts.Token;
                IdleDetector idleDetector = new IdleDetector();
                try
                {
                    int sourceCount = sources.Length;
                    bool[] atEof = new bool[sourceCount];
                    RoundRobinLoopGenerator loop = new RoundRobinLoopGenerator(sourceCount, inputPriorities);
                    while (!(atEof.All(e => e)))
                    {
                        var ops = Utils.OperationStarters<int, Option<T>>();

                        loop.ForEach
                        (
                            j => { ops = ops.AddIf(!atEof[j], j, Utils.StartableGet<T, Option<T>>(sources[j], a => new Some<T>(a), new None<T>())); }
                        );

                        Tuple<int, Option<T>> result = await ops.CompleteAny(ctoken);

                        if (result.Item2.HasValue)
                        {
                            int sourceIndex = result.Item1;
                            T itemValue = result.Item2.Value;

                            idleDetector.Enter();

                            await parallelWorker.StartWorkItem
                            (
                                async workerId =>
                                {
                                    try
                                    {
                                        await processAsync(new ForEachInfo<T>(itemValue, sourceIndex, workerId, ctokenInternal));
                                    }
                                    catch(Exception exc)
                                    {
                                        lock(syncRootExceptions)
                                        {
                                            exceptions.Add(exc);
                                        }
                                    }
                                    finally
                                    {
                                        idleDetector.Leave();
                                    }
                                },
                                ctoken
                            );
                        }
                        else
                        {
                            atEof[result.Item1] = true;
                        }
                    }
                }
                finally
                {
                    if (onCloseAsync != null)
                    {
                        try
                        {
                            await onCloseAsync();
                        }
                        catch(Exception exc)
                        {
                            exceptions.Add(exc);
                        }
                    }
                }
                if (exceptions.Count == 1)
                {
                    throw exceptions[0];
                }
                else if (exceptions.Count > 1)
                {
                    throw new AggregateException(exceptions);
                }
            };

            return t;
        }
    }

    public enum InputPriorities
    {
        AsWritten,
        RoundRobin
    }

    public class ForEachInfo<T>
    {
        private T item;
        private int inputIndex;
        private int workerId;
        private CancellationToken ctoken;

        public ForEachInfo(T item, int inputIndex, int workerId, CancellationToken ctoken)
        {
            this.item = item;
            this.inputIndex = inputIndex;
            this.workerId = workerId;
            this.ctoken = ctoken;
        }

        public T Item => item;
        public int InputIndex => inputIndex;
        public int WorkerId => workerId;
        public CancellationToken CancellationToken => ctoken;
    }

    public class RoundRobinLoopGenerator
    {
        private int size;
        private InputPriorities inputPriorities;
        private int counter;

        public RoundRobinLoopGenerator(int size, InputPriorities inputPriorities)
        {
            this.size = size;
            this.inputPriorities = inputPriorities;
            this.counter = 0;
        }

        public void ForEach(Action<int> action)
        {
            for (int i = 0; i < size; ++i)
            {
                int j;
                if (inputPriorities == InputPriorities.AsWritten)
                {
                    j = i;
                }
                else
                {
                    j = i + counter;
                    if (j >= size) j -= size;
                }

                action(j);
            }

            ++counter;
            if (counter >= size) counter -= size;
        }
    }

    public class ExceptionAggregator
    {
        private object syncRoot;
        private ImmutableList<Exception> exceptions;

        public ExceptionAggregator()
        {
            syncRoot = new object();
            exceptions = ImmutableList<Exception>.Empty;
        }

        private void AddInternal(Exception exc)
        {
            if (exc is AggregateException)
            {
                foreach(Exception e2 in ((AggregateException)exc).InnerExceptions)
                {
                    AddInternal(e2);
                }
            }
            else
            {
                exceptions = exceptions.Add(exc);
            }
        }

        public void Add(Exception exc)
        {
            lock (syncRoot)
            {
                AddInternal(exc);
            }
        }

        public void ThrowAll()
        {
            lock(syncRoot)
            {
                if (exceptions.Count == 1)
                {
                    throw exceptions[0];
                }
                else if (exceptions.Count > 1)
                {
                    throw new AggregateException(exceptions);
                }
                else
                {
                    // do nothing
                }
            }
        }
    }
}
