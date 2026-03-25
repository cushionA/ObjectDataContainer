using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODC.Runtime
{
    /// <summary>
    /// ownerHash ごとに T 型の候補を提出・スコアリングし、
    /// 最高スコアの候補を選択するフレームバッファ。
    /// 毎フレームの評価サイクルを GCフリーでサポートする。
    /// </summary>
    /// <typeparam name="T">候補の型（unmanaged構造体）</typeparam>
    public class ScoredCandidateBuffer<T> : IDisposable where T : unmanaged
    {
        /// <summary>
        /// 候補エントリ構造体。
        /// </summary>
        private struct Candidate
        {
            public T Value;
            public float Score;
        }

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

        private Candidate[] _candidates;   // flat: owner領域 × maxCandidatesPerOwner
        private int[] _candidateCounts;    // 各オーナーの現在の候補数
        private int[] _indexToHash;
        private int _activeOwnerCount;
        private int _maxOwners;
        private int _maxCandidatesPerOwner;

        private int[] _buckets;
        private Entry[] _entries;
        private int _entryCount;
        private int _bucketCount;
        private Stack<int> _freeEntries;

        /// <summary>現在のオーナー数</summary>
        public int OwnerCount => _activeOwnerCount;

        /// <summary>オーナーあたりの最大候補数</summary>
        public int MaxCandidatesPerOwner => _maxCandidatesPerOwner;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        /// <param name="maxOwners">最大オーナー数</param>
        /// <param name="maxCandidatesPerOwner">オーナーあたりの最大候補数</param>
        public ScoredCandidateBuffer(int maxOwners, int maxCandidatesPerOwner = 16)
        {
            _maxOwners = maxOwners;
            _maxCandidatesPerOwner = maxCandidatesPerOwner;
            _bucketCount = GetPrimeBucketCount(maxOwners);

            _candidates = new Candidate[maxOwners * maxCandidatesPerOwner];
            _candidateCounts = new int[maxOwners];
            _indexToHash = new int[maxOwners];
            _activeOwnerCount = 0;

            _buckets = new int[_bucketCount];
            _entries = new Entry[maxOwners];
            _entryCount = 0;
            _freeEntries = new Stack<int>();

            for (int i = 0; i < _bucketCount; i++)
                _buckets[i] = -1;
        }

        /// <summary>
        /// オーナーを登録する。
        /// </summary>
        public void AddOwner(int hash)
        {
            if (TryGetIndexByHash(hash, out _))
                throw new InvalidOperationException("同じハッシュが既に登録されています。");
            if (_activeOwnerCount >= _maxOwners)
                throw new InvalidOperationException("オーナー数が上限に達しています。");

            int ownerIndex = _activeOwnerCount;
            _indexToHash[ownerIndex] = hash;
            _candidateCounts[ownerIndex] = 0;
            _activeOwnerCount++;

            RegisterToHashTable(hash, ownerIndex);
        }

        /// <summary>
        /// GameObjectをキーにオーナーを登録する。
        /// </summary>
        public void AddOwner(GameObject obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            AddOwner(obj.GetInstanceID());
        }

        /// <summary>
        /// オーナーを削除する。
        /// </summary>
        public bool RemoveOwner(int hash)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return false;

            BackSwapRemoveOwner(ownerIndex);
            return true;
        }

        /// <summary>
        /// 評価サイクルを開始する（候補をクリア）。
        /// </summary>
        public void BeginEvaluation(int hash)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return;
            _candidateCounts[ownerIndex] = 0;
        }

        /// <summary>
        /// GameObjectをキーに評価サイクルを開始する。
        /// </summary>
        public void BeginEvaluation(GameObject obj)
        {
            if (obj == null) return;
            BeginEvaluation(obj.GetInstanceID());
        }

        /// <summary>
        /// スコア付きで候補を提出する。O(1)。
        /// バッファが満杯の場合、最低スコアの候補を置換する。
        /// </summary>
        public void Submit(int hash, in T candidate, float score)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return;

            int offset = ownerIndex * _maxCandidatesPerOwner;
            int count = _candidateCounts[ownerIndex];

            if (count < _maxCandidatesPerOwner)
            {
                // 空きあり
                _candidates[offset + count] = new Candidate { Value = candidate, Score = score };
                _candidateCounts[ownerIndex] = count + 1;
            }
            else
            {
                // 最低スコアの候補を見つけて置換
                int minIdx = 0;
                float minScore = _candidates[offset].Score;
                for (int i = 1; i < count; i++)
                {
                    if (_candidates[offset + i].Score < minScore)
                    {
                        minScore = _candidates[offset + i].Score;
                        minIdx = i;
                    }
                }

                if (score > minScore)
                {
                    _candidates[offset + minIdx] = new Candidate { Value = candidate, Score = score };
                }
            }
        }

        /// <summary>
        /// GameObjectをキーに候補を提出する。
        /// </summary>
        public void Submit(GameObject obj, in T candidate, float score)
        {
            if (obj == null) return;
            Submit(obj.GetInstanceID(), candidate, score);
        }

        /// <summary>
        /// 最高スコアの候補を取得。
        /// </summary>
        public bool TryGetBest(int hash, out T best, out float bestScore)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex) || _candidateCounts[ownerIndex] == 0)
            {
                best = default;
                bestScore = 0f;
                return false;
            }

            int offset = ownerIndex * _maxCandidatesPerOwner;
            int count = _candidateCounts[ownerIndex];

            int bestIdx = 0;
            float maxScore = _candidates[offset].Score;
            for (int i = 1; i < count; i++)
            {
                if (_candidates[offset + i].Score > maxScore)
                {
                    maxScore = _candidates[offset + i].Score;
                    bestIdx = i;
                }
            }

            best = _candidates[offset + bestIdx].Value;
            bestScore = maxScore;
            return true;
        }

        /// <summary>
        /// GameObjectをキーに最高スコアの候補を取得する。
        /// </summary>
        public bool TryGetBest(GameObject obj, out T best, out float bestScore)
        {
            if (obj == null)
            {
                best = default;
                bestScore = 0f;
                return false;
            }
            return TryGetBest(obj.GetInstanceID(), out best, out bestScore);
        }

        /// <summary>
        /// スコア上位k件をSpanに書き込む。
        /// </summary>
        /// <param name="hash">オーナーハッシュ</param>
        /// <param name="results">結果を書き込むSpan</param>
        /// <param name="k">取得件数</param>
        /// <returns>実際に取得された件数</returns>
        public int GetTopK(int hash, Span<T> results, int k)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return 0;

            int offset = ownerIndex * _maxCandidatesPerOwner;
            int count = _candidateCounts[ownerIndex];
            int resultCount = Math.Min(k, Math.Min(count, results.Length));

            if (resultCount == 0) return 0;

            // 簡易ソート（候補数は少ないのでSelection Sort）
            Span<int> indices = stackalloc int[count];
            Span<bool> used = stackalloc bool[count];
            for (int i = 0; i < count; i++)
            {
                indices[i] = i;
                used[i] = false;
            }

            for (int picked = 0; picked < resultCount; picked++)
            {
                int bestIdx = -1;
                float bestScore = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    if (!used[i] && _candidates[offset + i].Score > bestScore)
                    {
                        bestScore = _candidates[offset + i].Score;
                        bestIdx = i;
                    }
                }

                if (bestIdx < 0) break;
                used[bestIdx] = true;
                results[picked] = _candidates[offset + bestIdx].Value;
            }

            return resultCount;
        }

        /// <summary>
        /// 指定オーナーの現在の候補数を取得する。
        /// </summary>
        public int GetCount(int hash)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return 0;
            return _candidateCounts[ownerIndex];
        }

        /// <summary>
        /// コンテナの全データをクリアする。
        /// </summary>
        public void Clear()
        {
            _activeOwnerCount = 0;
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
            _candidates = null;
            _candidateCounts = null;
            _indexToHash = null;
            _buckets = null;
            _entries = null;
            _freeEntries = null;
        }

        // =============================================
        // BackSwap削除
        // =============================================

        private void BackSwapRemoveOwner(int ownerIndex)
        {
            int removedHash = _indexToHash[ownerIndex];
            int lastIndex = _activeOwnerCount - 1;

            if (ownerIndex != lastIndex)
            {
                int movedHash = _indexToHash[lastIndex];

                // 候補データのブロックコピー
                int srcOffset = lastIndex * _maxCandidatesPerOwner;
                int dstOffset = ownerIndex * _maxCandidatesPerOwner;
                Array.Copy(_candidates, srcOffset, _candidates, dstOffset, _maxCandidatesPerOwner);

                _candidateCounts[ownerIndex] = _candidateCounts[lastIndex];
                _indexToHash[ownerIndex] = movedHash;

                UpdateEntryDataIndex(movedHash, ownerIndex);
            }

            _candidateCounts[lastIndex] = 0;
            RemoveFromHashTable(removedHash);
            _activeOwnerCount--;
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
