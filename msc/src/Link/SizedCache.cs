using System;
using System.Collections.Generic;

namespace PerkeleRim
{
    // What was read once and may be asked for again (ModelSender's texture pixels: one texture is in
    // many models), up to a size: the least recently used goes first when a new one doesn't fit.
    // Unity-free; the caller says how big each value is.
    public sealed class SizedCache<TKey, TValue>
    {
        readonly long capacity;
        readonly Dictionary<TKey, LinkedListNode<Entry>> entries = new Dictionary<TKey, LinkedListNode<Entry>>();
        readonly LinkedList<Entry> order = new LinkedList<Entry>();  // most recently used first

        struct Entry
        {
            public TKey Key;
            public TValue Value;
            public long Size;
        }

        public SizedCache(long capacity)
        {
            this.capacity = capacity;
        }

        public long Size { get; private set; }
        public int Count => entries.Count;
        public int Hits { get; private set; }
        public int Misses { get; private set; }

        public bool TryGet(TKey key, out TValue value)
        {
            LinkedListNode<Entry> node;
            if (!entries.TryGetValue(key, out node))
            {
                Misses++;
                value = default(TValue);
                return false;
            }
            Hits++;
            order.Remove(node);
            order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        // Keeps value (replacing one under the same key), unless it alone is bigger than the cache.
        public void Put(TKey key, TValue value, long size)
        {
            Remove(key);
            if (size > capacity) return;
            while (Size + size > capacity && order.Last != null) Remove(order.Last.Value.Key);
            entries[key] = order.AddFirst(new Entry { Key = key, Value = value, Size = size });
            Size += size;
        }

        public void Remove(TKey key)
        {
            LinkedListNode<Entry> node;
            if (!entries.TryGetValue(key, out node)) return;
            entries.Remove(key);
            order.Remove(node);
            Size -= node.Value.Size;
        }

        public void Clear()
        {
            entries.Clear();
            order.Clear();
            Size = 0;
        }
    }
}
