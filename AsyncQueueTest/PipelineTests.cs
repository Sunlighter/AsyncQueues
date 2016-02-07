using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sunlighter.AsyncQueueLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsyncQueueTest
{
    [TestClass]
    public class PipelineTests
    {
        [TestMethod]
        public void PipelineTest()
        {
            Random r = new Random((int)((System.Diagnostics.Stopwatch.GetTimestamp() >> 3) & 0x7FFFFFFF));

            Func<int, Task<long>> proc = async delegate (int w)
            {
                System.Diagnostics.Debug.WriteLine($"proc begin wait for {w}");
                await Task.Delay(200 + r.Next(200));
                System.Diagnostics.Debug.WriteLine($"proc end wait for {w}");
                return (long)w;
            };

            Func<long, Task<int>> proc2 = async delegate (long w)
            {
                System.Diagnostics.Debug.WriteLine($"proc2 begin wait for {w}");
                await Task.Delay(200 + r.Next(200));
                System.Diagnostics.Debug.WriteLine($"proc2 end wait for {w}");
                return (int)w;
            };

            int COUNT = 100;

            var x = Enumerable.Range(0, COUNT)
                .AsQueueSource(5)
                .Select(proc, 5)
                .Select(proc2, 5)
                .AsEnumerable();

            int actualCount = 0;

            foreach (int i in x)
            {
                ++actualCount;
                System.Diagnostics.Debug.WriteLine($"Received {i}");
            }

            Assert.AreEqual(COUNT, actualCount);
        }

        [TestMethod]
        public void ParallelPipelineTest()
        {
            Random r = new Random((int)((System.Diagnostics.Stopwatch.GetTimestamp() >> 3) & 0x7FFFFFFF));

            int COUNT = 64;

            int[] delays = new int[COUNT];
            int[] delays2 = new int[COUNT];
            foreach(int i in Enumerable.Range(0, COUNT))
            {
                delays[i] = 50 + r.Next(200) + (r.Next(5) == 0 ? 500 : 0);
                delays2[i] = 50 + r.Next(200) + (r.Next(5) == 0 ? 500 : 0);
            }

            Func<int, Task<long>> proc = async delegate (int w)
            {
                System.Diagnostics.Debug.WriteLine($"proc begin wait for {w}");
                await Task.Delay(delays[w]);
                System.Diagnostics.Debug.WriteLine($"proc end wait for {w}");
                return (long)w;
            };

            Func<long, Task<bool>> predicate = async delegate (long l)
            {
                System.Diagnostics.Debug.WriteLine($"predicate begin wait for {l}");
                await Task.Delay(delays[(int)l]);
                System.Diagnostics.Debug.WriteLine($"predicate end wait for {l}");
                return (l & 2L) == 0L;
            };

            ParallelWorker pWorker = new ParallelWorker(3);

            var x = Enumerable.Range(0, COUNT)
                .AsQueueSource(5)
                .ParallelSelect(pWorker, proc, 5)
                .ParallelWhere(pWorker, predicate, 5)
                .AsEnumerable();

            int actualCount = 0;

            foreach(long i in x)
            {
                ++actualCount;
                System.Diagnostics.Debug.WriteLine($"Received {i}");
            }

            Assert.AreEqual(COUNT / 2, actualCount);
        }

        [TestMethod]
        public void OrderedParallelPipelineTest()
        {
            Random r = new Random((int)((System.Diagnostics.Stopwatch.GetTimestamp() >> 3) & 0x7FFFFFFF));

            int COUNT = 64;

            int[] delays = new int[COUNT];
            int[] delays2 = new int[COUNT];
            foreach (int i in Enumerable.Range(0, COUNT))
            {
                delays[i] = 50 + r.Next(200) + (r.Next(5) == 0 ? 500 : 0);
                delays2[i] = 50 + r.Next(200) + (r.Next(5) == 0 ? 500 : 0);
            }

            Func<int, Task<long>> proc = async delegate (int w)
            {
                System.Diagnostics.Debug.WriteLine($"proc begin wait for {w}");
                await Task.Delay(delays[w]);
                System.Diagnostics.Debug.WriteLine($"proc end wait for {w}");
                return (long)w;
            };

            Func<long, Task<bool>> predicate = async delegate (long l)
            {
                System.Diagnostics.Debug.WriteLine($"predicate begin wait for {l}");
                await Task.Delay(delays[(int)l]);
                System.Diagnostics.Debug.WriteLine($"predicate end wait for {l}");
                return (l & 2L) == 0L;
            };

            ParallelWorker pWorker = new ParallelWorker(3);

            var x = Enumerable.Range(0, COUNT)
                .AsQueueSource(5)
                .OrderedParallelSelect(pWorker, proc, 5)
                .OrderedParallelWhere(pWorker, predicate, 5)
                .AsEnumerable();

            int actualCount = 0;

            long? oldI = null;

            foreach (long i in x)
            {
                ++actualCount;
                System.Diagnostics.Debug.WriteLine($"Received {i}");
                Assert.IsTrue(!oldI.HasValue || oldI.Value < i);
                oldI = i;
            }

            Assert.AreEqual(COUNT / 2, actualCount);
        }
    }
}
