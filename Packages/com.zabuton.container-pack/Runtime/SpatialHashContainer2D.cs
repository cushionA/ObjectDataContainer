using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODC.Runtime
{
    /// <summary>
    /// XZ平面を使用した2D空間ハッシュコンテナ。
    /// GameObjectをキーとしてデータを格納し、Transform位置を自動追跡する。
    /// Update()で全要素の位置を再ハッシュし、QueryNeighborsで近傍検索を行う。
    /// </summary>
    /// <typeparam name="T">格納するデータ型（参照型のみ）</typeparam>
    public class SpatialHashContainer2D<T> : IDisposable where T : class
    {
        /// <summary>
        /// 要素データ。GameObjectへの参照、ユーザーデータ、現在のセルキー、ハッシュコードを保持する。
        /// </summary>
        private struct ElementData
        {
            public GameObject GameObject;
            public T Data;
            public int CellKey;
            public int HashCode;
        }

        /// <summary>
        /// セル内のリンクリストエントリ。要素インデックスと次のエントリへのインデックスを持つ。
        /// </summary>
        private struct CellEntry
        {
            public int ElementIndex;
            public int NextInCell;
        }

        private ElementData[] _elements;
        private T[] _dataCache;
        private int _activeCount;
        private int _maxCapacity;
        private float _cellSize;
        private float _inverseCellSize;

        // GameObjectハッシュ → インデックスのルックアップテーブル
        private int[] _buckets;
        private struct HashEntry
        {
            public int HashCode;
            public int ValueIndex;
            public int NextInBucket;
        }
        private HashEntry[] _hashEntries;
        private int _hashEntryCount;
        private int _bucketCount;
        private Stack<int> _freeHashEntries;

        // 空間グリッド: セルキー → CellEntryリンクリストの先頭
        private Dictionary<int, int> _cellHeads;
        private CellEntry[] _cellEntries;
        private int _cellEntryCount;
        private Stack<int> _freeCellEntries;

        /// <summary>
        /// アクティブな要素数を返す。
        /// </summary>
        public int Count => _activeCount;

        /// <summary>
        /// アクティブなデータのSpanを返す（T型データのみ）。
        /// </summary>
        public Span<T> ActiveDataSpan
        {
            get
            {
                for (int i = 0; i < _activeCount; i++)
                    _dataCache[i] = _elements[i].Data;
                return _dataCache.AsSpan(0, _activeCount);
            }
        }

        /// <summary>
        /// 2D空間ハッシュコンテナを生成する。
        /// </summary>
        /// <param name="cellSize">空間ハッシュのセルサイズ</param>
        /// <param name="maxCapacity">最大要素数</param>
        public SpatialHashContainer2D(float cellSize, int maxCapacity)
        {
            _cellSize = cellSize;
            _inverseCellSize = 1f / cellSize;
            _maxCapacity = maxCapacity;
            _activeCount = 0;

            _elements = new ElementData[maxCapacity];
            _dataCache = new T[maxCapacity];

            _bucketCount = NextPowerOfTwo(maxCapacity);
            _buckets = new int[_bucketCount];
            Array.Fill(_buckets, -1);
            _hashEntries = new HashEntry[maxCapacity];
            _hashEntryCount = 0;
            _freeHashEntries = new Stack<int>();

            _cellHeads = new Dictionary<int, int>();
            _cellEntries = new CellEntry[maxCapacity * 2];
            _cellEntryCount = 0;
            _freeCellEntries = new Stack<int>();
        }

        /// <summary>
        /// GameObjectとデータを追加する。位置はobj.transform.positionから読み取る。
        /// </summary>
        /// <param name="obj">追加するGameObject</param>
        /// <param name="data">関連データ</param>
        /// <returns>要素のインデックス</returns>
        public int Add(GameObject obj, T data)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            if (_activeCount >= _maxCapacity)
                throw new InvalidOperationException("コンテナが満杯です。");

            int hashCode = obj.GetHashCode() & 0x7FFFFFFF;
            if (TryGetIndexByHash(hashCode, out _))
                throw new InvalidOperationException("同じGameObjectが既に登録されています。");

            int index = _activeCount;
            Vector3 pos = obj.transform.position;
            int cellKey = GetCellKey(pos);

            _elements[index] = new ElementData
            {
                GameObject = obj,
                Data = data,
                CellKey = cellKey,
                HashCode = hashCode
            };
            _activeCount++;

            AddToHashTable(hashCode, index);
            AddToCellGrid(cellKey, index);

            return index;
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

            RemoveAtIndex(index);
            return true;
        }

        /// <summary>
        /// インデックス指定で要素をBackSwap方式で削除する。
        /// </summary>
        private void RemoveAtIndex(int index)
        {
            int lastIndex = _activeCount - 1;

            // セルグリッドから削除
            RemoveFromCellGrid(_elements[index].CellKey, index);

            // ハッシュテーブルから削除
            RemoveFromHashTable(_elements[index].HashCode, index);

            if (index < lastIndex)
            {
                // BackSwap: 最後尾を削除位置に移動
                var lastElement = _elements[lastIndex];

                // 最後尾のセルグリッドエントリを更新
                UpdateCellGridIndex(lastElement.CellKey, lastIndex, index);

                // 最後尾のハッシュテーブルエントリを更新
                UpdateHashTableIndex(lastElement.HashCode, lastIndex, index);

                _elements[index] = lastElement;
            }

            _elements[lastIndex] = default;
            _activeCount--;
        }

        /// <summary>
        /// 全要素のTransform位置を読み取り、セルキーが変わった場合に再ハッシュする。
        /// </summary>
        public void Update()
        {
            for (int i = _activeCount - 1; i >= 0; i--)
            {
                ref var element = ref _elements[i];
                if (element.GameObject == null)
                {
                    RemoveAtIndex(i);
                    continue;
                }

                Vector3 pos = element.GameObject.transform.position;
                int newCellKey = GetCellKey(pos);

                if (newCellKey != element.CellKey)
                {
                    RemoveFromCellGrid(element.CellKey, i);
                    AddToCellGrid(newCellKey, i);
                    element.CellKey = newCellKey;
                }
            }
        }

        /// <summary>
        /// 指定した中心座標と半径内にある要素を検索する（XZ平面のみ）。
        /// </summary>
        /// <param name="center">検索中心座標</param>
        /// <param name="radius">検索半径</param>
        /// <param name="results">結果を書き込むSpan</param>
        /// <returns>見つかった要素数</returns>
        public int QueryNeighbors(Vector3 center, float radius, Span<T> results)
        {
            float radiusSq = radius * radius;
            int written = 0;
            int maxResults = results.Length;

            int cellRange = Mathf.CeilToInt(radius * _inverseCellSize);
            int centerCellX = Mathf.FloorToInt(center.x * _inverseCellSize);
            int centerCellZ = Mathf.FloorToInt(center.z * _inverseCellSize);

            for (int cx = centerCellX - cellRange; cx <= centerCellX + cellRange; cx++)
            {
                for (int cz = centerCellZ - cellRange; cz <= centerCellZ + cellRange; cz++)
                {
                    int cellKey = cx * 73856093 ^ cz * 19349669;

                    if (!_cellHeads.TryGetValue(cellKey, out int entryIndex))
                        continue;

                    while (entryIndex >= 0)
                    {
                        ref var cellEntry = ref _cellEntries[entryIndex];
                        int elemIdx = cellEntry.ElementIndex;

                        if (elemIdx < _activeCount)
                        {
                            Vector3 pos = _elements[elemIdx].GameObject.transform.position;
                            float dx = pos.x - center.x;
                            float dz = pos.z - center.z;
                            float distSq = dx * dx + dz * dz;

                            if (distSq <= radiusSq)
                            {
                                if (written < maxResults)
                                {
                                    results[written] = _elements[elemIdx].Data;
                                    written++;
                                }
                            }
                        }

                        entryIndex = cellEntry.NextInCell;
                    }
                }
            }

            return written;
        }

        /// <summary>
        /// 指定した中心座標と半径内にある要素を検索し、距離も返す（XZ平面のみ）。
        /// </summary>
        /// <param name="center">検索中心座標</param>
        /// <param name="radius">検索半径</param>
        /// <param name="results">結果データを書き込むSpan</param>
        /// <param name="distances">距離を書き込むSpan</param>
        /// <returns>見つかった要素数</returns>
        public int QueryNeighbors(Vector3 center, float radius, Span<T> results, Span<float> distances)
        {
            float radiusSq = radius * radius;
            int written = 0;
            int maxResults = Math.Min(results.Length, distances.Length);

            int cellRange = Mathf.CeilToInt(radius * _inverseCellSize);
            int centerCellX = Mathf.FloorToInt(center.x * _inverseCellSize);
            int centerCellZ = Mathf.FloorToInt(center.z * _inverseCellSize);

            for (int cx = centerCellX - cellRange; cx <= centerCellX + cellRange; cx++)
            {
                for (int cz = centerCellZ - cellRange; cz <= centerCellZ + cellRange; cz++)
                {
                    int cellKey = cx * 73856093 ^ cz * 19349669;

                    if (!_cellHeads.TryGetValue(cellKey, out int entryIndex))
                        continue;

                    while (entryIndex >= 0)
                    {
                        ref var cellEntry = ref _cellEntries[entryIndex];
                        int elemIdx = cellEntry.ElementIndex;

                        if (elemIdx < _activeCount)
                        {
                            Vector3 pos = _elements[elemIdx].GameObject.transform.position;
                            float dx = pos.x - center.x;
                            float dz = pos.z - center.z;
                            float distSq = dx * dx + dz * dz;

                            if (distSq <= radiusSq)
                            {
                                if (written < maxResults)
                                {
                                    results[written] = _elements[elemIdx].Data;
                                    distances[written] = Mathf.Sqrt(distSq);
                                    written++;
                                }
                            }
                        }

                        entryIndex = cellEntry.NextInCell;
                    }
                }
            }

            return written;
        }

        /// <summary>
        /// GameObjectに関連付けられたデータを取得する。
        /// </summary>
        /// <param name="obj">検索するGameObject</param>
        /// <param name="data">見つかったデータ</param>
        /// <returns>見つかった場合true</returns>
        public bool TryGetValue(GameObject obj, out T data)
        {
            if (obj == null)
            {
                data = null;
                return false;
            }

            int hashCode = obj.GetHashCode() & 0x7FFFFFFF;
            if (TryGetIndexByHash(hashCode, out int index))
            {
                data = _elements[index].Data;
                return true;
            }

            data = null;
            return false;
        }

        /// <summary>
        /// 格納されたGameObjectの現在のTransform位置を返す。
        /// </summary>
        /// <param name="obj">位置を取得するGameObject</param>
        /// <returns>GameObjectのTransform位置</returns>
        public Vector3 GetPosition(GameObject obj)
        {
            return obj.transform.position;
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
                _elements[i] = default;

            _activeCount = 0;

            Array.Fill(_buckets, -1);
            _hashEntryCount = 0;
            _freeHashEntries.Clear();

            _cellHeads.Clear();
            _cellEntryCount = 0;
            _freeCellEntries.Clear();
        }

        /// <summary>
        /// コンテナを破棄し、リソースを解放する。
        /// </summary>
        public void Dispose()
        {
            Clear();
            _elements = null;
            _buckets = null;
            _hashEntries = null;
            _cellEntries = null;
            _cellHeads = null;
        }

        // ===== セルキー計算 =====

        /// <summary>
        /// XZ平面上の位置からセルキーを計算する。
        /// </summary>
        private int GetCellKey(Vector3 position)
        {
            int cellX = Mathf.FloorToInt(position.x * _inverseCellSize);
            int cellZ = Mathf.FloorToInt(position.z * _inverseCellSize);
            return cellX * 73856093 ^ cellZ * 19349669;
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

            _hashEntries[entryIndex] = new HashEntry
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

        // ===== セルグリッド操作 =====

        private void AddToCellGrid(int cellKey, int elementIndex)
        {
            int entryIndex;
            if (_freeCellEntries.Count > 0)
            {
                entryIndex = _freeCellEntries.Pop();
            }
            else
            {
                if (_cellEntryCount >= _cellEntries.Length)
                    Array.Resize(ref _cellEntries, _cellEntries.Length * 2);
                entryIndex = _cellEntryCount++;
            }

            int existingHead = -1;
            if (_cellHeads.TryGetValue(cellKey, out int head))
                existingHead = head;

            _cellEntries[entryIndex] = new CellEntry
            {
                ElementIndex = elementIndex,
                NextInCell = existingHead
            };
            _cellHeads[cellKey] = entryIndex;
        }

        private void RemoveFromCellGrid(int cellKey, int elementIndex)
        {
            if (!_cellHeads.TryGetValue(cellKey, out int entryIndex))
                return;

            int prevIndex = -1;

            while (entryIndex >= 0)
            {
                ref var entry = ref _cellEntries[entryIndex];
                if (entry.ElementIndex == elementIndex)
                {
                    if (prevIndex < 0)
                    {
                        if (entry.NextInCell < 0)
                            _cellHeads.Remove(cellKey);
                        else
                            _cellHeads[cellKey] = entry.NextInCell;
                    }
                    else
                    {
                        _cellEntries[prevIndex].NextInCell = entry.NextInCell;
                    }

                    _freeCellEntries.Push(entryIndex);
                    return;
                }
                prevIndex = entryIndex;
                entryIndex = entry.NextInCell;
            }
        }

        private void UpdateCellGridIndex(int cellKey, int oldElementIndex, int newElementIndex)
        {
            if (!_cellHeads.TryGetValue(cellKey, out int entryIndex))
                return;

            while (entryIndex >= 0)
            {
                ref var entry = ref _cellEntries[entryIndex];
                if (entry.ElementIndex == oldElementIndex)
                {
                    entry.ElementIndex = newElementIndex;
                    return;
                }
                entryIndex = entry.NextInCell;
            }
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
