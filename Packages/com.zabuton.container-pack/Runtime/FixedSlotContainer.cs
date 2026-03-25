using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODC.Runtime
{
    /// <summary>
    /// N個の固定スロットを管理する汎用コンテナ。
    /// エンティティごとにN個の固定スロットを持ち、アクティブスロットのカーソル・
    /// 空き検索・スワップを提供する。ヒープアロケーションなし。
    /// </summary>
    /// <typeparam name="T">スロットに格納するデータの型（unmanaged構造体）</typeparam>
    public class FixedSlotContainer<T> : IDisposable where T : unmanaged
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

        private T[] _slots;              // flat: entity[0]slots + entity[1]slots + ...
        private int[] _cursors;          // カーソル位置 per entity
        private int[] _indexToHash;
        private int[] _buckets;
        private Entry[] _entries;
        private int _activeCount;
        private int _maxEntities;
        private int _slotCount;
        private int _bucketCount;
        private int _entryCount;
        private Stack<int> _freeEntries;

        /// <summary>現在の登録エンティティ数</summary>
        public int Count => _activeCount;

        /// <summary>最大エンティティ数</summary>
        public int MaxEntities => _maxEntities;

        /// <summary>エンティティあたりのスロット数</summary>
        public int SlotCount => _slotCount;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        /// <param name="maxEntities">最大エンティティ数</param>
        /// <param name="slotCount">エンティティあたりのスロット数</param>
        public FixedSlotContainer(int maxEntities, int slotCount)
        {
            if (slotCount <= 0)
                throw new ArgumentException("スロット数は1以上である必要があります。");

            _maxEntities = maxEntities;
            _slotCount = slotCount;
            _bucketCount = GetPrimeBucketCount(maxEntities);

            _slots = new T[maxEntities * slotCount];
            _cursors = new int[maxEntities];
            _indexToHash = new int[maxEntities];
            _buckets = new int[_bucketCount];
            _entries = new Entry[maxEntities];
            _activeCount = 0;
            _entryCount = 0;
            _freeEntries = new Stack<int>();

            for (int i = 0; i < _bucketCount; i++)
                _buckets[i] = -1;
        }

        /// <summary>
        /// エンティティを登録する。全スロットはdefault値で初期化される。
        /// </summary>
        /// <param name="hash">キーとなるハッシュ値</param>
        public void Add(int hash)
        {
            if (TryGetIndexByHash(hash, out _))
                throw new InvalidOperationException("同じハッシュが既に登録されています。");
            if (_activeCount >= _maxEntities)
                throw new InvalidOperationException("エンティティ数が上限に達しています。");

            int entityIndex = _activeCount;
            _indexToHash[entityIndex] = hash;
            _cursors[entityIndex] = 0;

            // スロットをクリア
            int offset = entityIndex * _slotCount;
            for (int i = 0; i < _slotCount; i++)
                _slots[offset + i] = default;

            _activeCount++;
            RegisterToHashTable(hash, entityIndex);
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
        /// エンティティを削除する（ブロックBackSwap）。
        /// </summary>
        /// <param name="hash">削除対象のハッシュ値</param>
        /// <returns>削除に成功した場合true</returns>
        public bool Remove(int hash)
        {
            if (!TryGetIndexByHash(hash, out int entityIndex))
                return false;

            BackSwapRemove(entityIndex);
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
        /// スロットに値をセットする。O(1)。
        /// </summary>
        /// <param name="hash">対象のハッシュ値</param>
        /// <param name="slotIndex">スロット番号（0〜SlotCount-1）</param>
        /// <param name="value">セットする値</param>
        public void SetSlot(int hash, int slotIndex, in T value)
        {
            int entityIndex = GetEntityIndexOrThrow(hash);
            ValidateSlotIndex(slotIndex);
            _slots[entityIndex * _slotCount + slotIndex] = value;
        }

        /// <summary>
        /// スロットから値を取得する。O(1)。
        /// </summary>
        /// <param name="hash">対象のハッシュ値</param>
        /// <param name="slotIndex">スロット番号（0〜SlotCount-1）</param>
        /// <returns>スロットの値</returns>
        public T GetSlot(int hash, int slotIndex)
        {
            int entityIndex = GetEntityIndexOrThrow(hash);
            ValidateSlotIndex(slotIndex);
            return _slots[entityIndex * _slotCount + slotIndex];
        }

        /// <summary>
        /// スロットへのref参照を取得する。O(1)。
        /// </summary>
        public ref T GetSlotRef(int hash, int slotIndex)
        {
            int entityIndex = GetEntityIndexOrThrow(hash);
            ValidateSlotIndex(slotIndex);
            return ref _slots[entityIndex * _slotCount + slotIndex];
        }

        /// <summary>
        /// アクティブスロットを次に進める（ループ）。
        /// </summary>
        /// <param name="hash">対象のハッシュ値</param>
        public void RotateCursor(int hash)
        {
            int entityIndex = GetEntityIndexOrThrow(hash);
            _cursors[entityIndex] = (_cursors[entityIndex] + 1) % _slotCount;
        }

        /// <summary>
        /// 現在のカーソル位置を取得する。
        /// </summary>
        public int GetCursorIndex(int hash)
        {
            int entityIndex = GetEntityIndexOrThrow(hash);
            return _cursors[entityIndex];
        }

        /// <summary>
        /// カーソル位置のスロット値を取得する。
        /// </summary>
        public T GetAtCursor(int hash)
        {
            int entityIndex = GetEntityIndexOrThrow(hash);
            int slotIdx = _cursors[entityIndex];
            return _slots[entityIndex * _slotCount + slotIdx];
        }

        /// <summary>
        /// カーソル位置のスロットへのref参照を取得する。
        /// </summary>
        public ref T GetAtCursorRef(int hash)
        {
            int entityIndex = GetEntityIndexOrThrow(hash);
            int slotIdx = _cursors[entityIndex];
            return ref _slots[entityIndex * _slotCount + slotIdx];
        }

        /// <summary>
        /// 2スロット間の値をスワップ。O(1)。
        /// </summary>
        public void SwapSlots(int hash, int slotA, int slotB)
        {
            int entityIndex = GetEntityIndexOrThrow(hash);
            ValidateSlotIndex(slotA);
            ValidateSlotIndex(slotB);

            if (slotA == slotB) return;

            int offset = entityIndex * _slotCount;
            T tmp = _slots[offset + slotA];
            _slots[offset + slotA] = _slots[offset + slotB];
            _slots[offset + slotB] = tmp;
        }

        /// <summary>
        /// エンティティの全スロットをSpanで取得。
        /// </summary>
        public Span<T> GetSlots(int hash)
        {
            int entityIndex = GetEntityIndexOrThrow(hash);
            int offset = entityIndex * _slotCount;
            return _slots.AsSpan(offset, _slotCount);
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
            _slots = null;
            _cursors = null;
            _indexToHash = null;
            _buckets = null;
            _entries = null;
            _freeEntries = null;
        }

        // =============================================
        // 内部ユーティリティ
        // =============================================

        private int GetEntityIndexOrThrow(int hash)
        {
            if (!TryGetIndexByHash(hash, out int idx))
                throw new KeyNotFoundException($"ハッシュ {hash} が登録されていません。");
            return idx;
        }

        private void ValidateSlotIndex(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slotCount)
                throw new ArgumentOutOfRangeException(nameof(slotIndex),
                    $"スロットインデックス {slotIndex} は範囲外です。(0〜{_slotCount - 1})");
        }

        // =============================================
        // BackSwap削除（ブロックコピー）
        // =============================================

        private void BackSwapRemove(int entityIndex)
        {
            int removedHash = _indexToHash[entityIndex];
            int lastIndex = _activeCount - 1;

            if (entityIndex != lastIndex)
            {
                int movedHash = _indexToHash[lastIndex];

                // スロットデータのブロックコピー
                int srcOffset = lastIndex * _slotCount;
                int dstOffset = entityIndex * _slotCount;
                Array.Copy(_slots, srcOffset, _slots, dstOffset, _slotCount);

                // カーソルとハッシュをコピー
                _cursors[entityIndex] = _cursors[lastIndex];
                _indexToHash[entityIndex] = movedHash;

                UpdateEntryDataIndex(movedHash, entityIndex);
            }

            // 末尾をクリア
            int clearOffset = lastIndex * _slotCount;
            for (int i = 0; i < _slotCount; i++)
                _slots[clearOffset + i] = default;

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
                    var entry = _entries[current];
                    entry.ValueIndex = newDataIndex;
                    _entries[current] = entry;
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
