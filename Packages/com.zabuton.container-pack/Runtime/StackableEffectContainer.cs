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
        /// <param name="maxOwners">最大オーナー数</param>
        /// <param name="maxEffectsTotal">最大エフェクト総数（デフォルト: maxOwners * 8）</param>
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
            if (_ownerCount >= _maxOwners)
                throw new InvalidOperationException("オーナー数が上限に達しています。");

            int hashCode = obj.GetInstanceID();
            if (TryGetIndexByHash(hashCode, out _))
                throw new InvalidOperationException("同じGameObjectが既に登録されています。");

            int ownerIndex = _ownerCount;
            _owners[ownerIndex] = obj;
            _ownerData[ownerIndex] = new OwnerData { HashCode = hashCode };
            _ownerCount++;

            RegisterToHashTable(hashCode, ownerIndex);
        }

        /// <summary>
        /// GameObjectをオーナーから削除する。関連する全エフェクトも削除される。
        /// </summary>
        public bool RemoveOwner(GameObject obj)
        {
            if (obj == null)
                return false;

            int hashCode = obj.GetInstanceID();
            if (!TryGetIndexByHash(hashCode, out int ownerIndex))
                return false;

            // BackSwapで末尾要素が現在位置に移動するため、
            // 移動された要素も同オーナーの可能性がある → whileで再チェック
            int i = _effectCount - 1;
            while (i >= 0)
            {
                if (_effects[i].OwnerIndex == ownerIndex)
                {
                    RemoveEffectAtIndex(i);
                    // BackSwapで_effects[i]に新しい要素が来た可能性 → iをデクリメントしない
                }
                else
                {
                    i--;
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
            int hashCode = obj.GetInstanceID();
            return TryGetIndexByHash(hashCode, out _);
        }

        // =============================================
        // エフェクト操作
        // =============================================

        /// <summary>
        /// エフェクトを適用する。
        /// 同一 effectKey のスロットが存在する場合はスタックを追加し継続時間を更新。
        /// 存在しない場合は新規登録。
        /// </summary>
        /// <param name="obj">対象のGameObject</param>
        /// <param name="effectKey">エフェクトキー（enum等をintにキャスト）</param>
        /// <param name="effectData">エフェクトデータ</param>
        /// <param name="addStacks">追加するスタック数</param>
        /// <param name="duration">持続時間（秒）。-1で無期限。</param>
        /// <param name="tickInterval">Tick間隔（秒）。-1でTickなし。</param>
        /// <returns>適用に成功した場合true</returns>
        public bool Apply(GameObject obj, int effectKey, in TEffect effectData,
            int addStacks = 1, float duration = -1f, float tickInterval = -1f)
        {
            if (obj == null) return false;

            int hashCode = obj.GetInstanceID();
            if (!TryGetIndexByHash(hashCode, out int ownerIndex))
                return false;

            // 既存のエフェクトを検索
            for (int i = 0; i < _effectCount; i++)
            {
                if (_effects[i].OwnerIndex == ownerIndex && _effects[i].EffectKey == effectKey)
                {
                    _effects[i].StackCount += addStacks;
                    _effects[i].EffectData = effectData;
                    if (duration >= 0f)
                        _effects[i].RemainingTime = duration; // 継続時間をリフレッシュ
                    return true;
                }
            }

            // 新規登録
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
        /// 指定エフェクトを即時解除。
        /// </summary>
        /// <returns>解除に成功した場合true</returns>
        public bool Remove(GameObject obj, int effectKey)
        {
            if (obj == null) return false;

            int hashCode = obj.GetInstanceID();
            if (!TryGetIndexByHash(hashCode, out int ownerIndex))
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
        /// 指定エフェクトが発動中か。
        /// </summary>
        public bool IsActive(GameObject obj, int effectKey)
        {
            if (obj == null) return false;

            int hashCode = obj.GetInstanceID();
            if (!TryGetIndexByHash(hashCode, out int ownerIndex))
                return false;

            for (int i = 0; i < _effectCount; i++)
            {
                if (_effects[i].OwnerIndex == ownerIndex && _effects[i].EffectKey == effectKey)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// スタック数を取得。
        /// </summary>
        public int GetStacks(GameObject obj, int effectKey)
        {
            if (obj == null) return 0;

            int hashCode = obj.GetInstanceID();
            if (!TryGetIndexByHash(hashCode, out int ownerIndex))
                return 0;

            for (int i = 0; i < _effectCount; i++)
            {
                if (_effects[i].OwnerIndex == ownerIndex && _effects[i].EffectKey == effectKey)
                    return _effects[i].StackCount;
            }
            return 0;
        }

        /// <summary>
        /// エンティティのアクティブエフェクトを全て列挙する。
        /// results に (effectKey, effectData, stacks, remainingTime) を書き込み、件数を返す。
        /// UI表示・デバッグ用途を想定。GCフリー（Span で受け取る）。
        /// </summary>
        public int GetActiveEffects(GameObject obj,
            Span<(int effectKey, TEffect effectData, int stacks, float remainingTime)> results)
        {
            if (obj == null) return 0;

            int hashCode = obj.GetInstanceID();
            if (!TryGetIndexByHash(hashCode, out int ownerIndex))
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
        /// アクティブエフェクト数（現在かかっているエフェクトの種類数）。
        /// </summary>
        public int GetActiveCount(GameObject obj)
        {
            if (obj == null) return 0;

            int hashCode = obj.GetInstanceID();
            if (!TryGetIndexByHash(hashCode, out int ownerIndex))
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
        /// 期限切れスロットを自動解放し、TickInterval到達時にonTickを呼ぶ。
        /// コールバック内でこのコンテナのApply/Removeを呼ばないこと（走査中の配列変更で不整合になる）。
        /// </summary>
        public void TickAll(float deltaTime,
            TickCallback onTick = null,
            ExpireCallback onExpire = null)
        {
            for (int i = _effectCount - 1; i >= 0; i--)
            {
                ref EffectEntry effect = ref _effects[i];
                int entityHash = _ownerData[effect.OwnerIndex].HashCode;

                // 持続時間の更新
                if (effect.RemainingTime >= 0f)
                {
                    effect.RemainingTime -= deltaTime;
                    if (effect.RemainingTime <= 0f)
                    {
                        // 期限切れ
                        onExpire?.Invoke(entityHash, effect.EffectKey, effect.EffectData);
                        RemoveEffectAtIndex(i);
                        continue;
                    }
                }

                // TickInterval処理（大きなdeltaTimeで複数回発火）
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

                // 移動したオーナーを参照するエフェクトのOwnerIndexを更新
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
