# AsyncQueues
A C# implementation of async queues, suitable for producer-consumer, etc, written for Visual Studio 2015, .NET 4.6, and C# 6.0. Requires Microsoft's [Immutable Collections](http://www.nuget.org/packages/System.Collections.Immutable).

There are already a number of implementations of async queues (also known as blocking queues), including the `BlockingQueue`
class in the .NET framework itself, and the TPL DataFlow library, but there is no consensus on what kinds of features should
be available. The `BlockingQueue` in .NET is not awaitable, for example. I've actually written several different
implementations of my own. This one should be general enough for many uses, although it is thoroughly `async`.

Some tests are included.

## Queues

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
(If the queue's capacity is at least two, it is possible for the read lock and the write lock to be held by separate threads
at the same time, because the locks don't overlap.)

If this protocol seems too complex, the `Utils` class has extension methods `Enqueue` and `Dequeue` which handle the details
for you.

A writer should eventually call the `WriteEof` function on the queue. If multiple threads are writing, then your code will
have to wait for all the threads to finish writing before calling `WriteEof`.

`Dequeue` returns a value of type `Option<T>`, which is `None<T>` in case of EOF.

## Complete Any

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

`CompleteAny` attempts to acquire locks in the order provided, and in the event of multiple simultaneous completions, prefers
whichever operation appears earliest in the list. As a result, it is possible for two calls to ``CompleteAny``
to deadlock with each other, if they acquire locks in different orders.

## Pipelines

Async queues are commonly used in producer-consumer arrangements. In order to make these arrangements easier to use, an
interface and several extension methods have been added.

The ``IQueueSource<T>`` interface represents the read end of a queue, and abstracts the functions that acquire and release
the read lock.

The function ``AsQueueSource<T>`` extends ``IEnumerable<T>`` and starts an asynchronous task that will pump values from the
enumerable to a queue. The read end of the queue is then returned as an ``IQueueSource<T>``.

The function ``AsEnumerable<T>`` extends ``IQueueSource<T>`` and carries out the reverse conversion. The returned enumerable
can block. Internally, this function uses Microsoft's ``BlockingCollection<T>`` class. ``AsEnumerable<T>`` should be used only
in non-async code. (There is no ``IAsyncEnumerable<T>`` in the .NET framework; I hope that one day one is added.)

``Select<T, U>`` extends ``IQueueSource<T>``, takes an asynchronous projection function ``Func<T, Task<U>>``, and returns a
new ``IQueueSource<U>`` that returns the projection of of the items in the original ``IQueueSource<T>``. The projection
function is run in a loop in its own task. This means that if you have ``source.Select(proc1).Select(proc2).Select(proc3)``,
you will actually run three simultaneous tasks, analogously to Unix processes connected by pipes.

``Where<T>`` extends ``IQueueSource<T>``, takes an asynchronous predicate ``Func<T, Task<bool>>``, and returns a new
``IQueueSource<T>`` that contains only the items for which the predicate is true. The predicate is run in a loop in its
own task.

One limitation of ``...Select(proc1)...`` is that it runs only one call to ``proc1`` at a time. To eliminate this limitation,
the functions ``ParallelSelect<T, U>`` and ``ParallelWhere<T>`` are provided. As an additional argument, they take an object
of type ``ParallelWorker``. A ``ParallelWorker`` must be given a capacity, and can schedule up to that many simultaneous calls
to functions. Two or more ``Parallel`` calls are allowed to share a ``ParallelWorker``, in which case they will contend
for the same capacity. It is more common to create a ``new ParallelWorker(...)`` for each ``ParallelSelect`` or
``ParallelWhere``. (For special cases, it is permitted to create a ``ParallelWorker`` with a capacity of one.)

``ParallelSelect`` and ``ParallelWhere`` may return results out of order, especially if calls to their ``proc`` functions take
varying amounts of time. If unordered results are not desirable, the functions ``OrderedParallelSelect`` and
``OrderedParallelWhere`` are provided. These functions use a reorder buffer and the output may be "bursty."

Three functions used to implement the ``OrderedParallel`` functions are public because they may be independendly useful.

``Indexed`` extends ``IQueueSource<T>`` and returns ``IQueueSource<Tuple<int, T>>``. As the items come through the ``Indexed``
function, it stamps them with indexes 0, 1, 2, and so forth. The integer part of each tuple is the index of the item. This
function is used to keep track of which item is which, when items are returned out of order.

``SynchronousSelect`` extends ``IQueueSource<T>``, takes a ``Func<T, U>``, and returns the result of applying the function
to every item in the queue source. It is similar to ``Select`` except that it requires the projection function to be
synchronous, and it does *not* run in its own task (preferring to piggyback on the next task).

Finally, ``Reorder`` extends ``IQueueSource<T>`` and provides a reorder buffer. This function can be useful if you want to do
a chain of multiple ``ParallelSelect`` calls followed by one big reordering at the end. (If you put a ``Parallel`` before an
``OrderedParallel``, it may not achieve the results you want. The ``Parallel`` will rearrange the items, and then the
``OrderedParallel`` will carefully preserve that rearrangement.)

## For Each

It is very common to want to create a task that processes each item in a queue. To make that easier, a new
``WorkerTask`` class is provided, with four ``ForEach`` functions.

* One which processes a single queue serially
* One which processes multiple queues serially
* One which processes a single queue with a ``ParallelWorker``
* One which processes multiple queues with a ``ParallelWorker``

These return a ``Func<Task>`` which can be passed to ``Task.Run``.

These require the use of an ``ExceptionCollector``. The ``ExceptionCollector`` keeps a ``CancellationTokenSource`` and cancels it
when any exception is added. The ``ForEach`` functions catch exceptions and automatically add them to the ``ExceptionCollector``
provided as an argument. They also stop iteration if the ``ExceptionCollector.CancellationToken`` becomes canceled.

All four of these functions pass a ``ForEachInfo<T>`` item to the "body" function you provide. The ``Item`` property of this object
contains the item that was retrieved. For multiple queues, the ``InputIndex`` property indicates which queue the item came from. For
parallel processing, the ``WorkerId`` property returns the index of the current worker, and is guaranteed not to be used by any other
parallel worker at the same time. Finally, the ``CancellationToken`` property contains a cancellation token which comes from the
``ExceptionCollector``.

All four of these functions also accept an ``onClose`` function, which can be ``null``. This function is always called when provided,
and is a great place to call ``WriteEof`` from.

## Tests
 
If you would like to see examples of how this code is used, please look at the tests.

**Warning:** There may still be bugs in this code.

Some of this code was written under .NET 4.5.2, so new flags introduced in .NET 4.6 such as
``TaskCreationOptions.RunContinuationsAsynchronously`` were not used. After further evaluation, the new flag will
still not be used, because it makes the decision (to run continuations asynchronously) in the wrong place. This decision
should not be made at the time the ``TaskCompletionSource`` is created, but at the time ``SetResult`` is called. Therefore,
the ``PostResult`` extension methods have been kept.

## A Personal Note

My résumé includes a reference to a Queue Service I wrote for an employer. This is **not** that service. I don't even
have a copy of the service I wrote for that employer. It was a Windows Service that had an interface over WCF. It was
written to work in dot-Net 3.5 and 4.0. Internally, it used a ``CancellableOperation`` abstraction instead of the
locking used by this library. Over WCF, though, it was not possible to cancel anything. Instead, you could specify
timeouts, and you would receive a special result if the operation timed out. So it basically did long-polling over
WCF. This library uses more recent versions of .NET and does not use WCF at all. It is a library and not a service.
