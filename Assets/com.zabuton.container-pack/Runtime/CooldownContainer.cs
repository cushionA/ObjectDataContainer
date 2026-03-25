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
        /// <param name="obj">追加するGameObject</param>
        /// <exception cref="ArgumentNullException">objがnullの場合</exception>
        /// <exception cref="InvalidOperationException">オーナー数が上限に達した場合</exception>
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
        /// GameObjectをオーナーから削除する。関連する全クールダウンも削除される。
        /// </summary>
        /// <param name="obj">削除するGameObject</param>
        /// <returns>削除に成功した場合true</returns>
        public bool RemoveOwner(GameObject obj)
        {
            if (obj == null)
                return false;

            int hashCode = obj.GetInstanceID();
            if (!TryGetIndexByHash(hashCode, out int ownerIndex))
                return false;

            // このオーナーの全クールダウンを削除
            // BackSwapで末尾要素が現在位置に移動するため、削除後にiを再チェックする
            for (int i = _cooldownCount - 1; i >= 0; i--)
            {
                if (_cooldowns[i].OwnerIndex == ownerIndex)
                {
                    RemoveCooldownAtIndex(i);
                    // BackSwapで移動してきた要素も同じオーナーの可能性があるため再チェック
                    if (i < _cooldownCount && _cooldowns[i].OwnerIndex == ownerIndex)
                    {
                        i++; // 次のループでiが再度チェックされる
                    }
                }
            }

            // オーナーをBackSwap削除
            BackSwapRemoveOwner(ownerIndex);
            return true;
        }

        /// <summary>
        /// 指定されたGameObjectがオーナーとして登録されているか確認する。
        /// </summary>
        /// <param name="obj">検索対象のGameObject</param>
        /// <returns>登録されている場合true</returns>
        public bool ContainsOwner(GameObject obj)
        {
            if (obj == null)
                return false;
            int hashCode = obj.GetInstanceID();
            return TryGetIndexByHash(hashCode, out _);
        }

        // =============================================
        // クールダウン操作
        // =============================================

        /// <summary>
        /// 指定されたGameObjectのクールダウンを開始する。
        /// 既にアクティブなクールダウンがある場合、タイマーをリセットする。
        /// </summary>
        /// <param name="obj">対象のGameObject</param>
        /// <param name="cooldownName">クールダウン名</param>
        /// <param name="duration">クールダウン時間（秒）</param>
        public void StartCooldown(GameObject obj, string cooldownName, float duration)
        {
            if (obj == null) return;

            int hashCode = obj.GetInstanceID();
            if (!TryGetIndexByHash(hashCode, out int ownerIndex))
                return;

            int nameId = GetOrCreateNameId(cooldownName);

            // 既存のクールダウンを検索
            for (int i = 0; i < _cooldownCount; i++)
            {
                if (_cooldowns[i].OwnerIndex == ownerIndex && _cooldowns[i].CooldownNameId == nameId)
                {
                    // 既存のクールダウンをリセット
                    _cooldowns[i].RemainingTime = duration;
                    _cooldowns[i].TotalDuration = duration;
                    return;
                }
            }

            // 新しいクールダウンを追加
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
        /// <param name="obj">対象のGameObject</param>
        /// <param name="cooldownName">クールダウン名</param>
        /// <returns>使用可能な場合true</returns>
        public bool IsCooldownReady(GameObject obj, string cooldownName)
        {
            if (obj == null) return true;

            int hashCode = obj.GetInstanceID();
            if (!TryGetIndexByHash(hashCode, out int ownerIndex))
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
        /// <param name="obj">対象のGameObject</param>
        /// <param name="cooldownName">クールダウン名</param>
        /// <returns>残り時間（秒）。使用可能な場合は0</returns>
        public float GetRemainingTime(GameObject obj, string cooldownName)
        {
            if (obj == null) return 0f;

            int hashCode = obj.GetInstanceID();
            if (!TryGetIndexByHash(hashCode, out int ownerIndex))
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
        /// <param name="obj">対象のGameObject</param>
        /// <param name="cooldownName">クールダウン名</param>
        /// <returns>クールダウン比率（0..1）</returns>
        public float GetCooldownRatio(GameObject obj, string cooldownName)
        {
            if (obj == null) return 0f;

            int hashCode = obj.GetInstanceID();
            if (!TryGetIndexByHash(hashCode, out int ownerIndex))
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
        /// 逆順イテレーションでBackSwap安全な削除を行う。
        /// </summary>
        /// <param name="deltaTime">経過時間（秒）</param>
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
        /// <param name="obj">対象のGameObject</param>
        /// <returns>アクティブなクールダウン数</returns>
        public int GetActiveCooldownCount(GameObject obj)
        {
            if (obj == null) return 0;

            int hashCode = obj.GetInstanceID();
            if (!TryGetIndexByHash(hashCode, out int ownerIndex))
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
        /// <param name="obj">対象のGameObject</param>
        public void ResetAllCooldowns(GameObject obj)
        {
            if (obj == null) return;

            int hashCode = obj.GetInstanceID();
            if (!TryGetIndexByHash(hashCode, out int ownerIndex))
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
        /// <param name="obj">対象のGameObject</param>
        /// <param name="cooldownName">リセットするクールダウン名</param>
        public void ResetCooldown(GameObject obj, string cooldownName)
        {
            if (obj == null) return;

            int hashCode = obj.GetInstanceID();
            if (!TryGetIndexByHash(hashCode, out int ownerIndex))
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

        /// <summary>
        /// クールダウン名のIDを取得または作成する。
        /// </summary>
        private int GetOrCreateNameId(string name)
        {
            if (_cooldownNameToId.TryGetValue(name, out int id))
                return id;

            id = _nameIdCounter++;
            _cooldownNameToId[name] = id;
            return id;
        }

        /// <summary>
        /// クールダウン配列からBackSwap方式で要素を削除する。
        /// </summary>
        /// <param name="index">削除するクールダウンのインデックス</param>
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

        /// <summary>
        /// オーナー配列からBackSwap方式で要素を削除する。
        /// 移動されたオーナーを参照するクールダウンのOwnerIndexも更新する。
        /// </summary>
        /// <param name="ownerIndex">削除するオーナーのインデックス</param>
        private void BackSwapRemoveOwner(int ownerIndex)
        {
            int removedHash = _ownerData[ownerIndex].HashCode;
            int lastIndex = _ownerCount - 1;

            if (ownerIndex != lastIndex)
            {
                int movedHash = _ownerData[lastIndex].HashCode;

                _owners[ownerIndex] = _owners[lastIndex];
                _ownerData[ownerIndex] = _ownerData[lastIndex];

                // 移動したオーナーを参照するクールダウンのOwnerIndexを更新
                for (int i = 0; i < _cooldownCount; i++)
                {
                    if (_cooldowns[i].OwnerIndex == lastIndex)
                    {
                        _cooldowns[i].OwnerIndex = ownerIndex;
                    }
                }

                // ハッシュテーブルのValueIndexを更新
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

        /// <summary>
        /// ハッシュコードからデータインデックスを検索する。
        /// </summary>
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

        /// <summary>
        /// 指定ハッシュコードのエントリのValueIndexを更新する（BackSwap後の移動先を反映）。
        /// </summary>
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
