# AsyncQueues
A C# implementation of async queues, suitable for producer-consumer, etc, written for Visual Studio 2015, .NET 4.6, and C# 6.0. Requires Microsoft's [Immutable Collections](http://www.nuget.org/packages/System.Collections.Immutable).

There are already a number of implementations of async queues (also known as blocking queues), including the `BlockingQueue`
class in the .NET framework itself, and the TPL DataFlow library, but there is no consensus on what kinds of features should
be available. The `BlockingQueue` in .NET is not awaitable, for example. I've actually written several different
implementations of my own. This one should be general enough for many uses, although it is thoroughly `async`.

Some tests are included.

You can create queues of finite or infinite capacity.

You do reads by acquiring a lock on the read end of the queue. You can ask to lock any positive integer number of items,
and the lock will not be acquired until that number of items is available (unless EOF occurs, in which case you will get a
smaller number of items, possibly zero). The act of acquiring the lock is awaitable and works with `CancellationToken`.

Once the lock is acquired, you can inspect the items covered by the lock. Then you release the lock, indicating the number
of items you actually consumed, which can be any number from zero up to the number obtained.

Writes work exactly the same way, except that you acquire a lock on one or more *free spaces* at the end of the queue. You
cannot inspect the free spaces, but when you release the lock you can provide zero or more items to put into them.

Because reads and writes work by means of locks, it is possible for multiple threads to contend on the read end or the write
end of any queue. The lock can be held by only one thread at a time, but grants will occur in the order requested.

If this protocol seems too complex, the `Utils` class has extension methods `Enqueue` and `Dequeue` which handle the details
for you.

A writer should eventually call the `WriteEof` function on the queue. If multiple threads are writing, then you'll have to
work out a protocol so that `WriteEof` is called exactly once, *after* all the threads are done writing.

`Dequeue` returns a value of type `Option<T>`, which is `None<T>` in case of EOF.

The complex protocol was adopted because it allows operations such as "Get Any" and "Put Any." These operations work by
attempting to acquire all the locks required, but once any acquisition succeeds, all the other attempts are canceled. To
avoid a race condition between completion and cancellation, even if one of the canceled attempts completes, it can
still be canceled by releasing the lock with zero items produced or consumed. This provides for a sort of "two-phase
cancellation" which resembles two-phase commits.

There is a function `CompleteAny` in the `Utils` class. To make the syntax a little more fluid, it is implemented as an
extension method. You can create a list of (key, value) tuples. The key allows you to recognize which operation completed.
The value is an "operation starter" which can be either a Get or a Put.

`CompleteAny` can be made to work with queues of different types -- in other words, you can do a "Get Any" even if one
source is an `AsyncQueue<int>` and another is an `AsyncQueue<string>`. This requires that you convert the
result types into a common type, such as `object`.

**Warning:** Testing is not complete and there are probably still bugs in this code.

Some of this code was written under .NET 4.5.2, so new flags introduced in .NET 4.6 such as
``TaskCreationOptions.RunContinuationsAsynchronously`` are not used.
