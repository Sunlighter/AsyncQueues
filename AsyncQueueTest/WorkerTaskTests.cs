using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sunlighter.AsyncQueueLib;
using System.Threading;

namespace Sunlighter.AsyncQueueTest
{
    [TestClass]
    public class WorkerTaskTests
    {
        [TestMethod]
        public void ForEachTest()
        {
            Random r = new Random((int)((System.Diagnostics.Stopwatch.GetTimestamp() >> 3) & 0x7FFFFFFF));

            ExceptionCollector ec = new ExceptionCollector();

            AsyncQueue<int> q1 = new AsyncQueue<int>(5);

            Func<Task> t1 = async delegate ()
            {
                for (int i = 0; i < 100; ++i)
                {
                    await q1.Enqueue(r.Next(1000), ec.CancellationToken);
                }
                q1.WriteEof();
            };

            AsyncQueue<int> q2 = new AsyncQueue<int>(5);

            Func<Task> t2 = WorkerTask.ForEach
            (
                q1,
                async delegate (ForEachInfo<int> fi)
                {
                    await q2.Enqueue(fi.Item * 100, fi.CancellationToken);
                },
                delegate ()
                {
                    q2.WriteEof();
                    return Task.CompletedTask;
                },
                ec
            );

            List<int> items = new List<int>();

            Func<Task> t3 = WorkerTask.ForEach
            (
                q2,
                delegate (ForEachInfo<int> fi)
                {
                    items.Add(fi.Item);
                    return Task.CompletedTask;
                },
                delegate ()
                {
                    return Task.CompletedTask;
                },
                ec
            );

            Task T1 = Task.Run(t1);
            Task T2 = Task.Run(t2);
            Task T3 = Task.Run(t3);

            Task.WaitAll(T1, T2, T3);

            Assert.AreEqual(0, ec.Exceptions.Count);
            Assert.AreEqual(100, items.Count);
        }

        [TestMethod]
        public void ForEachWithExceptionTest()
        {
            Random r = new Random((int)((System.Diagnostics.Stopwatch.GetTimestamp() >> 3) & 0x7FFFFFFF));

            ExceptionCollector ec = new ExceptionCollector();

            AsyncQueue<int> q1 = new AsyncQueue<int>(5);

            Func<Task> t1 = async delegate ()
            {
                for (int i = 0; i < 100; ++i)
                {
                    await q1.Enqueue(r.Next(1000), ec.CancellationToken);
                }
                q1.WriteEof();
            };

            AsyncQueue<int> q2 = new AsyncQueue<int>(5);

            Func<Task> t2 = WorkerTask.ForEach
            (
                q1,
                async delegate (ForEachInfo<int> fi)
                {
                    await q2.Enqueue(fi.Item * 100, fi.CancellationToken);
                    throw new InvalidOperationException("test 1");
                },
                delegate ()
                {
                    q2.WriteEof();
                    return Task.FromException(new InvalidOperationException("test 2"));
                },
                ec
            );

            List<int> items = new List<int>();

            Func<Task> t3 = WorkerTask.ForEach
            (
                q2,
                delegate(ForEachInfo<int> fi)
                {
                    items.Add(fi.Item);
                    return Task.CompletedTask;
                },
                delegate()
                {
                    return Task.CompletedTask;
                },
                ec
            );

            Task T1 = Task.Run(t1);
            Task T2 = Task.Run(t2);
            Task T3 = Task.Run(t3);

            bool threwException = false;
            try
            {
                ec.WaitAll(T1, T2, T3);
            }
            catch(Exception exc)
            {
                System.Diagnostics.Debug.WriteLine(exc);
                threwException = true;
            }

            Assert.IsTrue(threwException);
        }

        [TestMethod]
        public void ParallelForEachTest()
        {
            object rSyncRoot = new object();
            Random r = new Random((int)((System.Diagnostics.Stopwatch.GetTimestamp() >> 3) & 0x7FFFFFFF));

            ExceptionCollector ec = new ExceptionCollector();

            AsyncQueue<int> q1 = new AsyncQueue<int>(5);

            Func<Task> t1 = async delegate ()
            {
                for (int i = 0; i < 100; ++i)
                {
                    await q1.Enqueue(r.Next(1000), ec.CancellationToken);
                }
                q1.WriteEof();
            };

            AsyncQueue<int> q2 = new AsyncQueue<int>(5);

            ParallelWorker pw = new ParallelWorker(8);

            Func<Task> t2 = WorkerTask.ParallelForEach
            (
                q1,
                pw,
                async delegate (ForEachInfo<int> fi)
                {
                    await q2.Enqueue(fi.Item * 100, fi.CancellationToken);

                    int time;
                    lock (rSyncRoot)
                    {
                        time = 100 + r.Next(800);
                    }
                    await Task.Delay(time);
                },
                delegate ()
                {
                    q2.WriteEof();
                    return Task.CompletedTask;
                },
                ec
            );

            List<int> items = new List<int>();

            Func<Task> t3 = WorkerTask.ForEach
            (
                q2,
                delegate (ForEachInfo<int> fi)
                {
                    items.Add(fi.Item);
                    System.Diagnostics.Debug.WriteLine(fi.Item);
                    return Task.CompletedTask;
                },
                delegate ()
                {
                    return Task.CompletedTask;
                },
                ec
            );

            Task T1 = Task.Run(t1);
            Task T2 = Task.Run(t2);
            Task T3 = Task.Run(t3);

            Task.WaitAll(T1, T2, T3);

            Assert.AreEqual(0, ec.Exceptions.Count);
            Assert.AreEqual(100, items.Count);
        }

        [TestMethod]
        public void ParallelForEachExceptionTest()
        {
            ExceptionCollector ec = new ExceptionCollector();

            AsyncQueue<int> q1 = new AsyncQueue<int>(5);

            Func<Task> t1 = async delegate ()
            {
                for (int i = 0; i < 100; ++i)
                {
                    await q1.Enqueue(i, ec.CancellationToken);
                }
                q1.WriteEof();
            };

            AsyncQueue<int> q2 = new AsyncQueue<int>(5);

            Func<Task> t2 = WorkerTask.ForEach
            (
                q1,
                async delegate (ForEachInfo<int> fi)
                {
                    await q2.Enqueue(fi.Item * 100, fi.CancellationToken);
                    if (fi.Item == 53) throw new InvalidOperationException("test 1");
                },
                delegate ()
                {
                    q2.WriteEof();
                    return Task.CompletedTask;
                },
                ec
            );

            List<int> items = new List<int>();

            Func<Task> t3 = WorkerTask.ForEach
            (
                q2,
                delegate (ForEachInfo<int> fi)
                {
                    items.Add(fi.Item);
                    return Task.CompletedTask;
                },
                delegate ()
                {
                    return Task.CompletedTask;
                },
                ec
            );

            Task T1 = Task.Run(t1);
            Task T2 = Task.Run(t2);
            Task T3 = Task.Run(t3);

            bool threwException = false;
            try
            {
                ec.WaitAll(T1, T2, T3);
            }
            catch (Exception exc)
            {
                System.Diagnostics.Debug.WriteLine(exc);
                threwException = true;
            }

            Assert.IsTrue(threwException);
        }

        [TestMethod]
        public void MultiSourceForEachTest()
        {
            ExceptionCollector ec = new ExceptionCollector();

            AsyncQueue<int> q1 = new AsyncQueue<int>(5);

            Func<Task> t1 = async delegate ()
            {
                for (int i = 0; i < 100; ++i)
                {
                    await q1.Enqueue(i, ec.CancellationToken);
                }
                q1.WriteEof();
            };

            AsyncQueue<int> q2 = new AsyncQueue<int>(5);

            Func<Task> t2 = async delegate ()
            {
                for (int i = 100; i < 200; ++i)
                {
                    await q2.Enqueue(i, ec.CancellationToken);
                }
                q2.WriteEof();
            };

            AsyncQueue<int> q3 = new AsyncQueue<int>(5);

            Func<Task> t3 = WorkerTask.ForEach
            (
                new IQueueSource<int>[] { q1, q2 },
                InputPriorities.RoundRobin,
                async fei =>
                {
                    await q3.Enqueue(fei.Item, fei.CancellationToken);
                },
                () =>
                {
                    q3.WriteEof();
                    return Task.CompletedTask;
                },
                ec
            );

            Func<Task> t4 = WorkerTask.ForEach
            (
                q3,
                fei =>
                {
                    System.Diagnostics.Debug.WriteLine(fei.Item);
                    return Task.CompletedTask;
                },
                null,
                ec
            );

            Task T1 = Task.Run(t1);
            Task T2 = Task.Run(t2);
            Task T3 = Task.Run(t3);
            Task T4 = Task.Run(t4);

            ec.WaitAll(T1, T2, T3, T4);
        }

        [TestMethod]
        public void MultiSourceForEachExceptionTest()
        {
            ExceptionCollector ec = new ExceptionCollector();

            AsyncQueue<int> q1 = new AsyncQueue<int>(5);

            Func<Task> t1 = async delegate ()
            {
                for (int i = 0; i < 100; ++i)
                {
                    await q1.Enqueue(i, ec.CancellationToken);
                }
                q1.WriteEof();
            };

            AsyncQueue<int> q2 = new AsyncQueue<int>(5);

            Func<Task> t2 = async delegate ()
            {
                for (int i = 100; i < 200; ++i)
                {
                    await q2.Enqueue(i, ec.CancellationToken);
                }
                q2.WriteEof();
            };

            AsyncQueue<int> q3 = new AsyncQueue<int>(5);

            Func<Task> t3 = WorkerTask.ForEach
            (
                new IQueueSource<int>[] { q1, q2 },
                InputPriorities.RoundRobin,
                async fei =>
                {
                    if (fei.Item == 133) throw new InvalidOperationException("test");
                    await q3.Enqueue(fei.Item, fei.CancellationToken);
                },
                () =>
                {
                    q3.WriteEof();
                    return Task.CompletedTask;
                },
                ec
            );

            Func<Task> t4 = WorkerTask.ForEach
            (
                q3,
                fei =>
                {
                    System.Diagnostics.Debug.WriteLine(fei.Item);
                    return Task.CompletedTask;
                },
                null,
                ec
            );

            Task T1 = Task.Run(t1);
            Task T2 = Task.Run(t2);
            Task T3 = Task.Run(t3);
            Task T4 = Task.Run(t4);

            try
            {
                ec.WaitAll(T1, T2, T3, T4);
            }
            catch (Exception exc)
            {
                System.Diagnostics.Debug.WriteLine(exc);
            }
        }

        [TestMethod]
        public void ParallelMultiSourceForEachTest()
        {
            object rSyncRoot = new object();
            Random r = new Random((int)((System.Diagnostics.Stopwatch.GetTimestamp() >> 3) & 0x7FFFFFFF));

            ExceptionCollector ec = new ExceptionCollector();

            AsyncQueue<int> q1 = new AsyncQueue<int>(5);

            Func<Task> t1 = async delegate ()
            {
                for (int i = 0; i < 100; ++i)
                {
                    await q1.Enqueue(i, ec.CancellationToken);
                }
                q1.WriteEof();
            };

            AsyncQueue<int> q2 = new AsyncQueue<int>(5);

            Func<Task> t2 = async delegate ()
            {
                for (int i = 100; i < 200; ++i)
                {
                    await q2.Enqueue(i, ec.CancellationToken);
                }
                q2.WriteEof();
            };

            AsyncQueue<int> q3 = new AsyncQueue<int>(5);

            ParallelWorker pw = new ParallelWorker(8);

            Func<Task> t3 = WorkerTask.ParallelForEach
            (
                new IQueueSource<int>[] { q1, q2 },
                InputPriorities.RoundRobin,
                pw,
                async fei =>
                {
                    await q3.Enqueue(fei.Item, fei.CancellationToken);

                    int time;
                    lock(rSyncRoot)
                    {
                        time = 100 + r.Next(800);
                    }
                    await Task.Delay(time);
                },
                () =>
                {
                    q3.WriteEof();
                    return Task.CompletedTask;
                },
                ec
            );

            Func<Task> t4 = WorkerTask.ForEach
            (
                q3,
                fei =>
                {
                    System.Diagnostics.Debug.WriteLine(fei.Item);
                    return Task.CompletedTask;
                },
                null,
                ec
            );

            Task T1 = Task.Run(t1);
            Task T2 = Task.Run(t2);
            Task T3 = Task.Run(t3);
            Task T4 = Task.Run(t4);

            ec.WaitAll(T1, T2, T3, T4);
        }

        [TestMethod]
        public void ParallelMultiSourceForEachExceptionTest()
        {
            object rSyncRoot = new object();
            Random r = new Random((int)((System.Diagnostics.Stopwatch.GetTimestamp() >> 3) & 0x7FFFFFFF));

            ExceptionCollector ec = new ExceptionCollector();

            AsyncQueue<int> q1 = new AsyncQueue<int>(5);

            Func<Task> t1 = async delegate ()
            {
                for (int i = 0; i < 100; ++i)
                {
                    await q1.Enqueue(i, ec.CancellationToken);
                }
                q1.WriteEof();
            };

            AsyncQueue<int> q2 = new AsyncQueue<int>(5);

            Func<Task> t2 = async delegate ()
            {
                for (int i = 100; i < 200; ++i)
                {
                    await q2.Enqueue(i, ec.CancellationToken);
                }
                q2.WriteEof();
            };

            AsyncQueue<int> q3 = new AsyncQueue<int>(5);

            ParallelWorker pw = new ParallelWorker(8);

            Func<Task> t3 = WorkerTask.ParallelForEach
            (
                new IQueueSource<int>[] { q1, q2 },
                InputPriorities.RoundRobin,
                pw,
                async fei =>
                {
                    await q3.Enqueue(fei.Item, fei.CancellationToken);

                    if (fei.Item == 133) throw new InvalidOperationException("test");

                    int time;
                    lock (rSyncRoot)
                    {
                        time = 100 + r.Next(800);
                    }
                    await Task.Delay(time);
                },
                () =>
                {
                    q3.WriteEof();
                    return Task.CompletedTask;
                },
                ec
            );

            Func<Task> t4 = WorkerTask.ForEach
            (
                q3,
                fei =>
                {
                    System.Diagnostics.Debug.WriteLine(fei.Item);
                    return Task.CompletedTask;
                },
                null,
                ec
            );

            Task T1 = Task.Run(t1);
            Task T2 = Task.Run(t2);
            Task T3 = Task.Run(t3);
            Task T4 = Task.Run(t4);

            try
            {
                ec.WaitAll(T1, T2, T3, T4);
            }
            catch(Exception exc)
            {
                System.Diagnostics.Debug.WriteLine(exc);
            }
        }
    }
}
