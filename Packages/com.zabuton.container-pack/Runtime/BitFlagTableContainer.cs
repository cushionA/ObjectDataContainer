using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODC.Runtime
{
    /// <summary>
    /// ハッシュキー × ビットフラグのテーブルコンテナ。
    /// 対象ごとに最大64フラグ（ulong）をO(1)で操作できる。
    /// GateRegistry / AbilityFlag / 訪問済みエリアなどに汎用的に使える。
    /// </summary>
    public class BitFlagTableContainer : IDisposable
    {
        /// <summary>
        /// ハッシュテーブルのエントリ構造体。
        /// </summary>
        private struct Entry
        {
            public int HashCode;
            public int ValueIndex;
            public int NextInBucket;
        }

        /// <summary>バケットサイズ決定用の素数テーブル</summary>
        private static readonly int[] Primes = { 7, 17, 37, 79, 163, 331, 673, 1361, 2729, 5471, 10949, 15497 };

        private ulong[] _flags;
        private int[] _indexToHash;
        private int[] _buckets;
        private Entry[] _entries;
        private int _activeCount;
        private int _maxCapacity;
        private int _bucketCount;
        private int _entryCount;
        private Stack<int> _freeEntries;

        /// <summary>現在の登録エンティティ数</summary>
        public int Count => _activeCount;

        /// <summary>最大容量</summary>
        public int MaxCapacity => _maxCapacity;

        /// <summary>
        /// コンストラクタ。指定された最大容量でコンテナを初期化する。
        /// </summary>
        /// <param name="maxCapacity">最大容量</param>
        public BitFlagTableContainer(int maxCapacity)
        {
            _maxCapacity = maxCapacity;
            _bucketCount = GetPrimeBucketCount(maxCapacity);
            _flags = new ulong[maxCapacity];
            _indexToHash = new int[maxCapacity];
            _buckets = new int[_bucketCount];
            _entries = new Entry[maxCapacity];
            _activeCount = 0;
            _entryCount = 0;
            _freeEntries = new Stack<int>();

            for (int i = 0; i < _bucketCount; i++)
                _buckets[i] = -1;
        }

        /// <summary>
        /// エンティティを登録する。初期フラグは0。
        /// </summary>
        /// <param name="hash">キーとなるハッシュ値</param>
        /// <exception cref="InvalidOperationException">既に登録済みまたは満杯の場合</exception>
        public void Add(int hash)
        {
            if (TryGetIndexByHash(hash, out _))
                throw new InvalidOperationException("同じハッシュが既に登録されています。");
            if (_activeCount >= _maxCapacity)
                throw new InvalidOperationException("コンテナが満杯です。");

            int dataIndex = _activeCount;
            _flags[dataIndex] = 0UL;
            _indexToHash[dataIndex] = hash;
            _activeCount++;

            RegisterToHashTable(hash, dataIndex);
        }

        /// <summary>
        /// GameObjectをキーにエンティティを登録する。
        /// </summary>
        public void Add(GameObject obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            Add(obj.GetInstanceID());
        }

        /// <summary>
        /// エンティティを削除する（BackSwap方式）。
        /// </summary>
        /// <param name="hash">削除対象のハッシュ値</param>
        /// <returns>削除に成功した場合true</returns>
        public bool Remove(int hash)
        {
            if (!TryGetIndexByHash(hash, out int dataIndex))
                return false;

            BackSwapRemove(dataIndex);
            return true;
        }

        /// <summary>
        /// GameObjectをキーにエンティティを削除する。
        /// </summary>
        public bool Remove(GameObject obj)
        {
            if (obj == null)
                return false;
            return Remove(obj.GetInstanceID());
        }

        /// <summary>
        /// エンティティが登録されているか確認する。
        /// </summary>
        public bool ContainsKey(int hash)
        {
            return TryGetIndexByHash(hash, out _);
        }

        /// <summary>
        /// GameObjectが登録されているか確認する。
        /// </summary>
        public bool ContainsKey(GameObject obj)
        {
            if (obj == null)
                return false;
            return ContainsKey(obj.GetInstanceID());
        }

        /// <summary>
        /// フラグマスクをセット（OR演算）。O(1)。
        /// </summary>
        /// <param name="hash">対象のハッシュ値</param>
        /// <param name="flagMask">セットするフラグマスク</param>
        public void SetFlag(int hash, ulong flagMask)
        {
            int idx = GetIndexOrThrow(hash);
            _flags[idx] |= flagMask;
        }

        /// <summary>
        /// GameObjectをキーにフラグマスクをセット。
        /// </summary>
        public void SetFlag(GameObject obj, ulong flagMask)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            SetFlag(obj.GetInstanceID(), flagMask);
        }

        /// <summary>
        /// フラグマスクをクリア（AND NOT演算）。O(1)。
        /// </summary>
        /// <param name="hash">対象のハッシュ値</param>
        /// <param name="flagMask">クリアするフラグマスク</param>
        public void ClearFlag(int hash, ulong flagMask)
        {
            int idx = GetIndexOrThrow(hash);
            _flags[idx] &= ~flagMask;
        }

        /// <summary>
        /// GameObjectをキーにフラグマスクをクリア。
        /// </summary>
        public void ClearFlag(GameObject obj, ulong flagMask)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            ClearFlag(obj.GetInstanceID(), flagMask);
        }

        /// <summary>
        /// フラグマスクをトグル（XOR演算）。O(1)。
        /// </summary>
        /// <param name="hash">対象のハッシュ値</param>
        /// <param name="flagMask">トグルするフラグマスク</param>
        public void ToggleFlag(int hash, ulong flagMask)
        {
            int idx = GetIndexOrThrow(hash);
            _flags[idx] ^= flagMask;
        }

        /// <summary>
        /// GameObjectをキーにフラグマスクをトグル。
        /// </summary>
        public void ToggleFlag(GameObject obj, ulong flagMask)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            ToggleFlag(obj.GetInstanceID(), flagMask);
        }

        /// <summary>
        /// 指定フラグが全てセットされているか。O(1)。
        /// </summary>
        /// <param name="hash">対象のハッシュ値</param>
        /// <param name="flagMask">確認するフラグマスク</param>
        /// <returns>全てセットされている場合true</returns>
        public bool HasAll(int hash, ulong flagMask)
        {
            int idx = GetIndexOrThrow(hash);
            return (_flags[idx] & flagMask) == flagMask;
        }

        /// <summary>
        /// GameObjectをキーに指定フラグが全てセットされているか判定。
        /// </summary>
        public bool HasAll(GameObject obj, ulong flagMask)
        {
            if (obj == null) return false;
            return HasAll(obj.GetInstanceID(), flagMask);
        }

        /// <summary>
        /// 指定フラグのいずれかがセットされているか。O(1)。
        /// </summary>
        /// <param name="hash">対象のハッシュ値</param>
        /// <param name="flagMask">確認するフラグマスク</param>
        /// <returns>いずれかがセットされている場合true</returns>
        public bool HasAny(int hash, ulong flagMask)
        {
            int idx = GetIndexOrThrow(hash);
            return (_flags[idx] & flagMask) != 0;
        }

        /// <summary>
        /// GameObjectをキーに指定フラグのいずれかがセットされているか判定。
        /// </summary>
        public bool HasAny(GameObject obj, ulong flagMask)
        {
            if (obj == null) return false;
            return HasAny(obj.GetInstanceID(), flagMask);
        }

        /// <summary>
        /// フラグを全取得。O(1)。
        /// </summary>
        /// <param name="hash">対象のハッシュ値</param>
        /// <returns>現在のフラグ値</returns>
        public ulong GetFlags(int hash)
        {
            int idx = GetIndexOrThrow(hash);
            return _flags[idx];
        }

        /// <summary>
        /// 全エンティティのフラグをOR合算して返す。O(n)。
        /// 装備由来AbilityFlag合算等に使用。
        /// </summary>
        public ulong AggregateAll()
        {
            ulong result = 0UL;
            for (int i = 0; i < _activeCount; i++)
                result |= _flags[i];
            return result;
        }

        /// <summary>
        /// 指定フラグマスクのいずれかが立っているエンティティのハッシュをSpanに書き込む。O(n)。
        /// </summary>
        /// <param name="flagMask">検索するフラグマスク</param>
        /// <param name="results">結果を書き込むSpan</param>
        /// <returns>書き込まれた件数</returns>
        public int QueryHashes(ulong flagMask, Span<int> results)
        {
            int count = 0;
            for (int i = 0; i < _activeCount && count < results.Length; i++)
            {
                if ((_flags[i] & flagMask) != 0)
                {
                    results[count] = _indexToHash[i];
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// コンテナの全要素をクリアする。
        /// </summary>
        public void Clear()
        {
            _activeCount = 0;
            _entryCount = 0;
            _freeEntries.Clear();

            for (int i = 0; i < _bucketCount; i++)
                _buckets[i] = -1;
        }

        /// <summary>
        /// リソースを解放する。
        /// </summary>
        public void Dispose()
        {
            Clear();
            _flags = null;
            _indexToHash = null;
            _buckets = null;
            _entries = null;
            _freeEntries = null;
        }

        // =============================================
        // 内部ユーティリティ
        // =============================================

        private int GetIndexOrThrow(int hash)
        {
            if (!TryGetIndexByHash(hash, out int idx))
                throw new KeyNotFoundException($"ハッシュ {hash} が登録されていません。");
            return idx;
        }

        // =============================================
        // BackSwap削除ロジック
        // =============================================

        private void BackSwapRemove(int dataIndex)
        {
            int removedHash = _indexToHash[dataIndex];
            int lastIndex = _activeCount - 1;

            if (dataIndex != lastIndex)
            {
                int movedHash = _indexToHash[lastIndex];

                _flags[dataIndex] = _flags[lastIndex];
                _indexToHash[dataIndex] = movedHash;

                UpdateEntryDataIndex(movedHash, dataIndex);
            }

            _flags[lastIndex] = 0UL;
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
            if (_freeEntries.Count > 0)
            {
                entryIndex = _freeEntries.Pop();
            }
            else
            {
                entryIndex = _entryCount;
                _entryCount++;
            }

            _entries[entryIndex] = new Entry
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
                if (_entries[current].HashCode == hashCode)
                {
                    if (prev == -1)
                        _buckets[bucketIndex] = _entries[current].NextInBucket;
                    else
                        _entries[prev].NextInBucket = _entries[current].NextInBucket;

                    _freeEntries.Push(current);
                    return;
                }
                prev = current;
                current = _entries[current].NextInBucket;
            }
        }

        private bool TryGetIndexByHash(int hashCode, out int valueIndex)
        {
            int bucketIndex = GetBucketIndex(hashCode);
            int current = _buckets[bucketIndex];

            while (current != -1)
            {
                if (_entries[current].HashCode == hashCode)
                {
                    valueIndex = _entries[current].ValueIndex;
                    return true;
                }
                current = _entries[current].NextInBucket;
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
                if (_entries[current].HashCode == hashCode)
                {
                    _entries[current].ValueIndex = newDataIndex;
                    return;
                }
                current = _entries[current].NextInBucket;
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
