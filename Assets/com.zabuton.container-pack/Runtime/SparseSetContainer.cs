using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODC.Runtime
{
    /// <summary>
    /// 疎集合コンテナ。全キャラのサブセット（アクティブな部分集合）を
    /// アロケーションなし・O(1)操作・連続メモリで管理する。
    /// sparse(ハッシュテーブル) → dense配列のマッピングにより、
    /// アクティブ要素のみを連続メモリで保持し、フルスキャンなしで一括走査できる。
    /// </summary>
    /// <typeparam name="T">格納するデータの型（unmanaged構造体）</typeparam>
    public class SparseSetContainer<T> : IDisposable where T : unmanaged
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

        private T[] _values;
        private int[] _denseToHash;
        private int[] _buckets;
        private Entry[] _entries;
        private int _activeCount;
        private int _maxCapacity;
        private int _bucketCount;
        private int _entryCount;
        private Stack<int> _freeEntries;

        /// <summary>現在のアクティブ要素数</summary>
        public int Count => _activeCount;

        /// <summary>最大容量</summary>
        public int MaxCapacity => _maxCapacity;

        /// <summary>全アクティブ要素を連続メモリで取得。GCアロケーションなし。</summary>
        public Span<T> ActiveValues => _values.AsSpan(0, _activeCount);

        /// <summary>全アクティブ要素に対応するハッシュコードを取得。</summary>
        public ReadOnlySpan<int> ActiveHashes => new ReadOnlySpan<int>(_denseToHash, 0, _activeCount);

        /// <summary>
        /// コンストラクタ。指定された最大容量でコンテナを初期化する。
        /// </summary>
        /// <param name="maxCapacity">最大容量</param>
        public SparseSetContainer(int maxCapacity)
        {
            _maxCapacity = maxCapacity;
            _bucketCount = GetPrimeBucketCount(maxCapacity);
            _values = new T[maxCapacity];
            _denseToHash = new int[maxCapacity];
            _buckets = new int[_bucketCount];
            _entries = new Entry[maxCapacity];
            _activeCount = 0;
            _entryCount = 0;
            _freeEntries = new Stack<int>();

            for (int i = 0; i < _bucketCount; i++)
                _buckets[i] = -1;
        }

        /// <summary>
        /// ハッシュをキーに要素を登録/更新する。O(1)。
        /// 既に存在する場合は値を上書きする。
        /// </summary>
        /// <param name="hash">キーとなるハッシュ値</param>
        /// <param name="value">格納する値</param>
        public void Set(int hash, in T value)
        {
            if (TryGetIndexByHash(hash, out int existingIndex))
            {
                _values[existingIndex] = value;
                return;
            }

            if (_activeCount >= _maxCapacity)
                throw new InvalidOperationException("コンテナが満杯です。");

            int dataIndex = _activeCount;
            _values[dataIndex] = value;
            _denseToHash[dataIndex] = hash;
            _activeCount++;

            RegisterToHashTable(hash, dataIndex);
        }

        /// <summary>
        /// GameObjectをキーに要素を登録/更新する。O(1)。
        /// </summary>
        /// <param name="obj">キーとなるGameObject</param>
        /// <param name="value">格納する値</param>
        public void Set(GameObject obj, in T value)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            Set(obj.GetInstanceID(), value);
        }

        /// <summary>
        /// 要素を削除する（スワップバック）。O(1)。
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
        /// GameObjectをキーに要素を削除する。O(1)。
        /// </summary>
        /// <param name="obj">削除対象のGameObject</param>
        /// <returns>削除に成功した場合true</returns>
        public bool Remove(GameObject obj)
        {
            if (obj == null)
                return false;
            return Remove(obj.GetInstanceID());
        }

        /// <summary>
        /// 登録されているか確認。O(1)。
        /// </summary>
        /// <param name="hash">検索するハッシュ値</param>
        /// <returns>登録されている場合true</returns>
        public bool Contains(int hash)
        {
            return TryGetIndexByHash(hash, out _);
        }

        /// <summary>
        /// GameObjectが登録されているか確認。O(1)。
        /// </summary>
        /// <param name="obj">検索するGameObject</param>
        /// <returns>登録されている場合true</returns>
        public bool Contains(GameObject obj)
        {
            if (obj == null)
                return false;
            return Contains(obj.GetInstanceID());
        }

        /// <summary>
        /// 値を取得。O(1)。
        /// </summary>
        /// <param name="hash">検索するハッシュ値</param>
        /// <param name="value">取得された値</param>
        /// <returns>見つかった場合true</returns>
        public bool TryGet(int hash, out T value)
        {
            if (TryGetIndexByHash(hash, out int dataIndex))
            {
                value = _values[dataIndex];
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// GameObjectをキーに値を取得。O(1)。
        /// </summary>
        public bool TryGet(GameObject obj, out T value)
        {
            if (obj == null)
            {
                value = default;
                return false;
            }
            return TryGet(obj.GetInstanceID(), out value);
        }

        /// <summary>
        /// ref返しで直接書き換え。O(1)。
        /// </summary>
        /// <param name="hash">検索するハッシュ値</param>
        /// <returns>値への参照</returns>
        /// <exception cref="KeyNotFoundException">ハッシュが見つからない場合</exception>
        public ref T GetRef(int hash)
        {
            if (!TryGetIndexByHash(hash, out int dataIndex))
                throw new KeyNotFoundException($"ハッシュ {hash} が見つかりません。");
            return ref _values[dataIndex];
        }

        /// <summary>
        /// GameObjectをキーにref返しで直接書き換え。O(1)。
        /// </summary>
        public ref T GetRef(GameObject obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            return ref GetRef(obj.GetInstanceID());
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
            _values = null;
            _denseToHash = null;
            _buckets = null;
            _entries = null;
            _freeEntries = null;
        }

        // =============================================
        // BackSwap削除ロジック
        // =============================================

        private void BackSwapRemove(int dataIndex)
        {
            int removedHash = _denseToHash[dataIndex];
            int lastIndex = _activeCount - 1;

            if (dataIndex != lastIndex)
            {
                int movedHash = _denseToHash[lastIndex];

                _values[dataIndex] = _values[lastIndex];
                _denseToHash[dataIndex] = movedHash;

                UpdateEntryDataIndex(movedHash, dataIndex);
            }

            _values[lastIndex] = default;
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
