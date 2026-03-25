using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODC.Runtime
{
    /// <summary>
    /// GetComponentの結果をGameObjectをキーにキャッシュするコンテナ。
    /// 繰り返しのGetComponent呼び出しを回避し、一括アクセスと破棄済みGameObjectの自動クリーンアップを提供する。
    /// </summary>
    /// <typeparam name="TComponent">キャッシュするコンポーネントの型</typeparam>
    public class ComponentCache<TComponent> : IDisposable where TComponent : Component
    {
        private struct CacheEntry
        {
            public GameObject GameObject;
            public TComponent Component;
            public int HashCode;
        }

        private CacheEntry[] _entries;
        private int _activeCount;
        private int _maxCapacity;

        // ハッシュテーブル（O(1)ルックアップ用）
        private int[] _buckets;

        private struct HashTableEntry
        {
            public int HashCode;
            public int ValueIndex;
            public int NextInBucket;
        }

        private HashTableEntry[] _hashEntries;
        private int _hashEntryCount;
        private int _bucketCount;
        private Stack<int> _freeHashEntries;

        /// <summary>
        /// 現在のキャッシュ済みエントリ数
        /// </summary>
        public int Count => _activeCount;

        /// <summary>
        /// ComponentCacheを初期化する。
        /// </summary>
        /// <param name="maxCapacity">最大キャッシュ容量</param>
        public ComponentCache(int maxCapacity)
        {
            _maxCapacity = maxCapacity;
            _entries = new CacheEntry[maxCapacity];
            _activeCount = 0;

            _bucketCount = NextPowerOfTwo(maxCapacity);
            _buckets = new int[_bucketCount];
            Array.Fill(_buckets, -1);

            _hashEntries = new HashTableEntry[maxCapacity];
            _hashEntryCount = 0;
            _freeHashEntries = new Stack<int>();
        }

        /// <summary>
        /// GameObjectのコンポーネントを取得する。キャッシュになければGetComponentして格納。
        /// </summary>
        /// <param name="obj">対象のGameObject</param>
        /// <returns>キャッシュまたは取得されたコンポーネント。見つからなければnull。</returns>
        public TComponent GetOrCache(GameObject obj)
        {
            if (obj == null) return null;

            int hash = obj.GetHashCode();
            if (TryGetIndexByHash(hash, out int index))
            {
                return _entries[index].Component;
            }

            var component = obj.GetComponent<TComponent>();
            if (component != null && _activeCount < _maxCapacity)
            {
                AddInternal(obj, component, hash);
            }

            return component;
        }

        /// <summary>
        /// キャッシュからGameObjectのコンポーネントを取得する（キャッシュミスはnull返却、GetComponentしない）。
        /// </summary>
        /// <param name="obj">対象のGameObject</param>
        /// <returns>キャッシュされたコンポーネント。キャッシュミスの場合はnull。</returns>
        public TComponent GetCached(GameObject obj)
        {
            if (obj == null) return null;

            int hash = obj.GetHashCode();
            if (TryGetIndexByHash(hash, out int index))
            {
                return _entries[index].Component;
            }

            return null;
        }

        /// <summary>
        /// 手動でキャッシュに追加する。
        /// </summary>
        /// <param name="obj">対象のGameObject</param>
        /// <param name="component">キャッシュするコンポーネント</param>
        public void Add(GameObject obj, TComponent component)
        {
            if (obj == null || component == null) return;
            if (_activeCount >= _maxCapacity) return;

            int hash = obj.GetHashCode();
            if (TryGetIndexByHash(hash, out _)) return; // 既にキャッシュ済み

            AddInternal(obj, component, hash);
        }

        /// <summary>
        /// キャッシュから削除（BackSwap）。
        /// </summary>
        /// <param name="obj">削除するGameObject</param>
        /// <returns>削除できた場合true</returns>
        public bool Remove(GameObject obj)
        {
            if (obj == null) return false;

            int hash = obj.GetHashCode();
            if (!TryGetIndexByHash(hash, out int index))
            {
                return false;
            }

            RemoveAtIndex(index);
            return true;
        }

        /// <summary>
        /// 破棄されたGameObjectのキャッシュを一括削除。
        /// 毎フレーム呼ぶ必要はなく、定期的に呼べばOK。
        /// </summary>
        /// <returns>削除されたエントリ数</returns>
        public int CleanupDestroyed()
        {
            int removed = 0;
            for (int i = _activeCount - 1; i >= 0; i--)
            {
                // Unity: 破棄されたオブジェクトはnullと比較される
                if (_entries[i].GameObject == null)
                {
                    RemoveAtIndex(i);
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>
        /// キャッシュ済みか確認する。
        /// </summary>
        /// <param name="obj">確認するGameObject</param>
        /// <returns>キャッシュに存在する場合true</returns>
        public bool ContainsKey(GameObject obj)
        {
            if (obj == null) return false;

            int hash = obj.GetHashCode();
            return TryGetIndexByHash(hash, out _);
        }

        /// <summary>
        /// キャッシュをすべてクリアする。
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _activeCount; i++)
            {
                _entries[i] = default;
            }

            _activeCount = 0;
            Array.Fill(_buckets, -1);
            _hashEntryCount = 0;
            _freeHashEntries.Clear();
        }

        /// <summary>
        /// リソースを解放する。
        /// </summary>
        public void Dispose()
        {
            Clear();
            _entries = null;
            _buckets = null;
            _hashEntries = null;
            _freeHashEntries = null;
        }

        // ================================================================
        // 内部メソッド
        // ================================================================

        private void AddInternal(GameObject obj, TComponent component, int hash)
        {
            int entryIndex = _activeCount;
            _entries[entryIndex] = new CacheEntry
            {
                GameObject = obj,
                Component = component,
                HashCode = hash
            };
            _activeCount++;

            AddToHashTable(hash, entryIndex);
        }

        private void RemoveAtIndex(int index)
        {
            int lastIndex = _activeCount - 1;
            int removedHash = _entries[index].HashCode;

            // ハッシュテーブルから削除対象を除去
            RemoveFromHashTable(removedHash, index);

            if (index < lastIndex)
            {
                // BackSwap: 最後の要素を削除位置に移動
                int lastHash = _entries[lastIndex].HashCode;

                // ハッシュテーブルの最後の要素のValueIndexを更新
                UpdateHashTableValueIndex(lastHash, lastIndex, index);

                _entries[index] = _entries[lastIndex];
            }

            _entries[lastIndex] = default;
            _activeCount--;
        }

        private void AddToHashTable(int hash, int valueIndex)
        {
            int bucketIndex = GetBucketIndex(hash);

            int hashEntryIndex;
            if (_freeHashEntries.Count > 0)
            {
                hashEntryIndex = _freeHashEntries.Pop();
            }
            else
            {
                hashEntryIndex = _hashEntryCount;
                _hashEntryCount++;
            }

            _hashEntries[hashEntryIndex] = new HashTableEntry
            {
                HashCode = hash,
                ValueIndex = valueIndex,
                NextInBucket = _buckets[bucketIndex]
            };

            _buckets[bucketIndex] = hashEntryIndex;
        }

        private void RemoveFromHashTable(int hash, int valueIndex)
        {
            int bucketIndex = GetBucketIndex(hash);
            int current = _buckets[bucketIndex];
            int prev = -1;

            while (current != -1)
            {
                if (_hashEntries[current].HashCode == hash &&
                    _hashEntries[current].ValueIndex == valueIndex)
                {
                    if (prev == -1)
                    {
                        _buckets[bucketIndex] = _hashEntries[current].NextInBucket;
                    }
                    else
                    {
                        _hashEntries[prev].NextInBucket = _hashEntries[current].NextInBucket;
                    }

                    _freeHashEntries.Push(current);
                    return;
                }

                prev = current;
                current = _hashEntries[current].NextInBucket;
            }
        }

        private void UpdateHashTableValueIndex(int hash, int oldValueIndex, int newValueIndex)
        {
            int bucketIndex = GetBucketIndex(hash);
            int current = _buckets[bucketIndex];

            while (current != -1)
            {
                if (_hashEntries[current].HashCode == hash &&
                    _hashEntries[current].ValueIndex == oldValueIndex)
                {
                    _hashEntries[current].ValueIndex = newValueIndex;
                    return;
                }

                current = _hashEntries[current].NextInBucket;
            }
        }

        private bool TryGetIndexByHash(int hash, out int valueIndex)
        {
            int bucketIndex = GetBucketIndex(hash);
            int current = _buckets[bucketIndex];

            while (current != -1)
            {
                if (_hashEntries[current].HashCode == hash)
                {
                    valueIndex = _hashEntries[current].ValueIndex;
                    return true;
                }

                current = _hashEntries[current].NextInBucket;
            }

            valueIndex = -1;
            return false;
        }

        private int GetBucketIndex(int hash)
        {
            return (hash & 0x7FFFFFFF) % _bucketCount;
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
            {
                result <<= 1;
            }

            return result;
        }
    }
}
