using Sunlighter.OptionLib;
using System;
using System.Collections.Immutable;

namespace Sunlighter.AsyncQueueLib
{
    public class CancellableQueue<T>
    {
        private long nextId;
        private ImmutableDictionary<long, T> itemMap;
        private ImmutableList<long> queue;

        public CancellableQueue()
        {
            this.nextId = 0L;
            this.itemMap = ImmutableDictionary<long, T>.Empty;
            this.queue = ImmutableList<long>.Empty;
        }

        public long Enqueue(T item)
        {
            long id;
            do
            {
                id = nextId;
                ++nextId;
            }
            while (itemMap.ContainsKey(id));

            queue = queue.Add(id);
            itemMap = itemMap.Add(id, item);
            return id;
        }

        public int Count { get { return itemMap.Count; } }

        public T Peek()
        {
            if (itemMap.Count == 0)
            {
                throw new InvalidOperationException("Can't Peek from an empty queue");
            }

            while (!itemMap.ContainsKey(queue[0]))
            {
                queue = queue.RemoveAt(0);
            }

            return itemMap[queue[0]];
        }

        public T Dequeue()
        {
            if (itemMap.Count == 0)
            {
                throw new InvalidOperationException("Can't Dequeue from an empty queue");
            }

            long id;
            do
            {
                id = queue[0];
                queue = queue.RemoveAt(0);
            }
            while (!(itemMap.ContainsKey(id)));

            T item = itemMap[id];
            itemMap = itemMap.Remove(id);
            return item;
        }

        public Option<T> Cancel(long id)
        {
            if (itemMap.ContainsKey(id))
            {
                T value = itemMap[id];
                itemMap = itemMap.Remove(id);
                return Option<T>.Some(value);
            }
            else
            {
                return Option<T>.None;
            }
        }

        public bool ContainsId(long id)
        {
            return itemMap.ContainsKey(id);
        }

        public T GetById(long id)
        {
            return itemMap[id];
        }
    }
}
