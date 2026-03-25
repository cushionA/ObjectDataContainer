using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODC.Runtime
{
    /// <summary>
    /// クールダウン管理コンテナ。GameObjectごとに複数の名前付きクールダウンを管理する。
    /// スキルクールダウン、攻撃間隔などの用途に設計されている。
    /// </summary>
    public class CooldownContainer : IDisposable
    {
        /// <summary>
        /// クールダウンエントリ構造体。各クールダウンの状態を保持する。
        /// </summary>
        private struct CooldownEntry
        {
            public int OwnerIndex;        // _ownersへのインデックス
            public int CooldownNameId;    // 文字列名からマッピングされたID
            public float RemainingTime;
            public float TotalDuration;   // 元のクールダウン時間
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

        private CooldownEntry[] _cooldowns;
        private int _cooldownCount;
        private int _maxCooldowns;

        private Dictionary<string, int> _cooldownNameToId;
        private int _nameIdCounter;

        // ハッシュテーブル（オーナールックアップ用）
        private int[] _buckets;
        private HashEntry[] _hashEntries;
        private int _hashEntryCount;
        private int _bucketCount;
        private Stack<int> _freeHashEntries;

        /// <summary>現在のオーナー数</summary>
        public int OwnerCount => _ownerCount;

        /// <summary>
        /// コンストラクタ。指定された最大オーナー数でコンテナを初期化する。
        /// </summary>
        /// <param name="maxOwners">最大オーナー数</param>
        /// <param name="maxCooldownsTotal">最大クールダウン総数（デフォルト: maxOwners * 4）</param>
        public CooldownContainer(int maxOwners, int maxCooldownsTotal = -1)
        {
            _maxOwners = maxOwners;
            _maxCooldowns = maxCooldownsTotal > 0 ? maxCooldownsTotal : maxOwners * 4;
            _bucketCount = GetPrimeBucketCount(maxOwners);

            _owners = new GameObject[maxOwners];
            _ownerData = new OwnerData[maxOwners];
            _ownerCount = 0;

            _cooldowns = new CooldownEntry[_maxCooldowns];
            _cooldownCount = 0;

            _cooldownNameToId = new Dictionary<string, int>();
            _nameIdCounter = 0;

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
        /// GameObjectをオーナーから削除する。関連する全クールダウンも削除される。
        /// </summary>
        public bool RemoveOwner(GameObject obj)
        {
            if (obj == null)
                return false;
            return RemoveOwner(obj.GetInstanceID());
        }

        /// <summary>
        /// int hashをオーナーから削除する。関連する全クールダウンも削除される。
        /// </summary>
        public bool RemoveOwner(int hash)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return false;

            // このオーナーの全クールダウンを削除
            for (int i = _cooldownCount - 1; i >= 0; i--)
            {
                if (_cooldowns[i].OwnerIndex == ownerIndex)
                {
                    RemoveCooldownAtIndex(i);
                    if (i < _cooldownCount && _cooldowns[i].OwnerIndex == ownerIndex)
                    {
                        i++;
                    }
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
            return ContainsOwner(obj.GetInstanceID());
        }

        /// <summary>
        /// 指定されたint hashがオーナーとして登録されているか確認する。
        /// </summary>
        public bool ContainsOwner(int hash)
        {
            return TryGetIndexByHash(hash, out _);
        }

        // =============================================
        // クールダウン操作
        // =============================================

        /// <summary>
        /// 指定されたGameObjectのクールダウンを開始する。
        /// </summary>
        public void StartCooldown(GameObject obj, string cooldownName, float duration)
        {
            if (obj == null) return;
            StartCooldown(obj.GetInstanceID(), cooldownName, duration);
        }

        /// <summary>
        /// 指定されたint hashのクールダウンを開始する。
        /// </summary>
        public void StartCooldown(int hash, string cooldownName, float duration)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return;

            int nameId = GetOrCreateNameId(cooldownName);

            for (int i = 0; i < _cooldownCount; i++)
            {
                if (_cooldowns[i].OwnerIndex == ownerIndex && _cooldowns[i].CooldownNameId == nameId)
                {
                    _cooldowns[i].RemainingTime = duration;
                    _cooldowns[i].TotalDuration = duration;
                    return;
                }
            }

            if (_cooldownCount >= _maxCooldowns)
                throw new InvalidOperationException("クールダウンの総数が上限に達しています。");

            _cooldowns[_cooldownCount] = new CooldownEntry
            {
                OwnerIndex = ownerIndex,
                CooldownNameId = nameId,
                RemainingTime = duration,
                TotalDuration = duration
            };
            _cooldownCount++;
        }

        /// <summary>
        /// 指定されたクールダウンが使用可能（非アクティブ）かどうかを確認する。
        /// </summary>
        public bool IsCooldownReady(GameObject obj, string cooldownName)
        {
            if (obj == null) return true;
            return IsCooldownReady(obj.GetInstanceID(), cooldownName);
        }

        /// <summary>
        /// 指定されたクールダウンが使用可能（非アクティブ）かどうかを確認する。
        /// </summary>
        public bool IsCooldownReady(int hash, string cooldownName)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return true;

            if (!_cooldownNameToId.TryGetValue(cooldownName, out int nameId))
                return true;

            for (int i = 0; i < _cooldownCount; i++)
            {
                if (_cooldowns[i].OwnerIndex == ownerIndex && _cooldowns[i].CooldownNameId == nameId)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 指定されたクールダウンの残り時間を取得する。使用可能な場合は0を返す。
        /// </summary>
        public float GetRemainingTime(GameObject obj, string cooldownName)
        {
            if (obj == null) return 0f;
            return GetRemainingTime(obj.GetInstanceID(), cooldownName);
        }

        /// <summary>
        /// 指定されたクールダウンの残り時間を取得する。使用可能な場合は0を返す。
        /// </summary>
        public float GetRemainingTime(int hash, string cooldownName)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return 0f;

            if (!_cooldownNameToId.TryGetValue(cooldownName, out int nameId))
                return 0f;

            for (int i = 0; i < _cooldownCount; i++)
            {
                if (_cooldowns[i].OwnerIndex == ownerIndex && _cooldowns[i].CooldownNameId == nameId)
                    return _cooldowns[i].RemainingTime;
            }
            return 0f;
        }

        /// <summary>
        /// 指定されたクールダウンの進行比率を取得する。0は使用可能、1は開始直後を意味する。
        /// </summary>
        public float GetCooldownRatio(GameObject obj, string cooldownName)
        {
            if (obj == null) return 0f;
            return GetCooldownRatio(obj.GetInstanceID(), cooldownName);
        }

        /// <summary>
        /// 指定されたクールダウンの進行比率を取得する。
        /// </summary>
        public float GetCooldownRatio(int hash, string cooldownName)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return 0f;

            if (!_cooldownNameToId.TryGetValue(cooldownName, out int nameId))
                return 0f;

            for (int i = 0; i < _cooldownCount; i++)
            {
                if (_cooldowns[i].OwnerIndex == ownerIndex && _cooldowns[i].CooldownNameId == nameId)
                {
                    if (_cooldowns[i].TotalDuration <= 0f) return 0f;
                    return _cooldowns[i].RemainingTime / _cooldowns[i].TotalDuration;
                }
            }
            return 0f;
        }

        // =============================================
        // バッチ操作
        // =============================================

        /// <summary>
        /// 全クールダウンのタイマーを更新し、完了したクールダウンを削除する。
        /// </summary>
        public void Update(float deltaTime)
        {
            for (int i = _cooldownCount - 1; i >= 0; i--)
            {
                _cooldowns[i].RemainingTime -= deltaTime;
                if (_cooldowns[i].RemainingTime <= 0f)
                {
                    RemoveCooldownAtIndex(i);
                }
            }
        }

        /// <summary>
        /// 指定されたGameObjectのアクティブなクールダウン数を取得する。
        /// </summary>
        public int GetActiveCooldownCount(GameObject obj)
        {
            if (obj == null) return 0;
            return GetActiveCooldownCount(obj.GetInstanceID());
        }

        /// <summary>
        /// 指定されたint hashのアクティブなクールダウン数を取得する。
        /// </summary>
        public int GetActiveCooldownCount(int hash)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return 0;

            int count = 0;
            for (int i = 0; i < _cooldownCount; i++)
            {
                if (_cooldowns[i].OwnerIndex == ownerIndex)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 指定されたGameObjectの全クールダウンをリセットする。
        /// </summary>
        public void ResetAllCooldowns(GameObject obj)
        {
            if (obj == null) return;
            ResetAllCooldowns(obj.GetInstanceID());
        }

        /// <summary>
        /// 指定されたint hashの全クールダウンをリセットする。
        /// </summary>
        public void ResetAllCooldowns(int hash)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return;

            for (int i = _cooldownCount - 1; i >= 0; i--)
            {
                if (_cooldowns[i].OwnerIndex == ownerIndex)
                {
                    RemoveCooldownAtIndex(i);
                }
            }
        }

        /// <summary>
        /// 指定されたGameObjectの特定のクールダウンをリセットする。
        /// </summary>
        public void ResetCooldown(GameObject obj, string cooldownName)
        {
            if (obj == null) return;
            ResetCooldown(obj.GetInstanceID(), cooldownName);
        }

        /// <summary>
        /// 指定されたint hashの特定のクールダウンをリセットする。
        /// </summary>
        public void ResetCooldown(int hash, string cooldownName)
        {
            if (!TryGetIndexByHash(hash, out int ownerIndex))
                return;

            if (!_cooldownNameToId.TryGetValue(cooldownName, out int nameId))
                return;

            for (int i = 0; i < _cooldownCount; i++)
            {
                if (_cooldowns[i].OwnerIndex == ownerIndex && _cooldowns[i].CooldownNameId == nameId)
                {
                    RemoveCooldownAtIndex(i);
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
            _cooldownCount = 0;
            _hashEntryCount = 0;
            _nameIdCounter = 0;
            _freeHashEntries.Clear();
            _cooldownNameToId.Clear();

            for (int i = 0; i < _maxOwners; i++)
            {
                _owners[i] = null;
                _ownerData[i] = default;
            }

            for (int i = 0; i < _maxCooldowns; i++)
            {
                _cooldowns[i] = default;
            }

            for (int i = 0; i < _bucketCount; i++)
                _buckets[i] = -1;
        }

        /// <summary>
        /// リソースを解放する（マネージドメモリのみ）。
        /// </summary>
        public void Dispose()
        {
            Clear();
            _owners = null;
            _ownerData = null;
            _cooldowns = null;
            _cooldownNameToId = null;
            _buckets = null;
            _hashEntries = null;
            _freeHashEntries = null;
        }

        // =============================================
        // 内部ヘルパー
        // =============================================

        private int GetOrCreateNameId(string name)
        {
            if (_cooldownNameToId.TryGetValue(name, out int id))
                return id;

            id = _nameIdCounter++;
            _cooldownNameToId[name] = id;
            return id;
        }

        private void RemoveCooldownAtIndex(int index)
        {
            int lastIndex = _cooldownCount - 1;
            if (index != lastIndex)
            {
                _cooldowns[index] = _cooldowns[lastIndex];
            }
            _cooldowns[lastIndex] = default;
            _cooldownCount--;
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

                for (int i = 0; i < _cooldownCount; i++)
                {
                    if (_cooldowns[i].OwnerIndex == lastIndex)
                    {
                        _cooldowns[i].OwnerIndex = ownerIndex;
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
                    {
                        var prevEntry = _hashEntries[prev];
                        prevEntry.NextInBucket = _hashEntries[current].NextInBucket;
                        _hashEntries[prev] = prevEntry;
                    }

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
                    var entry = _hashEntries[current];
                    entry.ValueIndex = newDataIndex;
                    _hashEntries[current] = entry;
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
