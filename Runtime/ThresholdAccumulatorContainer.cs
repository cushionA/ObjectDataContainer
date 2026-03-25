using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODC.Runtime
{
    /// <summary>
    /// 蓄積値が閾値に達したらトリガーを発火するコンテナ。
    /// 複数のキー（ラベル）を1オブジェクトに持てる（EXP・HP蓄積・スタミナ蓄積等）。
    /// objectHash x key で O(1) アクセス。
    /// </summary>
    public class ThresholdAccumulatorContainer : IDisposable
    {
        /// <summary>
        /// アキュムレータエントリ構造体。
        /// </summary>
        private struct AccumulatorEntry
        {
            public int OwnerIndex;
            public int KeyId;
            public float CurrentValue;
            public float Threshold;
        }

        /// <summary>
        /// オーナーデータ構造体。
        /// </summary>
        private struct OwnerData
        {
            public int HashCode;
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

        private GameObject[] _owners;
        private OwnerData[] _ownerData;
        private int _ownerCount;
        private int _maxOwners;

        private AccumulatorEntry[] _accumulators;
        private int _accumulatorCount;
        private int _maxAccumulators;

        // ハッシュテーブル（オーナールックアップ用）
        private int[] _buckets;
        private HashEntry[] _hashEntries;
        private int _hashEntryCount;
        private int _bucketCount;
        private Stack<int> _freeHashEntries;

        /// <summary>現在のオーナー数</summary>
        public int OwnerCount => _ownerCount;

        /// <summary>現在のアキュムレータ総数</summary>
        public int AccumulatorCount => _accumulatorCount;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public ThresholdAccumulatorContainer(int maxOwners, int maxAccumulatorsTotal = -1)
        {
            _maxOwners = maxOwners;
            _maxAccumulators = maxAccumulatorsTotal > 0 ? maxAccumulatorsTotal : maxOwners * 4;
            _bucketCount = GetPrimeBucketCount(maxOwners);

            _owners = new GameObject[maxOwners];
            _ownerData = new OwnerData[maxOwners];
            _ownerCount = 0;

            _accumulators = new AccumulatorEntry[_maxAccumulators];
            _accumulatorCount = 0;

            _buckets = new int[_bucketCount];
            _hashEntries = new HashEntry[maxOwners];
            _hashEntryCount = 0;
            _freeHashEntries = new Stack<int>();

            for (int i = 0; i < _bucketCount; i++)
                _buckets[i] = -1;
        }

        // =============================================
        // オーナー管理
        // =============================================

        /// <summary>
        /// GameObjectをオーナーとして追加する。
        /// </summary>
        public void AddOwner(GameObject obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            AddOwner(obj.GetHashCode());
            _owners[_ownerCount - 1] = obj;
        }

        /// <summary>
        /// int hashをオーナーとして追加する。
        /// </summary>
        public void AddOwner(int hash)
        {
            if (_ownerCount >= _maxOwners)
                throw new InvalidOperationException("オーナー数が上限に達しています。");
            if (TryGetIndexByHash(hash, out _))
                throw new InvalidOperationException("同じハッシュが既に登録されています。");

            int ownerIndex = _ownerCount;
            _ownerData[ownerIndex] = new OwnerData { HashCode = hash };
            _ownerCount++;

            RegisterToHashTable(hash, ownerIndex);
        }

        /// <summary>
        /// GameObjectをオーナーから削除する。関連する全アキュムレータも削除される。
        /// </summary>
        public bool RemoveOwner(GameObject obj)
        {
            if (obj == null)
                return false;
            return RemoveOwner(obj.GetHashCode());
        }

        /// <summary>
        /// int hashをオーナーから削除する。関連する全アキュムレータも削除される。
        /// </summary>
        public bool RemoveOwner(int hash)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return false;

            for (int i = _accumulatorCount - 1; i >= 0; i--)
            {
                if (_accumulators[i].OwnerIndex == ownerIndex)
                {
                    RemoveAccumulatorAtIndex(i);
                }
            }

            BackSwapRemoveOwner(ownerIndex);
            return true;
        }

        /// <summary>
        /// 指定されたGameObjectがオーナーとして登録されているか確認する。
        /// </summary>
        public bool ContainsOwner(GameObject obj)
        {
            if (obj == null)
                return false;
            return ContainsOwner(obj.GetHashCode());
        }

        /// <summary>
        /// 指定されたint hashがオーナーとして登録されているか確認する。
        /// </summary>
        public bool ContainsOwner(int hash)
        {
            return TryGetIndexByHash(hash, out _);
        }

        // =============================================
        // アキュムレータ操作
        // =============================================

        /// <summary>
        /// アキュムレータを登録する（GameObject版）。
        /// </summary>
        public void Register(GameObject obj, int key, float threshold)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            Register(obj.GetHashCode(), key, threshold);
        }

        /// <summary>
        /// アキュムレータを登録する（int hash版）。
        /// </summary>
        public void Register(int hash, int key, float threshold)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                throw new InvalidOperationException("オーナーが登録されていません。");

            for (int i = 0; i < _accumulatorCount; i++)
            {
                if (_accumulators[i].OwnerIndex == ownerIndex && _accumulators[i].KeyId == key)
                {
                    _accumulators[i].Threshold = threshold;
                    return;
                }
            }

            if (_accumulatorCount >= _maxAccumulators)
                throw new InvalidOperationException("アキュムレータの総数が上限に達しています。");

            _accumulators[_accumulatorCount] = new AccumulatorEntry
            {
                OwnerIndex = ownerIndex,
                KeyId = key,
                CurrentValue = 0f,
                Threshold = threshold
            };
            _accumulatorCount++;
        }

        /// <summary>
        /// 蓄積値を加算する（GameObject版）。
        /// </summary>
        public bool Add(GameObject obj, int key, float amount, bool carryOverflow = false)
        {
            if (obj == null) return false;
            return Add(obj.GetHashCode(), key, amount, carryOverflow);
        }

        /// <summary>
        /// 蓄積値を加算する（int hash版）。閾値超過時はtrueを返す。
        /// </summary>
        public bool Add(int hash, int key, float amount, bool carryOverflow = false)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return false;

            for (int i = 0; i < _accumulatorCount; i++)
            {
                if (_accumulators[i].OwnerIndex == ownerIndex && _accumulators[i].KeyId == key)
                {
                    _accumulators[i].CurrentValue += amount;

                    if (_accumulators[i].Threshold > 0f && _accumulators[i].CurrentValue >= _accumulators[i].Threshold)
                    {
                        if (carryOverflow)
                        {
                            _accumulators[i].CurrentValue -= _accumulators[i].Threshold;
                        }
                        else
                        {
                            _accumulators[i].CurrentValue = 0f;
                        }
                        return true;
                    }
                    return false;
                }
            }
            return false;
        }

        /// <summary>
        /// 現在の蓄積値を取得（GameObject版）。
        /// </summary>
        public float Get(GameObject obj, int key)
        {
            if (obj == null) return 0f;
            return Get(obj.GetHashCode(), key);
        }

        /// <summary>
        /// 現在の蓄積値を取得（int hash版）。
        /// </summary>
        public float Get(int hash, int key)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return 0f;

            for (int i = 0; i < _accumulatorCount; i++)
            {
                if (_accumulators[i].OwnerIndex == ownerIndex && _accumulators[i].KeyId == key)
                    return _accumulators[i].CurrentValue;
            }
            return 0f;
        }

        /// <summary>
        /// 進行率 (0.0〜1.0) を取得（GameObject版）。
        /// </summary>
        public float GetNormalized(GameObject obj, int key)
        {
            if (obj == null) return 0f;
            return GetNormalized(obj.GetHashCode(), key);
        }

        /// <summary>
        /// 進行率 (0.0〜1.0) を取得（int hash版）。
        /// </summary>
        public float GetNormalized(int hash, int key)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return 0f;

            for (int i = 0; i < _accumulatorCount; i++)
            {
                if (_accumulators[i].OwnerIndex == ownerIndex && _accumulators[i].KeyId == key)
                {
                    if (_accumulators[i].Threshold <= 0f) return 0f;
                    float ratio = _accumulators[i].CurrentValue / _accumulators[i].Threshold;
                    return ratio > 1f ? 1f : ratio;
                }
            }
            return 0f;
        }

        /// <summary>
        /// 閾値を変更する（GameObject版）。
        /// </summary>
        public void SetThreshold(GameObject obj, int key, float threshold)
        {
            if (obj == null) return;
            SetThreshold(obj.GetHashCode(), key, threshold);
        }

        /// <summary>
        /// 閾値を変更する（int hash版）。
        /// </summary>
        public void SetThreshold(int hash, int key, float threshold)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return;

            for (int i = 0; i < _accumulatorCount; i++)
            {
                if (_accumulators[i].OwnerIndex == ownerIndex && _accumulators[i].KeyId == key)
                {
                    _accumulators[i].Threshold = threshold;
                    return;
                }
            }
        }

        /// <summary>
        /// 蓄積値をリセット（GameObject版）。
        /// </summary>
        public void Reset(GameObject obj, int key)
        {
            if (obj == null) return;
            Reset(obj.GetHashCode(), key);
        }

        /// <summary>
        /// 蓄積値をリセット（int hash版）。
        /// </summary>
        public void Reset(int hash, int key)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return;

            for (int i = 0; i < _accumulatorCount; i++)
            {
                if (_accumulators[i].OwnerIndex == ownerIndex && _accumulators[i].KeyId == key)
                {
                    _accumulators[i].CurrentValue = 0f;
                    return;
                }
            }
        }

        /// <summary>
        /// コンテナの全データをクリアする。
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _ownerCount; i++)
                _owners[i] = null;

            _ownerCount = 0;
            _accumulatorCount = 0;
            _hashEntryCount = 0;
            _freeHashEntries.Clear();

            for (int i = 0; i < _bucketCount; i++)
                _buckets[i] = -1;
        }

        /// <summary>
        /// リソースを解放する。
        /// </summary>
        public void Dispose()
        {
            Clear();
            _owners = null;
            _ownerData = null;
            _accumulators = null;
            _buckets = null;
            _hashEntries = null;
            _freeHashEntries = null;
        }

        // =============================================
        // 内部ヘルパー
        // =============================================

        private void RemoveAccumulatorAtIndex(int index)
        {
            int lastIndex = _accumulatorCount - 1;
            if (index != lastIndex)
            {
                _accumulators[index] = _accumulators[lastIndex];
            }
            _accumulators[lastIndex] = default;
            _accumulatorCount--;
        }

        private void BackSwapRemoveOwner(int ownerIndex)
        {
            int removedHash = _ownerData[ownerIndex].HashCode;
            int lastIndex = _ownerCount - 1;

            if (ownerIndex != lastIndex)
            {
                int movedHash = _ownerData[lastIndex].HashCode;

                _owners[ownerIndex] = _owners[lastIndex];
                _ownerData[ownerIndex] = _ownerData[lastIndex];

                for (int i = 0; i < _accumulatorCount; i++)
                {
                    if (_accumulators[i].OwnerIndex == lastIndex)
                    {
                        _accumulators[i].OwnerIndex = ownerIndex;
                    }
                }

                UpdateEntryDataIndex(movedHash, ownerIndex);
            }

            _owners[lastIndex] = null;
            _ownerData[lastIndex] = default;
            RemoveFromHashTable(removedHash);
            _ownerCount--;
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
                        _hashEntries[prev].NextInBucket = _hashEntries[current].NextInBucket;

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
                    _hashEntries[current].ValueIndex = newDataIndex;
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
