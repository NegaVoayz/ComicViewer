using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ComicViewer.Services
{
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Xml.Linq;

    public class LRUCache<TKey, TValue>
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey,TValue>>> _cache;
        private readonly LinkedList<KeyValuePair<TKey, TValue>> _accessQueue;
        private readonly object _lock = new object();

        public LRUCache(int capacity)
        {
            _capacity = capacity;
            _cache = new();
            _accessQueue = new();
        }

        public bool TryGet(TKey key, out TValue value)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                lock (_lock)
                {
                    // 记录访问
                    value = node.Value.Value;
                    _accessQueue.Remove(node);
                    _accessQueue.AddLast(node);
                }
                return true;
            }
            value = default;
            return false;
        }

        public void Put(TKey key, TValue value)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var existingNode))
                {
                    // 更新现有节点的值并记录访问
                    existingNode.Value = new KeyValuePair<TKey, TValue>(key, value);
                    _accessQueue.Remove(existingNode);
                    _accessQueue.AddLast(existingNode);
                    return;
                }
                if (_cache.Count >= _capacity)
                {
                    // 尝试移除最久未使用的
                    _cache.Remove(_accessQueue.First.Value.Key, out _);
                    _accessQueue.RemoveFirst();
                }

                var newNode = new LinkedListNode<KeyValuePair<TKey, TValue>>(new KeyValuePair<TKey, TValue>(key, value));
                _accessQueue.AddLast(newNode);
                _cache[key] = newNode;
            }
        }

        public void Remove(TKey key)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var existingNode))
                {
                    _cache.Remove(key);
                    _accessQueue.Remove(existingNode);
                    return;
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _cache.Clear();
                _accessQueue.Clear();
            }
        }
    }
}
