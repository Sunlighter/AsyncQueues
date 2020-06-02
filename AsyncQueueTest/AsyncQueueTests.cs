using Sunlighter.AsyncQueueLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Immutable;

namespace Sunlighter.AsyncQueueTest
{
    [TestClass]
    public class AsyncQueueTests
    {
        private async Task SimpleQueueTestAsync()
        {
            AsyncQueue<int> queue = new AsyncQueue<int>(11);

            Func<Task> producer = async delegate()
            {
                #region

                for (int i = 0; i < 20; ++i)
                {
                    System.Diagnostics.Debug.WriteLine("Acquiring write...");
                    AcquireWriteResult result = await queue.AcquireWriteAsync(1, CancellationToken.None);
                    result.Visit<DBNull>
                    (
                        new Func<AcquireWriteSucceeded, DBNull>
                        (
                            succeeded =>
                            {
                                System.Diagnostics.Debug.WriteLine("Write-acquire succeeded, offset " + succeeded.Offset + ", acquired " + succeeded.ItemCount + " spaces");

                                Assert.AreEqual(1, succeeded.ItemCount);
                            
                                if (succeeded.ItemCount >= 1)
                                {
                                    System.Diagnostics.Debug.WriteLine("Releasing write (1)...");
                                    queue.ReleaseWrite(i);
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine("Releasing write (0)...");
                                    queue.ReleaseWrite();
                                }

                                return DBNull.Value;
                            }
                        ),
                        new Func<AcquireWriteCancelled, DBNull>
                        (
                            cancelled =>
                            {
                                throw new OperationCanceledException();
                            }
                        ),
                        new Func<AcquireWriteFaulted, DBNull>
                        (
                            faulted =>
                            {
                                throw faulted.Exception;
                            }
                        )
                    );
                }

                System.Diagnostics.Debug.WriteLine("Writing EOF...");

                queue.WriteEof();

                #endregion
            };

            Func<Task> consumer = async delegate()
            {
                #region

                bool more = true;
                while (more)
                {
                    System.Diagnostics.Debug.WriteLine("Acquiring read...");
                    const int ACQUIRE_COUNT = 3;
                    AcquireReadResult result = await queue.AcquireReadAsync(ACQUIRE_COUNT, CancellationToken.None);
                    result.Visit<DBNull>
                    (
                        new Func<AcquireReadSucceeded, DBNull>
                        (
                            succeeded =>
                            {
                                System.Diagnostics.Debug.WriteLine("Read-acquire succeeded, offset " + succeeded.Offset + ", acquired " + succeeded.ItemCount + " items");
                                Assert.IsInstanceOfType(succeeded, typeof(AcquireReadSucceeded<int>));

                                if (succeeded is AcquireReadSucceeded<int>)
                                {
                                    AcquireReadSucceeded<int> succeeded2 = (AcquireReadSucceeded<int>)succeeded;

                                    System.Diagnostics.Debug.WriteLine("{ " + string.Join(", ", succeeded2.Items) + " }");
                                }

                                if (succeeded.ItemCount < ACQUIRE_COUNT)
                                {
                                    System.Diagnostics.Debug.WriteLine("Setting \"more\" flag to false...");
                                    more = false;
                                }

                                System.Diagnostics.Debug.WriteLine("Releasing read (" + succeeded.ItemCount + ")...");
                                queue.ReleaseRead(succeeded.ItemCount);

                                return DBNull.Value;
                            }
                        ),
                        new Func<AcquireReadCancelled, DBNull>
                        (
                            cancelled =>
                            {
                                throw new OperationCanceledException();
                            }
                        ),
                        new Func<AcquireReadFaulted, DBNull>
                        (
                            faulted =>
                            {
                                throw faulted.Exception;
                            }
                        )
                    );
                }

                #endregion
            };

            Task tProducer = Task.Run(producer);
            Task tConsumer = Task.Run(consumer);

            await Task.WhenAll(tProducer, tConsumer);
        }

        [TestMethod]
        public void SimpleQueueTest()
        {
            Task t = Task.Run(new Func<Task>(SimpleQueueTestAsync));
            t.Wait();
        }

        private async Task ExtMethodQueueTestAsync()
        {
            AsyncQueue<string> q = new AsyncQueue<string>(5);

            Func<Task> producer = async delegate ()
            {
                #region

                for (int i = 0; i < 20; ++i)
                {
                    System.Diagnostics.Debug.WriteLine($"Writing {i}...");
                    await q.Enqueue($"Value: {i}", CancellationToken.None);
                }

                System.Diagnostics.Debug.WriteLine("Writing EOF...");

                q.WriteEof();

                #endregion
            };

            Func<Task> consumer = async delegate ()
            {
                #region

                bool more = true;
                while (more)
                {
                    System.Diagnostics.Debug.WriteLine("Reading...");
                    Option<string> ostr = await q.Dequeue(CancellationToken.None);

                    if (ostr.HasValue)
                    {
                        System.Diagnostics.Debug.WriteLine($"Read {ostr.Value}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Read EOF...");
                        more = false;
                    }

                }

                #endregion
            };

            Task tProducer = Task.Run(producer);
            Task tConsumer = Task.Run(consumer);

            await Task.WhenAll(tProducer, tConsumer);
        }

        [TestMethod]
        public void ExtMethodQueueTest()
        {
            Task t = Task.Run(new Func<Task>(ExtMethodQueueTestAsync));
            t.Wait();
        }

        #region Resources for GetAnyTest

        private class Item
        {
            public int delay;
            public int value;
        }

        private List<Item> GenerateItems(Random r, int itemCount, int minDelay, int maxDelay)
        {
            List<Item> list = new List<Item>();
            for(int i = 0; i < itemCount; ++i)
            {
                list.Add(new Item() { value = r.Next(minDelay, maxDelay) });
            }
            List<Item> list2 = new List<Item>();
            list2.AddRange(list.OrderBy(i => i.value));
            int previous = 0;
            foreach(Item i in list2)
            {
                i.delay = i.value - previous;
                previous = i.value;
            }
            return list2;
        }

        private async Task PutItems(string prefix, AsyncQueue<string> dest, List<Item> items)
        {
            foreach(Item i in items)
            {
                await Task.Delay(i.delay);
                await dest.Enqueue(prefix + i.value, CancellationToken.None);
            }

            dest.WriteEof();
        }

        private async Task GetItems(AsyncQueue<string> src1, AsyncQueue<string> src2)
        {
            bool src1eof = false;
            bool src2eof = false;

            while (!src1eof || !src2eof)
            {
                var starters = Utils.OperationStarters<int, string>()
                    .AddIf(!src1eof, 1, Utils.StartableGet(src1, a => a, null))
                    .AddIf(!src2eof, 2, Utils.StartableGet(src2, a => a, null));

                Tuple<int, string> result;
                using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMinutes(0.5)))
                {
                    result = await starters.CompleteAny(cts.Token);
                }

                if (result.Item2 == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Source {result.Item1} EOF");

                    if (result.Item1 == 1)
                    {    
                        src1eof = true;
                    }
                    else if (result.Item1 == 2)
                    {
                        src2eof = true;
                    }
                    else
                    {
                        Assert.Fail("Result did not correspond to any known source");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Source {result.Item1} : \"{result.Item2}\"");
                }
            }
        }

        #endregion

        private async Task GetAnyTestAsync()
        {
            Random r = new Random((int)((System.Diagnostics.Stopwatch.GetTimestamp() >> 3) & 0x7FFFFFFF));

            List<Item> items1 = GenerateItems(r, 300, 0, 12000);
            List<Item> items2 = GenerateItems(r, 300, 1000, 13000);

            AsyncQueue<string> q1 = new AsyncQueue<string>(3);
            AsyncQueue<string> q2 = new AsyncQueue<string>(3);

            Task p1 = Task.Run(new Func<Task>(() => PutItems("A", q1, items1)));
            Task p2 = Task.Run(new Func<Task>(() => PutItems("B", q2, items2)));
            Task c = Task.Run(new Func<Task>(() => GetItems(q1, q2)));

            await Task.WhenAll(p1, p2, c);
        }

        [TestMethod]
        public void GetAnyTest()
        {
            Task t = Task.Run(new Func<Task>(GetAnyTestAsync));
            t.Wait(TimeSpan.FromMinutes(1.0));
            Assert.AreEqual(TaskStatus.RanToCompletion, t.Status);
        }

        private async Task CompleteAnyWithCancellationAsync()
        {
            AsyncQueue<string> q1 = new AsyncQueue<string>(5);
            AsyncQueue<string> q2 = new AsyncQueue<string>(5);

            Func<Task> producer1 = async delegate ()
            {
                await Task.Delay(2500);
                await q1.Enqueue("one", CancellationToken.None);
                q1.WriteEof();
            };

            Func<Task> producer2 = async delegate ()
            {
                await Task.Delay(5500);
                await q2.Enqueue("two", CancellationToken.None);
                q2.WriteEof();
            };

            Func<Task> consumer = async delegate ()
            {
                bool q1eof = false;
                bool q2eof = false;
                while (!q1eof || !q2eof)
                {
                    Tuple<int, string> r1 = null;

                    using (CancellationTokenSource s1 = new CancellationTokenSource(1000))
                    {
                        try
                        {
                            r1 = await Utils.OperationStarters<int, string>()
                                .AddIf(!q1eof, 1, Utils.StartableGet(q1, x => x, null))
                                .AddIf(!q2eof, 2, Utils.StartableGet(q2, x => x, null))
                                .CompleteAny(s1.Token);
                        }
                        catch (OperationCanceledException)
                        {

                        }
                    }

                    if (r1 == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Cancelled (because of timeout)");
                    }
                    else if (r1.Item1 == 1)
                    {
                        if (r1.Item2 == null)
                        {
                            System.Diagnostics.Debug.WriteLine("Got EOF from 1");
                            q1eof = true;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Got {r1.Item2} from 1");
                        }
                    }
                    else
                    {
                        Assert.AreEqual(2, r1.Item1);
                        if (r1.Item2 == null)
                        {
                            System.Diagnostics.Debug.WriteLine("Got EOF from 2");
                            q2eof = true;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Got {r1.Item2} from 2");
                        }
                    }
                }
            };

            Task p1 = Task.Run(producer1);
            Task p2 = Task.Run(producer2);
            Task c = Task.Run(consumer);

            await Task.WhenAll(p1, p2, c);
        }

        [TestMethod]
        public void CompleteAnyWithCancellation()
        {
            Task t = Task.Run(new Func<Task>(CompleteAnyWithCancellationAsync));
            t.Wait();
        }

        [TestMethod]
        public async Task ReceiveContentionTest()
        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10.0));
            //CancellationTokenSource cts = new CancellationTokenSource();

            ImmutableList<Func<Task>> tasks = ImmutableList<Func<Task>>.Empty;

            AsyncQueue<int> q = new AsyncQueue<int>(8);

            int readerCount = 3;

            //SemaphoreSlim ss = new SemaphoreSlim(0, readerCount);

            foreach(int i in Enumerable.Range(0, readerCount))
            {
                Func<Task> reader = async delegate ()
                {
                    try
                    {
                        while(true)
                        {
                            System.Diagnostics.Debug.WriteLine($"Reader {i} waiting for value");
                            Option<int> oi = await q.Dequeue(cts.Token);
                            if (!oi.HasValue) break;
                            System.Diagnostics.Debug.WriteLine($"Reader {i} received {oi.Value}");
                        }
                    }
                    finally
                    {
                        System.Diagnostics.Debug.WriteLine($"Reader {i} exited");
                        //ss.Release();
                    }
                };

                tasks = tasks.Add(reader);
            }

            ImmutableList<Task> runningTasks = tasks.Select(t => Task.Run(t)).ToImmutableList();

            await q.Enqueue(100, cts.Token);

            q.WriteEof();

            //for(int i = 0; i < readerCount; ++i)
            //{
            //    await ss.WaitAsync();
            //}

            await Task.WhenAll(runningTasks);

            System.Diagnostics.Debug.WriteLine("Done");

            Assert.IsFalse(cts.IsCancellationRequested);
        }
    }
}
