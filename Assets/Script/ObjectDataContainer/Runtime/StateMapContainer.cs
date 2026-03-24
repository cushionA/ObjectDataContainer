using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODC.Runtime
{
    /// <summary>
    /// FSMライクなステート管理コンテナ。
    /// GameObjectごとに現在のステート（enum）、前回のステート、経過時間を管理する。
    /// </summary>
    /// <typeparam name="TState">ステートを表すenum型</typeparam>
    public class StateMapContainer<TState> : IDisposable where TState : struct, Enum
    {
        /// <summary>
        /// 各GameObjectのステート情報。現在・前回のステート、経過時間、ハッシュコードを保持する。
        /// </summary>
        private struct StateEntry
        {
            public TState CurrentState;
            public TState PreviousState;
            public float StateElapsedTime;
            public int HashCode;
        }

        private StateEntry[] _states;
        private GameObject[] _gameObjects;
        private int _activeCount;
        private int _maxCapacity;

        // ハッシュテーブル（他のコンテナと同じパターン）
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
        /// アクティブな要素数を返す。
        /// </summary>
        public int Count => _activeCount;

        /// <summary>
        /// ステートマップコンテナを生成する。
        /// </summary>
        /// <param name="maxCapacity">最大要素数</param>
        public StateMapContainer(int maxCapacity)
        {
            _maxCapacity = maxCapacity;
            _activeCount = 0;

            _states = new StateEntry[maxCapacity];
            _gameObjects = new GameObject[maxCapacity];

            _bucketCount = NextPowerOfTwo(maxCapacity);
            _buckets = new int[_bucketCount];
            Array.Fill(_buckets, -1);
            _hashEntries = new HashTableEntry[maxCapacity];
            _hashEntryCount = 0;
            _freeHashEntries = new Stack<int>();
        }

        /// <summary>
        /// GameObjectと初期ステートを追加する。
        /// </summary>
        /// <param name="obj">追加するGameObject</param>
        /// <param name="initialState">初期ステート</param>
        public void Add(GameObject obj, TState initialState)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            if (_activeCount >= _maxCapacity)
                throw new InvalidOperationException("コンテナが満杯です。");

            int hashCode = obj.GetHashCode() & 0x7FFFFFFF;
            if (TryGetIndexByHash(hashCode, out _))
                throw new InvalidOperationException("同じGameObjectが既に登録されています。");

            int index = _activeCount;

            _gameObjects[index] = obj;
            _states[index] = new StateEntry
            {
                CurrentState = initialState,
                PreviousState = initialState,
                StateElapsedTime = 0f,
                HashCode = hashCode
            };
            _activeCount++;

            AddToHashTable(hashCode, index);
        }

        /// <summary>
        /// GameObjectを削除する。BackSwap方式で最後尾の要素と入れ替える。
        /// </summary>
        /// <param name="obj">削除するGameObject</param>
        /// <returns>削除に成功した場合true</returns>
        public bool Remove(GameObject obj)
        {
            if (obj == null) return false;
            int hashCode = obj.GetHashCode() & 0x7FFFFFFF;

            if (!TryGetIndexByHash(hashCode, out int index))
                return false;

            int lastIndex = _activeCount - 1;

            // ハッシュテーブルから削除
            RemoveFromHashTable(hashCode, index);

            if (index < lastIndex)
            {
                // BackSwap: 最後尾を削除位置に移動
                int lastHashCode = _states[lastIndex].HashCode;

                // 最後尾のハッシュテーブルエントリを更新
                UpdateHashTableIndex(lastHashCode, lastIndex, index);

                _gameObjects[index] = _gameObjects[lastIndex];
                _states[index] = _states[lastIndex];
            }

            _gameObjects[lastIndex] = null;
            _states[lastIndex] = default;
            _activeCount--;
            return true;
        }

        /// <summary>
        /// ステートを遷移させる。同じステートの場合は何もしない。
        /// </summary>
        /// <param name="obj">対象のGameObject</param>
        /// <param name="newState">新しいステート</param>
        /// <returns>GameObjectが見つかった場合true</returns>
        public bool SetState(GameObject obj, TState newState)
        {
            if (obj == null) return false;
            int hashCode = obj.GetHashCode() & 0x7FFFFFFF;

            if (TryGetIndexByHash(hashCode, out int index))
            {
                ref var entry = ref _states[index];
                if (!EqualityComparer<TState>.Default.Equals(entry.CurrentState, newState))
                {
                    entry.PreviousState = entry.CurrentState;
                    entry.CurrentState = newState;
                    entry.StateElapsedTime = 0f;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// GameObjectの現在のステートを取得する。
        /// </summary>
        /// <param name="obj">対象のGameObject</param>
        /// <param name="current">現在のステート</param>
        /// <returns>見つかった場合true</returns>
        public bool TryGetState(GameObject obj, out TState current)
        {
            if (obj == null)
            {
                current = default;
                return false;
            }

            int hashCode = obj.GetHashCode() & 0x7FFFFFFF;
            if (TryGetIndexByHash(hashCode, out int index))
            {
                current = _states[index].CurrentState;
                return true;
            }

            current = default;
            return false;
        }

        /// <summary>
        /// GameObjectの現在・前回のステートと経過時間を取得する。
        /// </summary>
        /// <param name="obj">対象のGameObject</param>
        /// <param name="current">現在のステート</param>
        /// <param name="previous">前回のステート</param>
        /// <param name="elapsed">現在のステートになってからの経過時間</param>
        /// <returns>見つかった場合true</returns>
        public bool TryGetState(GameObject obj, out TState current, out TState previous, out float elapsed)
        {
            if (obj == null)
            {
                current = default;
                previous = default;
                elapsed = 0f;
                return false;
            }

            int hashCode = obj.GetHashCode() & 0x7FFFFFFF;
            if (TryGetIndexByHash(hashCode, out int index))
            {
                ref var entry = ref _states[index];
                current = entry.CurrentState;
                previous = entry.PreviousState;
                elapsed = entry.StateElapsedTime;
                return true;
            }

            current = default;
            previous = default;
            elapsed = 0f;
            return false;
        }

        /// <summary>
        /// GameObjectの現在のステートを取得する。見つからない場合は例外をスローする。
        /// </summary>
        /// <param name="obj">対象のGameObject</param>
        /// <returns>現在のステート</returns>
        /// <exception cref="KeyNotFoundException">GameObjectが見つからない場合</exception>
        public TState GetState(GameObject obj)
        {
            if (TryGetState(obj, out var state))
                return state;

            throw new KeyNotFoundException("指定されたGameObjectはコンテナに存在しません。");
        }

        /// <summary>
        /// 全要素の経過時間をインクリメントする。
        /// </summary>
        /// <param name="deltaTime">経過時間（秒）</param>
        public void Update(float deltaTime)
        {
            for (int i = 0; i < _activeCount; i++)
            {
                _states[i].StateElapsedTime += deltaTime;
            }
        }

        /// <summary>
        /// 指定したステートにある全GameObjectを取得する。
        /// </summary>
        /// <param name="state">検索するステート</param>
        /// <param name="results">結果を書き込むSpan</param>
        /// <returns>見つかった要素数</returns>
        public int GetAllInState(TState state, Span<GameObject> results)
        {
            int written = 0;
            int maxResults = results.Length;
            var comparer = EqualityComparer<TState>.Default;

            for (int i = 0; i < _activeCount; i++)
            {
                if (comparer.Equals(_states[i].CurrentState, state))
                {
                    if (written < maxResults)
                    {
                        results[written] = _gameObjects[i];
                        written++;
                    }
                }
            }

            return written;
        }

        /// <summary>
        /// 指定したGameObjectがコンテナに含まれているか確認する。
        /// </summary>
        /// <param name="obj">確認するGameObject</param>
        /// <returns>含まれている場合true</returns>
        public bool ContainsKey(GameObject obj)
        {
            if (obj == null) return false;
            int hashCode = obj.GetHashCode() & 0x7FFFFFFF;
            return TryGetIndexByHash(hashCode, out _);
        }

        /// <summary>
        /// コンテナの全要素をクリアする。
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _activeCount; i++)
            {
                _gameObjects[i] = null;
                _states[i] = default;
            }

            _activeCount = 0;

            Array.Fill(_buckets, -1);
            _hashEntryCount = 0;
            _freeHashEntries.Clear();
        }

        /// <summary>
        /// コンテナを破棄し、リソースを解放する。
        /// </summary>
        public void Dispose()
        {
            Clear();
            _states = null;
            _gameObjects = null;
            _buckets = null;
            _hashEntries = null;
        }

        // ===== ハッシュテーブル操作 =====

        private void AddToHashTable(int hashCode, int valueIndex)
        {
            int bucketIndex = hashCode & (_bucketCount - 1);

            int entryIndex;
            if (_freeHashEntries.Count > 0)
            {
                entryIndex = _freeHashEntries.Pop();
            }
            else
            {
                if (_hashEntryCount >= _hashEntries.Length)
                    Array.Resize(ref _hashEntries, _hashEntries.Length * 2);
                entryIndex = _hashEntryCount++;
            }

            _hashEntries[entryIndex] = new HashTableEntry
            {
                HashCode = hashCode,
                ValueIndex = valueIndex,
                NextInBucket = _buckets[bucketIndex]
            };
            _buckets[bucketIndex] = entryIndex;
        }

        private void RemoveFromHashTable(int hashCode, int valueIndex)
        {
            int bucketIndex = hashCode & (_bucketCount - 1);
            int prevIndex = -1;
            int entryIndex = _buckets[bucketIndex];

            while (entryIndex >= 0)
            {
                ref var entry = ref _hashEntries[entryIndex];
                if (entry.HashCode == hashCode && entry.ValueIndex == valueIndex)
                {
                    if (prevIndex < 0)
                        _buckets[bucketIndex] = entry.NextInBucket;
                    else
                        _hashEntries[prevIndex].NextInBucket = entry.NextInBucket;

                    _freeHashEntries.Push(entryIndex);
                    return;
                }
                prevIndex = entryIndex;
                entryIndex = entry.NextInBucket;
            }
        }

        private void UpdateHashTableIndex(int hashCode, int oldValueIndex, int newValueIndex)
        {
            int bucketIndex = hashCode & (_bucketCount - 1);
            int entryIndex = _buckets[bucketIndex];

            while (entryIndex >= 0)
            {
                ref var entry = ref _hashEntries[entryIndex];
                if (entry.HashCode == hashCode && entry.ValueIndex == oldValueIndex)
                {
                    entry.ValueIndex = newValueIndex;
                    return;
                }
                entryIndex = entry.NextInBucket;
            }
        }

        private bool TryGetIndexByHash(int hashCode, out int valueIndex)
        {
            int bucketIndex = hashCode & (_bucketCount - 1);
            int entryIndex = _buckets[bucketIndex];

            while (entryIndex >= 0)
            {
                ref var entry = ref _hashEntries[entryIndex];
                if (entry.HashCode == hashCode)
                {
                    valueIndex = entry.ValueIndex;
                    return true;
                }
                entryIndex = entry.NextInBucket;
            }

            valueIndex = -1;
            return false;
        }

        // ===== ユーティリティ =====

        private static int NextPowerOfTwo(int value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }
    }
}
