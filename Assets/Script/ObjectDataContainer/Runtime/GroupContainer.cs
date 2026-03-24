using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODC.Runtime
{
    /// <summary>
    /// グループベースのコンテナ。GameObjectを名前付きグループで管理する。
    /// グループ間の移動や、グループ単位の一括操作をサポートする。
    /// </summary>
    /// <typeparam name="T">格納するデータの型（参照型）</typeparam>
    public class GroupContainer<T> : IDisposable where T : class
    {
        /// <summary>
        /// 要素データ構造体。GameObject、データ、所属グループIDを保持する。
        /// </summary>
        private struct ElementData
        {
            public GameObject GameObject;
            public T Data;
            public int GroupId;
            public int HashCode;
        }

        /// <summary>
        /// グループ情報構造体。グループ内の要素数を追跡する。
        /// </summary>
        private struct GroupInfo
        {
            public int Count;
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

        /// <summary>最大グループ数</summary>
        private const int MaxGroups = 64;

        private ElementData[] _elements;
        private int _activeCount;
        private int _maxCapacity;

        private Dictionary<string, int> _groupNameToId;
        private string[] _groupIdToName;
        private GroupInfo[] _groups;
        private int _groupCount;

        // ハッシュテーブル（GameObjectルックアップ用）
        private int[] _buckets;
        private HashEntry[] _hashEntries;
        private int _hashEntryCount;
        private int _bucketCount;
        private Stack<int> _freeHashEntries;

        /// <summary>現在のアクティブ要素数</summary>
        public int Count => _activeCount;

        /// <summary>
        /// コンストラクタ。指定された最大容量とグループ名でコンテナを初期化する。
        /// グループはコンストラクタ時に事前定義される。
        /// </summary>
        /// <param name="maxCapacity">最大容量</param>
        /// <param name="groupNames">事前定義するグループ名の配列</param>
        /// <exception cref="ArgumentException">グループ数が最大値を超える場合</exception>
        public GroupContainer(int maxCapacity, string[] groupNames)
        {
            if (groupNames == null)
                throw new ArgumentNullException(nameof(groupNames));
            if (groupNames.Length > MaxGroups)
                throw new ArgumentException($"グループ数は最大{MaxGroups}までです。");

            _maxCapacity = maxCapacity;
            _bucketCount = GetPrimeBucketCount(maxCapacity);
            _elements = new ElementData[maxCapacity];
            _activeCount = 0;

            _groupNameToId = new Dictionary<string, int>(groupNames.Length);
            _groupIdToName = new string[groupNames.Length];
            _groups = new GroupInfo[MaxGroups];
            _groupCount = groupNames.Length;

            for (int i = 0; i < groupNames.Length; i++)
            {
                _groupNameToId[groupNames[i]] = i;
                _groupIdToName[i] = groupNames[i];
            }

            _buckets = new int[_bucketCount];
            _hashEntries = new HashEntry[maxCapacity];
            _hashEntryCount = 0;
            _freeHashEntries = new Stack<int>();

            for (int i = 0; i < _bucketCount; i++)
                _buckets[i] = -1;
        }

        /// <summary>
        /// GameObjectとデータを指定グループに追加する。
        /// </summary>
        /// <param name="obj">キーとなるGameObject</param>
        /// <param name="data">格納するデータ</param>
        /// <param name="groupName">所属グループ名</param>
        /// <returns>データが格納されたインデックス</returns>
        /// <exception cref="ArgumentNullException">objがnullの場合</exception>
        /// <exception cref="InvalidOperationException">コンテナが満杯の場合</exception>
        /// <exception cref="KeyNotFoundException">指定グループが存在しない場合</exception>
        public int Add(GameObject obj, T data, string groupName)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            if (_activeCount >= _maxCapacity)
                throw new InvalidOperationException("コンテナが満杯です。");
            if (!_groupNameToId.TryGetValue(groupName, out int groupId))
                throw new KeyNotFoundException($"グループ '{groupName}' は存在しません。");

            int hashCode = obj.GetInstanceID();
            if (TryGetIndexByHash(hashCode, out _))
                throw new InvalidOperationException("同じGameObjectが既に登録されています。");

            int dataIndex = _activeCount;

            _elements[dataIndex] = new ElementData
            {
                GameObject = obj,
                Data = data,
                GroupId = groupId,
                HashCode = hashCode
            };
            _activeCount++;

            _groups[groupId].Count++;

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

            int groupId = _elements[dataIndex].GroupId;
            _groups[groupId].Count--;

            BackSwapRemove(dataIndex);
            return true;
        }

        /// <summary>
        /// 指定されたGameObjectを別のグループに移動する。
        /// データの物理的な移動は行わず、グループIDのみを更新する。
        /// </summary>
        /// <param name="obj">移動対象のGameObject</param>
        /// <param name="newGroup">移動先のグループ名</param>
        /// <returns>移動に成功した場合true</returns>
        public bool MoveToGroup(GameObject obj, string newGroup)
        {
            if (obj == null)
                return false;
            if (!_groupNameToId.TryGetValue(newGroup, out int newGroupId))
                return false;

            int hashCode = obj.GetInstanceID();
            if (!TryGetIndexByHash(hashCode, out int dataIndex))
                return false;

            int oldGroupId = _elements[dataIndex].GroupId;
            _groups[oldGroupId].Count--;
            _groups[newGroupId].Count++;

            var element = _elements[dataIndex];
            element.GroupId = newGroupId;
            _elements[dataIndex] = element;

            return true;
        }

        /// <summary>
        /// 指定されたGameObjectのデータとグループ名を取得する。
        /// </summary>
        /// <param name="obj">検索対象のGameObject</param>
        /// <param name="data">取得されたデータ</param>
        /// <param name="groupName">所属グループ名</param>
        /// <returns>見つかった場合true</returns>
        public bool TryGetValue(GameObject obj, out T data, out string groupName)
        {
            if (obj != null)
            {
                int hashCode = obj.GetInstanceID();
                if (TryGetIndexByHash(hashCode, out int dataIndex))
                {
                    data = _elements[dataIndex].Data;
                    groupName = _groupIdToName[_elements[dataIndex].GroupId];
                    return true;
                }
            }

            data = null;
            groupName = null;
            return false;
        }

        /// <summary>
        /// 指定グループのデータをSpanに書き込む。線形スキャンで該当グループの要素を検索する。
        /// </summary>
        /// <param name="groupName">グループ名</param>
        /// <param name="results">結果を格納するSpan</param>
        /// <returns>書き込まれた要素数</returns>
        public int GetGroup(string groupName, Span<T> results)
        {
            if (!_groupNameToId.TryGetValue(groupName, out int groupId))
                return 0;

            int count = 0;
            for (int i = 0; i < _activeCount && count < results.Length; i++)
            {
                if (_elements[i].GroupId == groupId)
                {
                    results[count++] = _elements[i].Data;
                }
            }
            return count;
        }

        /// <summary>
        /// 指定グループのGameObjectをSpanに書き込む。
        /// </summary>
        /// <param name="groupName">グループ名</param>
        /// <param name="results">結果を格納するSpan</param>
        /// <returns>書き込まれた要素数</returns>
        public int GetGroupObjects(string groupName, Span<GameObject> results)
        {
            if (!_groupNameToId.TryGetValue(groupName, out int groupId))
                return 0;

            int count = 0;
            for (int i = 0; i < _activeCount && count < results.Length; i++)
            {
                if (_elements[i].GroupId == groupId)
                {
                    results[count++] = _elements[i].GameObject;
                }
            }
            return count;
        }

        /// <summary>
        /// 指定グループの要素数を取得する。
        /// </summary>
        /// <param name="groupName">グループ名</param>
        /// <returns>グループ内の要素数</returns>
        public int GetGroupCount(string groupName)
        {
            if (!_groupNameToId.TryGetValue(groupName, out int groupId))
                return 0;
            return _groups[groupId].Count;
        }

        /// <summary>
        /// 指定されたグループ名が定義されているか確認する。
        /// </summary>
        /// <param name="groupName">確認するグループ名</param>
        /// <returns>グループが存在する場合true</returns>
        public bool GroupExists(string groupName)
        {
            return _groupNameToId.ContainsKey(groupName);
        }

        /// <summary>
        /// 指定グループの全要素に対してアクションを実行する。
        /// </summary>
        /// <param name="groupName">グループ名</param>
        /// <param name="action">各要素に対して実行するアクション</param>
        public void ForEachInGroup(string groupName, Action<GameObject, T> action)
        {
            if (!_groupNameToId.TryGetValue(groupName, out int groupId))
                return;
            if (action == null)
                return;

            for (int i = 0; i < _activeCount; i++)
            {
                if (_elements[i].GroupId == groupId)
                {
                    action(_elements[i].GameObject, _elements[i].Data);
                }
            }
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
        /// コンテナの全要素をクリアする。グループ定義は維持される。
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _activeCount; i++)
            {
                _elements[i] = default;
            }

            _activeCount = 0;
            _hashEntryCount = 0;
            _freeHashEntries.Clear();

            for (int i = 0; i < _groupCount; i++)
                _groups[i].Count = 0;

            for (int i = 0; i < _bucketCount; i++)
                _buckets[i] = -1;
        }

        /// <summary>
        /// リソースを解放する（マネージドメモリのみ）。
        /// </summary>
        public void Dispose()
        {
            Clear();
            _elements = null;
            _buckets = null;
            _hashEntries = null;
            _freeHashEntries = null;
            _groupNameToId = null;
            _groupIdToName = null;
            _groups = null;
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
            int removedHash = _elements[dataIndex].HashCode;
            int lastIndex = _activeCount - 1;

            if (dataIndex != lastIndex)
            {
                int movedHash = _elements[lastIndex].HashCode;

                _elements[dataIndex] = _elements[lastIndex];

                // 移動した要素のハッシュエントリのValueIndexを更新
                UpdateEntryDataIndex(movedHash, dataIndex);
            }

            _elements[lastIndex] = default;
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
