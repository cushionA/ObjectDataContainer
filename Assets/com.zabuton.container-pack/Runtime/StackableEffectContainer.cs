using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODC.Runtime
{
    /// <summary>
    /// エンティティごとに N 枠の時限スタックエフェクトスロットを管理するコンテナ。
    /// スタック加算・継続時間・周期Tick・期限切れ解放を一元管理する。
    /// TEffect は unmanaged 構造体で効果の定義を自由に格納する。
    /// </summary>
    /// <typeparam name="TEffect">エフェクトの型（unmanaged構造体）</typeparam>
    public class StackableEffectContainer<TEffect> : IDisposable where TEffect : unmanaged
    {
        /// <summary>Tickコールバック</summary>
        public delegate void TickCallback(int entityHash, int effectKey, in TEffect effect, int stacks);

        /// <summary>期限切れコールバック</summary>
        public delegate void ExpireCallback(int entityHash, int effectKey, in TEffect effect);

        /// <summary>
        /// エフェクトエントリ構造体。
        /// </summary>
        private struct EffectEntry
        {
            public int OwnerIndex;
            public int EffectKey;
            public TEffect EffectData;
            public int StackCount;
            public float RemainingTime;    // -1 = 無期限
            public float TickInterval;     // -1 = Tickなし
            public float TickElapsed;
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

        private EffectEntry[] _effects;
        private int _effectCount;
        private int _maxEffects;

        // ハッシュテーブル（オーナールックアップ用）
        private int[] _buckets;
        private HashEntry[] _hashEntries;
        private int _hashEntryCount;
        private int _bucketCount;
        private Stack<int> _freeHashEntries;

        /// <summary>現在のオーナー数</summary>
        public int OwnerCount => _ownerCount;

        /// <summary>現在のエフェクト総数</summary>
        public int EffectCount => _effectCount;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        public StackableEffectContainer(int maxOwners, int maxEffectsTotal = -1)
        {
            _maxOwners = maxOwners;
            _maxEffects = maxEffectsTotal > 0 ? maxEffectsTotal : maxOwners * 8;
            _bucketCount = GetPrimeBucketCount(maxOwners);

            _owners = new GameObject[maxOwners];
            _ownerData = new OwnerData[maxOwners];
            _ownerCount = 0;

            _effects = new EffectEntry[_maxEffects];
            _effectCount = 0;

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
        /// GameObjectをオーナーから削除する。関連する全エフェクトも削除される。
        /// </summary>
        public bool RemoveOwner(GameObject obj)
        {
            if (obj == null)
                return false;
            return RemoveOwner(obj.GetHashCode());
        }

        /// <summary>
        /// int hashをオーナーから削除する。関連する全エフェクトも削除される。
        /// </summary>
        public bool RemoveOwner(int hash)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return false;

            for (int i = _effectCount - 1; i >= 0; i--)
            {
                if (_effects[i].OwnerIndex == ownerIndex)
                {
                    RemoveEffectAtIndex(i);
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
        // エフェクト操作
        // =============================================

        /// <summary>
        /// エフェクトを適用する（GameObject版）。
        /// </summary>
        public bool Apply(GameObject obj, int effectKey, in TEffect effectData,
            int addStacks = 1, float duration = -1f, float tickInterval = -1f)
        {
            if (obj == null) return false;
            return Apply(obj.GetHashCode(), effectKey, effectData, addStacks, duration, tickInterval);
        }

        /// <summary>
        /// エフェクトを適用する（int hash版）。
        /// 同一 effectKey のスロットが存在する場合はスタックを追加し継続時間を更新。
        /// 存在しない場合は新規登録。
        /// </summary>
        public bool Apply(int hash, int effectKey, in TEffect effectData,
            int addStacks = 1, float duration = -1f, float tickInterval = -1f)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return false;

            for (int i = 0; i < _effectCount; i++)
            {
                if (_effects[i].OwnerIndex == ownerIndex && _effects[i].EffectKey == effectKey)
                {
                    _effects[i].StackCount += addStacks;
                    _effects[i].EffectData = effectData;
                    if (duration >= 0f)
                        _effects[i].RemainingTime = duration;
                    return true;
                }
            }

            if (_effectCount >= _maxEffects)
                return false;

            _effects[_effectCount] = new EffectEntry
            {
                OwnerIndex = ownerIndex,
                EffectKey = effectKey,
                EffectData = effectData,
                StackCount = addStacks,
                RemainingTime = duration,
                TickInterval = tickInterval,
                TickElapsed = 0f
            };
            _effectCount++;
            return true;
        }

        /// <summary>
        /// 指定エフェクトを即時解除（GameObject版）。
        /// </summary>
        public bool Remove(GameObject obj, int effectKey)
        {
            if (obj == null) return false;
            return Remove(obj.GetHashCode(), effectKey);
        }

        /// <summary>
        /// 指定エフェクトを即時解除（int hash版）。
        /// </summary>
        public bool Remove(int hash, int effectKey)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return false;

            for (int i = 0; i < _effectCount; i++)
            {
                if (_effects[i].OwnerIndex == ownerIndex && _effects[i].EffectKey == effectKey)
                {
                    RemoveEffectAtIndex(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 指定エフェクトが発動中か（GameObject版）。
        /// </summary>
        public bool IsActive(GameObject obj, int effectKey)
        {
            if (obj == null) return false;
            return IsActive(obj.GetHashCode(), effectKey);
        }

        /// <summary>
        /// 指定エフェクトが発動中か（int hash版）。
        /// </summary>
        public bool IsActive(int hash, int effectKey)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return false;

            for (int i = 0; i < _effectCount; i++)
            {
                if (_effects[i].OwnerIndex == ownerIndex && _effects[i].EffectKey == effectKey)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// スタック数を取得（GameObject版）。
        /// </summary>
        public int GetStacks(GameObject obj, int effectKey)
        {
            if (obj == null) return 0;
            return GetStacks(obj.GetHashCode(), effectKey);
        }

        /// <summary>
        /// スタック数を取得（int hash版）。
        /// </summary>
        public int GetStacks(int hash, int effectKey)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return 0;

            for (int i = 0; i < _effectCount; i++)
            {
                if (_effects[i].OwnerIndex == ownerIndex && _effects[i].EffectKey == effectKey)
                    return _effects[i].StackCount;
            }
            return 0;
        }

        /// <summary>
        /// エンティティのアクティブエフェクトを全て列挙する（GameObject版）。
        /// </summary>
        public int GetActiveEffects(GameObject obj,
            Span<(int effectKey, TEffect effectData, int stacks, float remainingTime)> results)
        {
            if (obj == null) return 0;
            return GetActiveEffects(obj.GetHashCode(), results);
        }

        /// <summary>
        /// エンティティのアクティブエフェクトを全て列挙する（int hash版）。
        /// </summary>
        public int GetActiveEffects(int hash,
            Span<(int effectKey, TEffect effectData, int stacks, float remainingTime)> results)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return 0;

            int count = 0;
            for (int i = 0; i < _effectCount && count < results.Length; i++)
            {
                if (_effects[i].OwnerIndex == ownerIndex)
                {
                    results[count] = (
                        _effects[i].EffectKey,
                        _effects[i].EffectData,
                        _effects[i].StackCount,
                        _effects[i].RemainingTime
                    );
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// アクティブエフェクト数（GameObject版）。
        /// </summary>
        public int GetActiveCount(GameObject obj)
        {
            if (obj == null) return 0;
            return GetActiveCount(obj.GetHashCode());
        }

        /// <summary>
        /// アクティブエフェクト数（int hash版）。
        /// </summary>
        public int GetActiveCount(int hash)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return 0;

            int count = 0;
            for (int i = 0; i < _effectCount; i++)
            {
                if (_effects[i].OwnerIndex == ownerIndex)
                    count++;
            }
            return count;
        }

        // =============================================
        // Tick処理
        // =============================================

        /// <summary>
        /// 全エンティティを一括Tick。
        /// </summary>
        public void TickAll(float deltaTime,
            TickCallback onTick = null,
            ExpireCallback onExpire = null)
        {
            for (int i = _effectCount - 1; i >= 0; i--)
            {
                ref EffectEntry effect = ref _effects[i];
                int entityHash = _ownerData[effect.OwnerIndex].HashCode;

                if (effect.RemainingTime >= 0f)
                {
                    effect.RemainingTime -= deltaTime;
                    if (effect.RemainingTime <= 0f)
                    {
                        onExpire?.Invoke(entityHash, effect.EffectKey, effect.EffectData);
                        RemoveEffectAtIndex(i);
                        continue;
                    }
                }

                if (effect.TickInterval > 0f)
                {
                    effect.TickElapsed += deltaTime;
                    while (effect.TickElapsed >= effect.TickInterval)
                    {
                        effect.TickElapsed -= effect.TickInterval;
                        onTick?.Invoke(entityHash, effect.EffectKey, effect.EffectData, effect.StackCount);
                    }
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
            _effectCount = 0;
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
            _effects = null;
            _buckets = null;
            _hashEntries = null;
            _freeHashEntries = null;
        }

        // =============================================
        // 内部ヘルパー
        // =============================================

        private void RemoveEffectAtIndex(int index)
        {
            int lastIndex = _effectCount - 1;
            if (index != lastIndex)
            {
                _effects[index] = _effects[lastIndex];
            }
            _effects[lastIndex] = default;
            _effectCount--;
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

                for (int i = 0; i < _effectCount; i++)
                {
                    if (_effects[i].OwnerIndex == lastIndex)
                    {
                        _effects[i].OwnerIndex = ownerIndex;
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
