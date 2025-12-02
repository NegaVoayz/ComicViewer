using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ComicViewer.Services
{
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    public class LRUCache<TKey, TValue>
    {
        private readonly int _capacity;
        private readonly ConcurrentDictionary<TKey, TValue> _cache;
        private readonly ConcurrentQueue<TKey> _accessQueue;
        private readonly object _lock = new object();

        public LRUCache(int capacity)
        {
            _capacity = capacity;
            _cache = new ConcurrentDictionary<TKey, TValue>();
            _accessQueue = new ConcurrentQueue<TKey>();
        }

        public bool TryGet(TKey key, out TValue value)
        {
            if (_cache.TryGetValue(key, out value))
            {
                // 记录访问
                RecordAccess(key);
                return true;
            }
            return false;
        }

        public void Put(TKey key, TValue value)
        {
            lock (_lock)
            {
                if (_cache.Count >= _capacity)
                {
                    // 尝试移除最久未使用的
                    if (_accessQueue.TryDequeue(out var oldestKey))
                    {
                        _cache.TryRemove(oldestKey, out _);
                    }
                }

                _cache[key] = value;
                RecordAccess(key);
            }
        }

        public void Clear()
        {
            _cache.Clear();
        }

        private void RecordAccess(TKey key)
        {
            // 简单的访问记录，实际使用可能需要更复杂的逻辑
            _accessQueue.Enqueue(key);
        }
    }
}
