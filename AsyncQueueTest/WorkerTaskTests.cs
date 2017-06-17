﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
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

            AsyncQueue<int> q1 = new AsyncQueue<int>(5);

            Func<Task> t1 = async delegate ()
            {
                for (int i = 0; i < 100; ++i)
                {
                    await q1.Enqueue(r.Next(1000), CancellationToken.None);
                }
                q1.WriteEof();
            };

            AsyncQueue<int> q2 = new AsyncQueue<int>(5);

            Func<Task> t2 = WorkerTask.ForEach
            (
                q1,
                async delegate (ForEachInfo<int> fi)
                {
                    await q2.Enqueue(fi.Item * 100, CancellationToken.None);
                },
                delegate ()
                {
                    q2.WriteEof();
                    return Task.CompletedTask;
                },
                CancellationToken.None
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
                CancellationToken.None
            );

            Task T1 = Task.Run(t1);
            Task T2 = Task.Run(t2);
            Task T3 = Task.Run(t3);

            Task.WaitAll(T1, T2, T3);

            Assert.AreEqual(100, items.Count);
        }

        [TestMethod]
        public void ForEachWithExceptionTest()
        {
            Random r = new Random((int)((System.Diagnostics.Stopwatch.GetTimestamp() >> 3) & 0x7FFFFFFF));

            CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken ctoken = cts.Token;

            AsyncQueue<int> q1 = new AsyncQueue<int>(5);

            Func<Task> t1 = async delegate ()
            {
                for (int i = 0; i < 100; ++i)
                {
                    await q1.Enqueue(r.Next(1000), ctoken);
                }
                q1.WriteEof();
            };

            AsyncQueue<int> q2 = new AsyncQueue<int>(5);

            Func<Task> t2 = WorkerTask.ForEach
            (
                q1,
                async delegate (ForEachInfo<int> fi)
                {
                    await q2.Enqueue(fi.Item * 100, ctoken);
                    cts.Cancel();
                    throw new InvalidOperationException("test 1");
                },
                delegate ()
                {
                    q2.WriteEof();
                    return Task.FromException(new InvalidOperationException("test 2"));
                },
                ctoken
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
                ctoken
            );

            Task T1 = Task.Run(t1);
            Task T2 = Task.Run(t2);
            Task T3 = Task.Run(t3);

            bool threwException = false;
            try
            {
                Task.WaitAll(T1, T2, T3);
            }
            catch(Exception exc)
            {
                threwException = true;
                Assert.IsInstanceOfType(exc, typeof(AggregateException));
            }

            Assert.IsTrue(threwException);
        }
    }
}