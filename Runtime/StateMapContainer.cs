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

        /// <summary>
        /// ステート遷移時のコールバック情報。
        /// </summary>
        private struct StateCallback
        {
            public TState State;
            public Action<GameObject, TState> OnEnter;  // (obj, fromState)
            public Action<GameObject, TState> OnExit;   // (obj, toState)
        }

        private StateEntry[] _states;
        private GameObject[] _gameObjects;
        private int _activeCount;
        private int _maxCapacity;

        // ステートごとのコールバック登録（少数のステートを想定）
        private StateCallback[] _callbacks;
        private int _callbackCount;

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

            _callbacks = new StateCallback[32];
            _callbackCount = 0;
        }

        /// <summary>
        /// GameObjectと初期ステートを追加する。
        /// </summary>
        public void Add(GameObject obj, TState initialState)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            Add(obj.GetInstanceID(), initialState);
            _gameObjects[_activeCount - 1] = obj;
        }

        /// <summary>
        /// int hashと初期ステートを追加する。
        /// </summary>
        public void Add(int hash, TState initialState)
        {
            if (_activeCount >= _maxCapacity)
                throw new InvalidOperationException("コンテナが満杯です。");
            if (TryGetIndexByHash(hash, out _))
                throw new InvalidOperationException("同じハッシュが既に登録されています。");

            int index = _activeCount;

            _states[index] = new StateEntry
            {
                CurrentState = initialState,
                PreviousState = initialState,
                StateElapsedTime = 0f,
                HashCode = hash
            };
            _activeCount++;

            AddToHashTable(hash, index);
        }

        /// <summary>
        /// GameObjectを削除する。
        /// </summary>
        public bool Remove(GameObject obj)
        {
            if (obj == null) return false;
            return Remove(obj.GetInstanceID());
        }

        /// <summary>
        /// int hashを削除する。
        /// </summary>
        public bool Remove(int hash)
        {
            if (!TryGetIndexByHash(hash, out int index))
                return false;

            int lastIndex = _activeCount - 1;

            RemoveFromHashTable(hash, index);

            if (index < lastIndex)
            {
                int lastHashCode = _states[lastIndex].HashCode;

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
        /// 特定のステートに対するOnEnter/OnExitコールバックを登録する。
        /// </summary>
        public void RegisterCallback(TState state, Action<GameObject, TState> onEnter = null, Action<GameObject, TState> onExit = null)
        {
            var comparer = EqualityComparer<TState>.Default;

            for (int i = 0; i < _callbackCount; i++)
            {
                if (comparer.Equals(_callbacks[i].State, state))
                {
                    _callbacks[i].OnEnter = onEnter;
                    _callbacks[i].OnExit = onExit;
                    return;
                }
            }

            if (_callbackCount >= _callbacks.Length)
                throw new InvalidOperationException($"コールバック登録数が上限（{_callbacks.Length}）に達しています。");

            _callbacks[_callbackCount++] = new StateCallback
            {
                State = state,
                OnEnter = onEnter,
                OnExit = onExit
            };
        }

        /// <summary>
        /// 特定のステートのコールバック登録を解除する。
        /// </summary>
        public void UnregisterCallback(TState state)
        {
            var comparer = EqualityComparer<TState>.Default;
            for (int i = 0; i < _callbackCount; i++)
            {
                if (comparer.Equals(_callbacks[i].State, state))
                {
                    _callbackCount--;
                    if (i < _callbackCount)
                        _callbacks[i] = _callbacks[_callbackCount];
                    _callbacks[_callbackCount] = default;
                    return;
                }
            }
        }

        /// <summary>
        /// ステートを遷移させる（GameObject版）。
        /// </summary>
        public bool SetState(GameObject obj, TState newState)
        {
            if (obj == null) return false;
            int hash = obj.GetInstanceID();

            if (TryGetIndexByHash(hash, out int index))
            {
                ref var entry = ref _states[index];
                if (!EqualityComparer<TState>.Default.Equals(entry.CurrentState, newState))
                {
                    TState oldState = entry.CurrentState;
                    entry.PreviousState = oldState;
                    entry.CurrentState = newState;
                    entry.StateElapsedTime = 0f;

                    if (_callbackCount > 0)
                    {
                        FireCallbacks(obj, oldState, newState);
                    }
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// ステートを遷移させる（int hash版）。コールバックにはnullのGameObjectが渡される。
        /// </summary>
        public bool SetState(int hash, TState newState)
        {
            if (TryGetIndexByHash(hash, out int index))
            {
                ref var entry = ref _states[index];
                if (!EqualityComparer<TState>.Default.Equals(entry.CurrentState, newState))
                {
                    TState oldState = entry.CurrentState;
                    entry.PreviousState = oldState;
                    entry.CurrentState = newState;
                    entry.StateElapsedTime = 0f;

                    if (_callbackCount > 0)
                    {
                        FireCallbacks(_gameObjects[index], oldState, newState);
                    }
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// GameObjectの現在のステートを取得する。
        /// </summary>
        public bool TryGetState(GameObject obj, out TState current)
        {
            if (obj == null)
            {
                current = default;
                return false;
            }
            return TryGetState(obj.GetInstanceID(), out current);
        }

        /// <summary>
        /// int hashの現在のステートを取得する。
        /// </summary>
        public bool TryGetState(int hash, out TState current)
        {
            if (TryGetIndexByHash(hash, out int index))
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
        public bool TryGetState(GameObject obj, out TState current, out TState previous, out float elapsed)
        {
            if (obj == null)
            {
                current = default;
                previous = default;
                elapsed = 0f;
                return false;
            }
            return TryGetState(obj.GetInstanceID(), out current, out previous, out elapsed);
        }

        /// <summary>
        /// int hashの現在・前回のステートと経過時間を取得する。
        /// </summary>
        public bool TryGetState(int hash, out TState current, out TState previous, out float elapsed)
        {
            if (TryGetIndexByHash(hash, out int index))
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
        public TState GetState(GameObject obj)
        {
            if (TryGetState(obj, out var state))
                return state;

            throw new KeyNotFoundException("指定されたGameObjectはコンテナに存在しません。");
        }

        /// <summary>
        /// int hashの現在のステートを取得する。見つからない場合は例外をスローする。
        /// </summary>
        public TState GetState(int hash)
        {
            if (TryGetState(hash, out var state))
                return state;

            throw new KeyNotFoundException("指定されたハッシュはコンテナに存在しません。");
        }

        /// <summary>
        /// 全要素の経過時間をインクリメントする。
        /// </summary>
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
        /// 指定したステートにある全ハッシュを取得する。
        /// </summary>
        public int GetAllInState(TState state, Span<int> results)
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
                        results[written] = _states[i].HashCode;
                        written++;
                    }
                }
            }

            return written;
        }

        /// <summary>
        /// 指定されたGameObjectがコンテナに含まれているか確認する。
        /// </summary>
        public bool ContainsKey(GameObject obj)
        {
            if (obj == null) return false;
            return ContainsKey(obj.GetInstanceID());
        }

        /// <summary>
        /// 指定されたint hashがコンテナに含まれているか確認する。
        /// </summary>
        public bool ContainsKey(int hash)
        {
            return TryGetIndexByHash(hash, out _);
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
            _callbacks = null;
            _callbackCount = 0;
        }

        // ===== コールバック発火 =====

        private void FireCallbacks(GameObject obj, TState oldState, TState newState)
        {
            var comparer = EqualityComparer<TState>.Default;

            for (int i = 0; i < _callbackCount; i++)
            {
                if (comparer.Equals(_callbacks[i].State, oldState))
                {
                    _callbacks[i].OnExit?.Invoke(obj, newState);
                    break;
                }
            }

            for (int i = 0; i < _callbackCount; i++)
            {
                if (comparer.Equals(_callbacks[i].State, newState))
                {
                    _callbacks[i].OnEnter?.Invoke(obj, oldState);
                    break;
                }
            }
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
