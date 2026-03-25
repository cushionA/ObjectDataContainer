using System;
using System.Collections.Generic;

namespace ODC.Runtime
{
    /// <summary>
    /// イベントインスタンス単位でのターゲット処理重複を防止するコンテナ。
    /// Dictionary を使わず、ハッシュテーブル + BackSwapで GCフリーを実現する。
    /// eventHash（攻撃・スペル等のインスタンスID）× targetHash で O(1) 判定。
    /// </summary>
    public class HitDeduplicationContainer : IDisposable
    {
        /// <summary>
        /// ヒット記録エントリ。
        /// </summary>
        private struct HitRecord
        {
            public int CombinedHash;   // eventHash と targetHash の複合キー
            public int EventHash;
            public int TargetHash;
            public int HitCount;
            public int MaxPierce;      // 0 = 無制限
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

        private HitRecord[] _records;
        private int _recordCount;
        private int _maxRecords;

        private int[] _buckets;
        private Entry[] _entries;
        private int _entryCount;
        private int _bucketCount;
        private Stack<int> _freeEntries;

        /// <summary>現在のヒット記録数</summary>
        public int Count => _recordCount;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        /// <param name="maxRecords">最大ヒット記録数</param>
        public HitDeduplicationContainer(int maxRecords)
        {
            _maxRecords = maxRecords;
            _bucketCount = GetPrimeBucketCount(maxRecords);

            _records = new HitRecord[maxRecords];
            _recordCount = 0;

            _buckets = new int[_bucketCount];
            _entries = new Entry[maxRecords];
            _entryCount = 0;
            _freeEntries = new Stack<int>();

            for (int i = 0; i < _bucketCount; i++)
                _buckets[i] = -1;
        }

        /// <summary>
        /// ターゲットへのヒットを試みる。
        /// 初回はtrue（処理を進める）、既記録はfalse（スキップ）を返す。O(1)。
        /// </summary>
        /// <param name="eventHash">イベントハッシュ（攻撃インスタンスID等）</param>
        /// <param name="targetHash">ターゲットハッシュ</param>
        /// <param name="maxPierce">最大貫通数（0=無制限、1=重複なし）</param>
        /// <returns>ヒットが有効な場合true</returns>
        public bool TryRecord(int eventHash, int targetHash, int maxPierce = 1)
        {
            int combined = CombineHash(eventHash, targetHash);

            if (TryGetIndexByHash(combined, out int recordIndex))
            {
                // 既存記録がある
                ref HitRecord record = ref _records[recordIndex];

                if (record.MaxPierce > 0 && record.HitCount >= record.MaxPierce)
                    return false; // 貫通上限到達

                record.HitCount++;
                return true;
            }

            // 新規記録
            if (_recordCount >= _maxRecords)
                return false; // 容量オーバー（安全策）

            int dataIndex = _recordCount;
            _records[dataIndex] = new HitRecord
            {
                CombinedHash = combined,
                EventHash = eventHash,
                TargetHash = targetHash,
                HitCount = 1,
                MaxPierce = maxPierce
            };
            _recordCount++;

            RegisterToHashTable(combined, dataIndex);
            return true;
        }

        /// <summary>
        /// 貫通残回数付きのヒット試行。
        /// pierceRemaining が0になったら以降はfalseを返す。
        /// </summary>
        /// <param name="eventHash">イベントハッシュ</param>
        /// <param name="targetHash">ターゲットハッシュ</param>
        /// <param name="pierceRemaining">貫通残数（0の場合は無制限貫通）</param>
        /// <returns>ヒットが有効な場合true</returns>
        public bool TryRecord(int eventHash, int targetHash, ref int pierceRemaining)
        {
            if (pierceRemaining < 0)
                return false;

            bool result = TryRecord(eventHash, targetHash, maxPierce: 0);
            if (result && pierceRemaining > 0)
            {
                pierceRemaining--;
            }
            return result;
        }

        /// <summary>
        /// イベント終了時にヒット記録を一括解放。O(k)（k=ヒット数）。
        /// </summary>
        /// <param name="eventHash">解放するイベントハッシュ</param>
        public void Release(int eventHash)
        {
            for (int i = _recordCount - 1; i >= 0; i--)
            {
                if (_records[i].EventHash == eventHash)
                {
                    BackSwapRemove(i);
                }
            }
        }

        /// <summary>
        /// 特定のevent×targetペアのヒット数を取得する。
        /// </summary>
        public int GetHitCount(int eventHash, int targetHash)
        {
            int combined = CombineHash(eventHash, targetHash);
            if (TryGetIndexByHash(combined, out int recordIndex))
                return _records[recordIndex].HitCount;
            return 0;
        }

        /// <summary>
        /// 特定イベントの合計ヒット数を取得する（全ターゲット合算）。
        /// O(N)の線形走査のため、大量レコード時にフレーム毎の呼び出しは避けること。
        /// </summary>
        public int GetHitCount(int eventHash)
        {
            int total = 0;
            for (int i = 0; i < _recordCount; i++)
            {
                if (_records[i].EventHash == eventHash)
                    total += _records[i].HitCount;
            }
            return total;
        }

        /// <summary>
        /// コンテナの全記録をクリアする。
        /// </summary>
        public void Clear()
        {
            _recordCount = 0;
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
            _records = null;
            _buckets = null;
            _entries = null;
            _freeEntries = null;
        }

        // =============================================
        // 内部ヘルパー
        // =============================================

        private static int CombineHash(int a, int b)
        {
            // 非可換ハッシュ: CombineHash(a,b) != CombineHash(b,a)
            unchecked
            {
                int hash = a * 1000003;
                hash ^= b;
                hash *= 397;
                return hash;
            }
        }

        // =============================================
        // BackSwap削除ロジック
        // =============================================

        private void BackSwapRemove(int dataIndex)
        {
            int removedCombined = _records[dataIndex].CombinedHash;
            int lastIndex = _recordCount - 1;

            if (dataIndex != lastIndex)
            {
                int movedCombined = _records[lastIndex].CombinedHash;
                _records[dataIndex] = _records[lastIndex];
                UpdateEntryDataIndex(movedCombined, dataIndex);
            }

            _records[lastIndex] = default;
            RemoveFromHashTable(removedCombined);
            _recordCount--;
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
