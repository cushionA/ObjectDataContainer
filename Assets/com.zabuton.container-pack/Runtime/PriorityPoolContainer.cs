using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODC.Runtime
{
    /// <summary>
    /// 優先度ベースのプールコンテナ。オプションの時間制限付き自動削除とコールバック機能を提供する。
    /// 満杯時は最低優先度の要素を退去させることが可能。優先度・時間制限・コールバックは全て個別にオプション。
    /// </summary>
    /// <typeparam name="T">格納するデータの型（参照型）</typeparam>
    public class PriorityPoolContainer<T> : IDisposable where T : class
    {
        /// <summary>
        /// 要素データ構造体。優先度・残り時間・挿入順序を保持する。
        /// </summary>
        private struct ElementData
        {
            public T Data;
            public float Priority;
            public float RemainingTime;   // -1は無期限を意味する
            public int HashCode;
            public int InsertionOrder;    // 同一優先度時のFIFO順序決定用
        }

        /// <summary>
        /// ハッシュテーブルのエントリ構造体。
        /// </summary>
        private struct HashEntry
        {
            public int HashCode;
            public int ValueIndex;
            public int NextInBucket;
        }

        /// <summary>バケットサイズ決定用の素数テーブル</summary>
        private static readonly int[] Primes = { 7, 17, 37, 79, 163, 331, 673, 1361, 2729, 5471, 10949, 15497 };

        private ElementData[] _elements;
        private int _activeCount;
        private int _maxCapacity;
        private int _insertionCounter;

        // ハッシュテーブル（GameObjectルックアップ用）
        private int[] _buckets;
        private HashEntry[] _hashEntries;
        private int _hashEntryCount;
        private int _bucketCount;
        private Stack<int> _freeHashEntries;

        /// <summary>現在のアクティブ要素数</summary>
        public int Count => _activeCount;

        /// <summary>最大容量</summary>
        public int MaxCapacity => _maxCapacity;

        /// <summary>コンテナが満杯かどうか</summary>
        public bool IsFull => _activeCount >= _maxCapacity;

        /// <summary>
        /// コンストラクタ。指定された最大容量でプールを初期化する。
        /// </summary>
        public PriorityPoolContainer(int maxCapacity)
        {
            _maxCapacity = maxCapacity;
            _bucketCount = GetPrimeBucketCount(maxCapacity);
            _elements = new ElementData[maxCapacity];
            _activeCount = 0;
            _insertionCounter = 0;

            _buckets = new int[_bucketCount];
            _hashEntries = new HashEntry[maxCapacity];
            _hashEntryCount = 0;
            _freeHashEntries = new Stack<int>();

            for (int i = 0; i < _bucketCount; i++)
                _buckets[i] = -1;
        }

        /// <summary>
        /// GameObjectとデータをプールに追加する。
        /// </summary>
        public int Add(GameObject obj, T data, float priority = 0f, float duration = -1f)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            return Add(obj.GetHashCode(), data, priority, duration);
        }

        /// <summary>
        /// int hashとデータをプールに追加する。
        /// </summary>
        public int Add(int hash, T data, float priority = 0f, float duration = -1f)
        {
            if (_activeCount >= _maxCapacity)
                throw new InvalidOperationException("プールが満杯です。");
            if (TryGetIndexByHash(hash, out _))
                throw new InvalidOperationException("同じハッシュが既に登録されています。");

            int dataIndex = _activeCount;

            _elements[dataIndex] = new ElementData
            {
                Data = data,
                Priority = priority,
                RemainingTime = duration,
                HashCode = hash,
                InsertionOrder = _insertionCounter++
            };
            _activeCount++;

            RegisterToHashTable(hash, dataIndex);

            return dataIndex;
        }

        /// <summary>
        /// 指定されたGameObjectの要素をBackSwap方式で削除する。
        /// </summary>
        public bool Remove(GameObject obj)
        {
            if (obj == null)
                return false;
            return Remove(obj.GetHashCode());
        }

        /// <summary>
        /// 指定されたint hashの要素をBackSwap方式で削除する。
        /// </summary>
        public bool Remove(int hash)
        {
            if (!TryGetIndexByHash(hash, out int dataIndex))
                return false;

            RemoveAtIndex(dataIndex);
            return true;
        }

        /// <summary>
        /// 追加を試みる。満杯の場合は最低優先度の要素を退去させる。
        /// </summary>
        public bool TryAddOrEvict(GameObject obj, T data, out T evicted, float priority = 0f, float duration = -1f)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            return TryAddOrEvict(obj.GetHashCode(), data, out evicted, priority, duration);
        }

        /// <summary>
        /// 追加を試みる。満杯の場合は最低優先度の要素を退去させる。
        /// </summary>
        public bool TryAddOrEvict(int hash, T data, out T evicted, float priority = 0f, float duration = -1f)
        {
            if (_activeCount < _maxCapacity)
            {
                evicted = null;
                Add(hash, data, priority, duration);
                return true;
            }

            int lowestIdx = FindLowestPriorityIndex();
            if (lowestIdx < 0)
            {
                evicted = null;
                return false;
            }

            if (priority >= _elements[lowestIdx].Priority)
            {
                evicted = _elements[lowestIdx].Data;
                RemoveAtIndex(lowestIdx);
                Add(hash, data, priority, duration);
                return true;
            }

            evicted = null;
            return false;
        }

        /// <summary>
        /// タイマーを更新し、期限切れの要素を削除する（コールバックなし）。
        /// </summary>
        public int Update(float deltaTime)
        {
            int removed = 0;
            for (int i = _activeCount - 1; i >= 0; i--)
            {
                if (_elements[i].RemainingTime < 0f)
                    continue;

                _elements[i].RemainingTime -= deltaTime;
                if (_elements[i].RemainingTime <= 0f)
                {
                    RemoveAtIndex(i);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>
        /// タイマーを更新し、期限切れの要素を削除する。期限切れ時にコールバックを呼び出す。
        /// </summary>
        public int Update(float deltaTime, Action<T> onExpired)
        {
            int removed = 0;
            for (int i = _activeCount - 1; i >= 0; i--)
            {
                if (_elements[i].RemainingTime < 0f)
                    continue;

                _elements[i].RemainingTime -= deltaTime;
                if (_elements[i].RemainingTime <= 0f)
                {
                    onExpired?.Invoke(_elements[i].Data);
                    RemoveAtIndex(i);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>
        /// 指定されたGameObjectの優先度を更新する。
        /// </summary>
        public void UpdatePriority(GameObject obj, float newPriority)
        {
            if (obj == null) return;
            UpdatePriority(obj.GetHashCode(), newPriority);
        }

        /// <summary>
        /// 指定されたint hashの優先度を更新する。
        /// </summary>
        public void UpdatePriority(int hash, float newPriority)
        {
            if (TryGetIndexByHash(hash, out int dataIndex))
            {
                var element = _elements[dataIndex];
                element.Priority = newPriority;
                _elements[dataIndex] = element;
            }
        }

        /// <summary>
        /// 指定されたGameObjectの優先度を取得する。
        /// </summary>
        public float GetPriority(GameObject obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            return GetPriority(obj.GetHashCode());
        }

        /// <summary>
        /// 指定されたint hashの優先度を取得する。
        /// </summary>
        public float GetPriority(int hash)
        {
            if (TryGetIndexByHash(hash, out int dataIndex))
                return _elements[dataIndex].Priority;

            throw new KeyNotFoundException("指定されたハッシュはプールに存在しません。");
        }

        /// <summary>
        /// 指定されたGameObjectのデータを取得する。
        /// </summary>
        public bool TryGetValue(GameObject obj, out T data)
        {
            if (obj != null)
                return TryGetValue(obj.GetHashCode(), out data);

            data = null;
            return false;
        }

        /// <summary>
        /// 指定されたint hashのデータを取得する。
        /// </summary>
        public bool TryGetValue(int hash, out T data)
        {
            if (TryGetIndexByHash(hash, out int dataIndex))
            {
                data = _elements[dataIndex].Data;
                return true;
            }

            data = null;
            return false;
        }

        /// <summary>
        /// インデックスでデータを直接取得する。
        /// </summary>
        public T GetByIndex(int index)
        {
            return _elements[index].Data;
        }

        /// <summary>
        /// 指定されたGameObjectがプールに存在するか確認する。
        /// </summary>
        public bool ContainsKey(GameObject obj)
        {
            if (obj == null)
                return false;
            return ContainsKey(obj.GetHashCode());
        }

        /// <summary>
        /// 指定されたint hashがプールに存在するか確認する。
        /// </summary>
        public bool ContainsKey(int hash)
        {
            return TryGetIndexByHash(hash, out _);
        }

        /// <summary>
        /// プールの全要素をクリアする。
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _activeCount; i++)
            {
                _elements[i] = default;
            }

            _activeCount = 0;
            _hashEntryCount = 0;
            _insertionCounter = 0;
            _freeHashEntries.Clear();

            for (int i = 0; i < _bucketCount; i++)
                _buckets[i] = -1;
        }

        /// <summary>
        /// リソースを解放する（マネージドメモリのみ）。
        /// </summary>
        public void Dispose()
        {
            Clear();
            _elements = null;
            _buckets = null;
            _hashEntries = null;
            _freeHashEntries = null;
        }

        // =============================================
        // 最低優先度検索（退去用）
        // =============================================

        private int FindLowestPriorityIndex()
        {
            if (_activeCount == 0) return -1;

            int minIdx = 0;
            float minPri = _elements[0].Priority;
            int minOrder = _elements[0].InsertionOrder;

            for (int i = 1; i < _activeCount; i++)
            {
                if (_elements[i].Priority < minPri ||
                    (_elements[i].Priority == minPri && _elements[i].InsertionOrder < minOrder))
                {
                    minIdx = i;
                    minPri = _elements[i].Priority;
                    minOrder = _elements[i].InsertionOrder;
                }
            }

            return minIdx;
        }

        // =============================================
        // BackSwap削除ロジック
        // =============================================

        private void RemoveAtIndex(int dataIndex)
        {
            int removedHash = _elements[dataIndex].HashCode;
            int lastIndex = _activeCount - 1;

            if (dataIndex != lastIndex)
            {
                int movedHash = _elements[lastIndex].HashCode;

                _elements[dataIndex] = _elements[lastIndex];

                UpdateEntryDataIndex(movedHash, dataIndex);
            }

            _elements[lastIndex] = default;
            RemoveFromHashTable(removedHash);
            _activeCount--;
        }

        // =============================================
        // ハッシュテーブル操作
        // =============================================

        private int GetBucketIndex(int hashCode)
        {
            return (hashCode & 0x7FFFFFFF) % _bucketCount;
        }

        private void RegisterToHashTable(int hashCode, int valueIndex)
        {
            int bucketIndex = GetBucketIndex(hashCode);

            int entryIndex;
            if (_freeHashEntries.Count > 0)
            {
                entryIndex = _freeHashEntries.Pop();
            }
            else
            {
                entryIndex = _hashEntryCount;
                _hashEntryCount++;
            }

            _hashEntries[entryIndex] = new HashEntry
            {
                HashCode = hashCode,
                ValueIndex = valueIndex,
                NextInBucket = _buckets[bucketIndex]
            };
            _buckets[bucketIndex] = entryIndex;
        }

        private void RemoveFromHashTable(int hashCode)
        {
            int bucketIndex = GetBucketIndex(hashCode);
            int prev = -1;
            int current = _buckets[bucketIndex];

            while (current != -1)
            {
                if (_hashEntries[current].HashCode == hashCode)
                {
                    if (prev == -1)
                        _buckets[bucketIndex] = _hashEntries[current].NextInBucket;
                    else
                    {
                        var prevEntry = _hashEntries[prev];
                        prevEntry.NextInBucket = _hashEntries[current].NextInBucket;
                        _hashEntries[prev] = prevEntry;
                    }

                    _freeHashEntries.Push(current);
                    return;
                }
                prev = current;
                current = _hashEntries[current].NextInBucket;
            }
        }

        private bool TryGetIndexByHash(int hashCode, out int valueIndex)
        {
            int bucketIndex = GetBucketIndex(hashCode);
            int current = _buckets[bucketIndex];

            while (current != -1)
            {
                if (_hashEntries[current].HashCode == hashCode)
                {
                    valueIndex = _hashEntries[current].ValueIndex;
                    return true;
                }
                current = _hashEntries[current].NextInBucket;
            }

            valueIndex = -1;
            return false;
        }

        private void UpdateEntryDataIndex(int hashCode, int newDataIndex)
        {
            int bucketIndex = GetBucketIndex(hashCode);
            int current = _buckets[bucketIndex];

            while (current != -1)
            {
                if (_hashEntries[current].HashCode == hashCode)
                {
                    var entry = _hashEntries[current];
                    entry.ValueIndex = newDataIndex;
                    _hashEntries[current] = entry;
                    return;
                }
                current = _hashEntries[current].NextInBucket;
            }
        }

        private static int GetPrimeBucketCount(int capacity)
        {
            int target = (int)(capacity * 1.5f);
            for (int i = 0; i < Primes.Length; i++)
            {
                if (Primes[i] >= target)
                    return Primes[i];
            }
            return Primes[Primes.Length - 1];
        }
    }
}
