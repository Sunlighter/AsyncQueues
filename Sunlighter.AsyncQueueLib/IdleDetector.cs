using Sunlighter.OptionLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sunlighter.AsyncQueueLib
{
    public class IdleDetector
    {
        private readonly object syncRoot;
        private int referenceCount;
        private readonly CancellableQueue<Waiter> waiters;

        private class Waiter
        {
            public long? id;
            public TaskCompletionSource<bool> k;
            public CancellationToken ctoken;
            public CancellationTokenRegistration? ctr;
        }

        public IdleDetector()
        {
            syncRoot = new object();
            referenceCount = 0;
            waiters = new CancellableQueue<Waiter>();
        }

        public void Enter()
        {
            lock(syncRoot)
            {
                ++referenceCount;
            }
        }

        public void Leave()
        {
            lock(syncRoot)
            {
                --referenceCount;

                if (referenceCount == 0)
                {
                    while (waiters.Count > 0)
                    {
                        Waiter waiter = waiters.Dequeue();

                        waiter.k.PostResult(true);

                        if (waiter.ctr.HasValue)
                        {
                            waiter.ctr.Value.Dispose();
                        }
                    }
                }
            }
        }

        private void CancelWait(long id)
        {
            lock(syncRoot)
            {
                Option<Waiter> opt = waiters.Cancel(id);
                if (opt.HasValue)
                {
                    opt.Value.k.PostException(new OperationCanceledException(opt.Value.ctoken));

                    if (opt.Value.ctr.HasValue)
                    {
                        opt.Value.ctr.Value.Dispose();
                    }
                }
            }
        }

        private void SetRegistrationForWait(long id, CancellationTokenRegistration ctr)
        {
            lock(syncRoot)
            {
                if (waiters.ContainsId(id))
                {
                    waiters.GetById(id).ctr = ctr;
                }
                else
                {
                    ctr.PostDispose();
                }
            }
        }

        public Task WaitForIdle(CancellationToken ctoken)
        {
            if (ctoken.IsCancellationRequested)
            {
                return Task.FromException<bool>(new OperationCanceledException(ctoken));
            }
            else
            {
                lock (syncRoot)
                {
                    if (referenceCount == 0)
                    {
                        return Task.FromResult(true);
                    }
                    else
                    {
                        TaskCompletionSource<bool> k = new TaskCompletionSource<bool>();

                        Waiter waiter = new Waiter()
                        {
                            id = null,
                            k = k,
                            ctoken = ctoken,
                            ctr = null,
                        };

                        long id = waiters.Enqueue(waiter);
                        waiter.id = id;

                        Utils.PostRegistration(ctoken, ctr => SetRegistrationForWait(id, ctr), () => CancelWait(id));

                        return k.Task;
                    }
                }
            }
        }
    }
}
