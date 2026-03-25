using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODC.Runtime
{
    /// <summary>
    /// 累積スコア + 時間指数減衰 + 最大値クエリを提供するコンテナ。
    /// DamageScoreTrackerパターン等で使用。固定容量、GCフリー、遅延減衰評価。
    /// </summary>
    public class DecayingScoreContainer : IDisposable
    {
        /// <summary>
        /// スコアエントリ構造体。
        /// </summary>
        private struct ScoreEntry
        {
            public int OwnerIndex;
            public int TargetHash;
            public float RawScore;
            public float LastUpdateTime;
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

        private OwnerData[] _ownerData;
        private int _ownerCount;
        private int _maxOwners;

        private ScoreEntry[] _scores;
        private int _scoreCount;
        private int _maxScores;

        private float _decayRate;

        // ハッシュテーブル（オーナールックアップ用）
        private int[] _buckets;
        private HashEntry[] _hashEntries;
        private int _hashEntryCount;
        private int _bucketCount;
        private Stack<int> _freeHashEntries;

        /// <summary>現在のオーナー数</summary>
        public int OwnerCount => _ownerCount;

        /// <summary>現在のスコアエントリ総数</summary>
        public int ScoreCount => _scoreCount;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        /// <param name="maxOwners">最大オーナー数</param>
        /// <param name="maxEntriesPerOwner">オーナーあたりの最大エントリ数（総容量 = maxOwners * maxEntriesPerOwner）</param>
        /// <param name="decayRate">指数減衰率（1秒あたりの減衰率。例: 0.5 = 1秒で半減）</param>
        public DecayingScoreContainer(int maxOwners, int maxEntriesPerOwner, float decayRate)
        {
            _maxOwners = maxOwners;
            _maxScores = maxOwners * maxEntriesPerOwner;
            _decayRate = decayRate;
            _bucketCount = GetPrimeBucketCount(maxOwners);

            _ownerData = new OwnerData[maxOwners];
            _ownerCount = 0;

            _scores = new ScoreEntry[_maxScores];
            _scoreCount = 0;

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
            AddOwner(obj.GetInstanceID());
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
        /// オーナーを削除する。関連する全スコアも削除される。
        /// </summary>
        public bool RemoveOwner(GameObject obj)
        {
            if (obj == null) return false;
            return RemoveOwner(obj.GetInstanceID());
        }

        /// <summary>
        /// オーナーを削除する。関連する全スコアも削除される。
        /// </summary>
        public bool RemoveOwner(int hash)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return false;

            for (int i = _scoreCount - 1; i >= 0; i--)
            {
                if (_scores[i].OwnerIndex == ownerIndex)
                {
                    RemoveScoreAtIndex(i);
                }
            }

            BackSwapRemoveOwner(ownerIndex);
            return true;
        }

        // =============================================
        // スコア操作
        // =============================================

        /// <summary>
        /// スコアを加算する。既存エントリがある場合は減衰後の値に加算する。
        /// </summary>
        /// <param name="ownerHash">オーナーのハッシュ</param>
        /// <param name="targetHash">ターゲットのハッシュ</param>
        /// <param name="score">加算するスコア</param>
        /// <param name="currentTime">現在時刻（Time.time等）</param>
        public void AddScore(int ownerHash, int targetHash, float score, float currentTime)
        {
            if (!TryGetIndexByHash(ownerHash, out int ownerIndex))
                return;

            // 既存のエントリを検索
            for (int i = 0; i < _scoreCount; i++)
            {
                if (_scores[i].OwnerIndex == ownerIndex && _scores[i].TargetHash == targetHash)
                {
                    // 減衰後の値に加算
                    float decayed = GetDecayedScore(ref _scores[i], currentTime);
                    _scores[i].RawScore = decayed + score;
                    _scores[i].LastUpdateTime = currentTime;
                    return;
                }
            }

            // 新規追加
            if (_scoreCount >= _maxScores)
                return; // 満杯の場合は無視

            _scores[_scoreCount] = new ScoreEntry
            {
                OwnerIndex = ownerIndex,
                TargetHash = targetHash,
                RawScore = score,
                LastUpdateTime = currentTime
            };
            _scoreCount++;
        }

        /// <summary>
        /// 指定ターゲットの現在のスコアを取得する（減衰適用済み）。
        /// </summary>
        public float GetScore(int ownerHash, int targetHash, float currentTime)
        {
            if (!TryGetIndexByHash(ownerHash, out int ownerIndex))
                return 0f;

            for (int i = 0; i < _scoreCount; i++)
            {
                if (_scores[i].OwnerIndex == ownerIndex && _scores[i].TargetHash == targetHash)
                {
                    return GetDecayedScore(ref _scores[i], currentTime);
                }
            }
            return 0f;
        }

        /// <summary>
        /// 最高スコアのターゲットハッシュを返す。
        /// </summary>
        /// <param name="ownerHash">オーナーのハッシュ</param>
        /// <param name="currentTime">現在時刻</param>
        /// <returns>最高スコアのターゲットハッシュ。エントリがない場合は0</returns>
        public int GetHighest(int ownerHash, float currentTime)
        {
            if (!TryGetIndexByHash(ownerHash, out int ownerIndex))
                return 0;

            float bestScore = float.MinValue;
            int bestTarget = 0;

            for (int i = 0; i < _scoreCount; i++)
            {
                if (_scores[i].OwnerIndex == ownerIndex)
                {
                    float s = GetDecayedScore(ref _scores[i], currentTime);
                    if (s > bestScore)
                    {
                        bestScore = s;
                        bestTarget = _scores[i].TargetHash;
                    }
                }
            }

            return bestTarget;
        }

        /// <summary>
        /// 上位Kのターゲットハッシュとスコアを返す。
        /// </summary>
        /// <param name="ownerHash">オーナーのハッシュ</param>
        /// <param name="currentTime">現在時刻</param>
        /// <param name="results">ターゲットハッシュの出力Span</param>
        /// <param name="scores">スコアの出力Span</param>
        /// <returns>書き込まれた要素数</returns>
        public int GetTopK(int ownerHash, float currentTime, Span<int> results, Span<float> scores)
        {
            if (!TryGetIndexByHash(ownerHash, out int ownerIndex))
                return 0;

            int maxK = Math.Min(results.Length, scores.Length);
            int count = 0;

            // まずこのオーナーの全エントリを収集
            for (int i = 0; i < _scoreCount; i++)
            {
                if (_scores[i].OwnerIndex == ownerIndex)
                {
                    float s = GetDecayedScore(ref _scores[i], currentTime);
                    if (s <= 0f) continue;

                    if (count < maxK)
                    {
                        // 挿入ソート（k が小さい前提なのでO(nk)で十分）
                        int insertAt = count;
                        for (int j = 0; j < count; j++)
                        {
                            if (s > scores[j])
                            {
                                insertAt = j;
                                break;
                            }
                        }

                        // 後ろにシフト
                        for (int j = count; j > insertAt; j--)
                        {
                            if (j < maxK)
                            {
                                results[j] = results[j - 1];
                                scores[j] = scores[j - 1];
                            }
                        }

                        results[insertAt] = _scores[i].TargetHash;
                        scores[insertAt] = s;
                        count++;
                    }
                    else if (s > scores[count - 1])
                    {
                        // 最下位より大きければ挿入
                        int insertAt = count - 1;
                        for (int j = 0; j < count - 1; j++)
                        {
                            if (s > scores[j])
                            {
                                insertAt = j;
                                break;
                            }
                        }

                        for (int j = count - 1; j > insertAt; j--)
                        {
                            results[j] = results[j - 1];
                            scores[j] = scores[j - 1];
                        }

                        results[insertAt] = _scores[i].TargetHash;
                        scores[insertAt] = s;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// 特定のターゲットエントリを削除する。
        /// </summary>
        public void RemoveTarget(int ownerHash, int targetHash)
        {
            if (!TryGetIndexByHash(ownerHash, out int ownerIndex))
                return;

            for (int i = 0; i < _scoreCount; i++)
            {
                if (_scores[i].OwnerIndex == ownerIndex && _scores[i].TargetHash == targetHash)
                {
                    RemoveScoreAtIndex(i);
                    return;
                }
            }
        }

        /// <summary>
        /// コンテナの全データをクリアする。
        /// </summary>
        public void Clear()
        {
            _ownerCount = 0;
            _scoreCount = 0;
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
            _ownerData = null;
            _scores = null;
            _buckets = null;
            _hashEntries = null;
            _freeHashEntries = null;
        }

        // =============================================
        // 内部ヘルパー
        // =============================================

        private float GetDecayedScore(ref ScoreEntry entry, float currentTime)
        {
            float elapsed = currentTime - entry.LastUpdateTime;
            if (elapsed <= 0f) return entry.RawScore;
            return entry.RawScore * Mathf.Exp(-_decayRate * elapsed);
        }

        private void RemoveScoreAtIndex(int index)
        {
            int lastIndex = _scoreCount - 1;
            if (index != lastIndex)
            {
                _scores[index] = _scores[lastIndex];
            }
            _scores[lastIndex] = default;
            _scoreCount--;
        }

        private void BackSwapRemoveOwner(int ownerIndex)
        {
            int removedHash = _ownerData[ownerIndex].HashCode;
            int lastIndex = _ownerCount - 1;

            if (ownerIndex != lastIndex)
            {
                int movedHash = _ownerData[lastIndex].HashCode;

                _ownerData[ownerIndex] = _ownerData[lastIndex];

                for (int i = 0; i < _scoreCount; i++)
                {
                    if (_scores[i].OwnerIndex == lastIndex)
                    {
                        _scores[i].OwnerIndex = ownerIndex;
                    }
                }

                UpdateEntryDataIndex(movedHash, ownerIndex);
            }

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
