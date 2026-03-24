using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODC.Runtime
{
    /// <summary>
    /// 固定長のジェネリックコンテナ。GameObjectをキーとし、指定された持続時間後にBackSwapで自動削除される。
    /// </summary>
    /// <typeparam name="T">格納するデータの型（参照型）</typeparam>
    public class TimedDataContainer<T> : IDisposable where T : class
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

        private T[] _data;
        private float[] _remainingTime;
        private int[] _indexToHash;
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

        /// <summary>アクティブなデータのSpan</summary>
        public Span<T> ActiveDataSpan => _data.AsSpan(0, _activeCount);

        /// <summary>アクティブなタイマーのReadOnlySpan</summary>
        public ReadOnlySpan<float> ActiveTimerSpan => new ReadOnlySpan<float>(_remainingTime, 0, _activeCount);

        /// <summary>サブクラス用: アクティブ要素数へのアクセス</summary>
        protected int ActiveCount => _activeCount;

        /// <summary>
        /// コンストラクタ。指定された最大容量でコンテナを初期化する。
        /// </summary>
        /// <param name="maxCapacity">最大容量</param>
        public TimedDataContainer(int maxCapacity)
        {
            _maxCapacity = maxCapacity;
            _bucketCount = GetPrimeBucketCount(maxCapacity);
            _data = new T[maxCapacity];
            _remainingTime = new float[maxCapacity];
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
        /// GameObjectとデータをコンテナに追加する。
        /// </summary>
        /// <param name="obj">キーとなるGameObject</param>
        /// <param name="data">格納するデータ</param>
        /// <param name="duration">持続時間（秒）</param>
        /// <returns>データが格納されたインデックス</returns>
        /// <exception cref="ArgumentNullException">objがnullの場合</exception>
        /// <exception cref="InvalidOperationException">コンテナが満杯の場合</exception>
        public int Add(GameObject obj, T data, float duration)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            if (_activeCount >= _maxCapacity)
                throw new InvalidOperationException("コンテナが満杯です。");

            int hashCode = obj.GetInstanceID();
            if (TryGetIndexByHash(hashCode, out _))
                throw new InvalidOperationException("同じGameObjectが既に登録されています。");

            int dataIndex = _activeCount;

            _data[dataIndex] = data;
            _remainingTime[dataIndex] = duration;
            _indexToHash[dataIndex] = hashCode;
            _activeCount++;

            RegisterToHashTable(hashCode, dataIndex);

            return dataIndex;
        }

        /// <summary>
        /// 指定されたGameObjectの要素をBackSwap方式で削除する。
        /// </summary>
        /// <param name="obj">削除対象のGameObject</param>
        /// <returns>削除に成功した場合true</returns>
        public bool Remove(GameObject obj)
        {
            if (obj == null)
                return false;

            int hashCode = obj.GetInstanceID();
            if (!TryGetIndexByHash(hashCode, out int dataIndex))
                return false;

            BackSwapRemove(dataIndex);
            return true;
        }

        /// <summary>
        /// 全タイマーを更新し、期限切れの要素をBackSwapで削除する。
        /// </summary>
        /// <param name="deltaTime">経過時間（秒）</param>
        /// <returns>削除された要素数</returns>
        public virtual int Update(float deltaTime)
        {
            int removed = 0;
            for (int i = _activeCount - 1; i >= 0; i--)
            {
                _remainingTime[i] -= deltaTime;
                if (_remainingTime[i] <= 0f)
                {
                    OnElementExpired(_data[i]);
                    BackSwapRemove(i);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>
        /// 要素の期限切れ時に呼ばれる仮想メソッド。サブクラスでオーバーライド可能。
        /// </summary>
        /// <param name="data">期限切れになったデータ</param>
        protected virtual void OnElementExpired(T data)
        {
            // 基底クラスでは何もしない
        }

        /// <summary>
        /// 指定されたGameObjectのデータと残り時間を取得する。
        /// </summary>
        /// <param name="obj">検索対象のGameObject</param>
        /// <param name="data">取得されたデータ</param>
        /// <param name="remainingTime">残り時間</param>
        /// <returns>見つかった場合true</returns>
        public bool TryGetValue(GameObject obj, out T data, out float remainingTime)
        {
            if (obj != null)
            {
                int hashCode = obj.GetInstanceID();
                if (TryGetIndexByHash(hashCode, out int dataIndex))
                {
                    data = _data[dataIndex];
                    remainingTime = _remainingTime[dataIndex];
                    return true;
                }
            }

            data = null;
            remainingTime = 0f;
            return false;
        }

        /// <summary>
        /// インデックスでデータを直接取得する。
        /// </summary>
        /// <param name="index">データインデックス</param>
        /// <returns>指定インデックスのデータ</returns>
        public T GetByIndex(int index)
        {
            return _data[index];
        }

        /// <summary>
        /// 指定されたGameObjectがコンテナに存在するか確認する。
        /// </summary>
        /// <param name="obj">検索対象のGameObject</param>
        /// <returns>存在する場合true</returns>
        public bool ContainsKey(GameObject obj)
        {
            if (obj == null)
                return false;
            int hashCode = obj.GetInstanceID();
            return TryGetIndexByHash(hashCode, out _);
        }

        /// <summary>
        /// コンテナの全要素をクリアする。
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _activeCount; i++)
            {
                _data[i] = null;
            }

            _activeCount = 0;
            _entryCount = 0;
            _freeEntries.Clear();

            for (int i = 0; i < _bucketCount; i++)
                _buckets[i] = -1;
        }

        /// <summary>
        /// リソースを解放する（マネージドメモリのみ）。
        /// </summary>
        public void Dispose()
        {
            Clear();
            _data = null;
            _remainingTime = null;
            _indexToHash = null;
            _buckets = null;
            _entries = null;
            _freeEntries = null;
        }

        /// <summary>
        /// サブクラス用: 指定インデックスのタイマーを減算する。
        /// </summary>
        protected void DecrementTimer(int index, float delta)
        {
            _remainingTime[index] -= delta;
        }

        /// <summary>
        /// サブクラス用: 指定インデックスの残り時間を取得する。
        /// </summary>
        protected float GetRemainingTime(int index)
        {
            return _remainingTime[index];
        }

        /// <summary>
        /// サブクラス用: 指定インデックスの要素をBackSwapで削除する。
        /// </summary>
        protected void RemoveAtIndex(int index)
        {
            BackSwapRemove(index);
        }

        // =============================================
        // BackSwap削除ロジック
        // =============================================

        /// <summary>
        /// BackSwap方式でデータ配列から要素を削除する。
        /// 最後尾の要素を削除位置に移動し、ハッシュテーブルを更新する。
        /// </summary>
        /// <param name="dataIndex">削除するデータのインデックス</param>
        private void BackSwapRemove(int dataIndex)
        {
            int removedHash = _indexToHash[dataIndex];
            int lastIndex = _activeCount - 1;

            if (dataIndex != lastIndex)
            {
                int movedHash = _indexToHash[lastIndex];

                _data[dataIndex] = _data[lastIndex];
                _remainingTime[dataIndex] = _remainingTime[lastIndex];
                _indexToHash[dataIndex] = movedHash;

                // 移動した要素のハッシュエントリのValueIndexを更新
                UpdateEntryDataIndex(movedHash, dataIndex);
            }

            _data[lastIndex] = null;
            RemoveFromHashTable(removedHash);
            _activeCount--;
        }

        // =============================================
        // ハッシュテーブル操作
        // =============================================

        /// <summary>
        /// ハッシュコードからバケットインデックスを計算する。
        /// </summary>
        private int GetBucketIndex(int hashCode)
        {
            return (hashCode & 0x7FFFFFFF) % _bucketCount;
        }

        /// <summary>
        /// ハッシュテーブルに新しいエントリを登録する。
        /// </summary>
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

        /// <summary>
        /// ハッシュテーブルからエントリを削除し、エントリインデックスを再利用可能にする。
        /// </summary>
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

        /// <summary>
        /// ハッシュコードからデータインデックスを検索する。
        /// </summary>
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

        /// <summary>
        /// 指定ハッシュコードのエントリのValueIndexを更新する（BackSwap後の移動先を反映）。
        /// </summary>
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

        /// <summary>
        /// 容量に基づいて適切な素数バケットサイズを取得する。
        /// </summary>
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
