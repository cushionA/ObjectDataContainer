using System;
using System.Collections.Generic;
using UnityEngine;

namespace ODC.Runtime
{
    /// <summary>
    /// 2D空間ハッシュで使用する座標平面。
    /// </summary>
    public enum SpatialPlane2D
    {
        /// <summary>XZ平面（3Dゲームのトップダウン等）。position.x と position.z を使用。</summary>
        XZ,
        /// <summary>XY平面（2Dゲーム等）。position.x と position.y を使用。</summary>
        XY
    }

    /// <summary>
    /// 2D空間ハッシュコンテナ。XZ平面（3D）とXY平面（2D）の両方に対応。
    /// GameObjectまたはint hashをキーとしてデータを格納し、位置ベースの近傍検索を行う。
    /// GameObject版はTransform位置を自動追跡、int hash版は手動位置更新に対応。
    /// </summary>
    /// <typeparam name="T">格納するデータ型（参照型のみ）</typeparam>
    public class SpatialHashContainer2D<T> : IDisposable where T : class
    {
        /// <summary>
        /// 要素データ。GameObjectへの参照、ユーザーデータ、現在のセルキー、ハッシュコード、位置を保持する。
        /// </summary>
        private struct ElementData
        {
            public GameObject GameObject;
            public T Data;
            public int CellKey;
            public int HashCode;
            public Vector3 Position;
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
        private readonly bool _useXY;

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
        /// <param name="plane">使用する座標平面（デフォルト: XZ）</param>
        public SpatialHashContainer2D(float cellSize, int maxCapacity, SpatialPlane2D plane = SpatialPlane2D.XZ)
        {
            _cellSize = cellSize;
            _inverseCellSize = 1f / cellSize;
            _maxCapacity = maxCapacity;
            _activeCount = 0;
            _useXY = plane == SpatialPlane2D.XY;

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
        public int Add(GameObject obj, T data)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            Vector3 pos = obj.transform.position;
            int idx = Add(obj.GetHashCode(), data, pos);
            _elements[idx].GameObject = obj;
            return idx;
        }

        /// <summary>
        /// int hashとデータを指定位置で追加する。Transform不要。
        /// </summary>
        public int Add(int hash, T data, Vector3 position)
        {
            if (_activeCount >= _maxCapacity)
                throw new InvalidOperationException("コンテナが満杯です。");
            if (TryGetIndexByHash(hash, out _))
                throw new InvalidOperationException("同じハッシュが既に登録されています。");

            int index = _activeCount;
            int cellKey = GetCellKey(position);

            _elements[index] = new ElementData
            {
                Data = data,
                CellKey = cellKey,
                HashCode = hash,
                Position = position
            };
            _activeCount++;

            AddToHashTable(hash, index);
            AddToCellGrid(cellKey, index);

            return index;
        }

        /// <summary>
        /// GameObjectを削除する。
        /// </summary>
        public bool Remove(GameObject obj)
        {
            if (obj == null) return false;
            return Remove(obj.GetHashCode());
        }

        /// <summary>
        /// int hashを削除する。
        /// </summary>
        public bool Remove(int hash)
        {
            if (!TryGetIndexByHash(hash, out int index))
                return false;

            RemoveAtIndex(index);
            return true;
        }

        /// <summary>
        /// 個別の位置を手動更新する。Transform.positionの代替。
        /// </summary>
        public void UpdatePosition(int hash, Vector3 newPosition)
        {
            if (!TryGetIndexByHash(hash, out int index))
                return;

            ref var element = ref _elements[index];
            element.Position = newPosition;
            int newCellKey = GetCellKey(newPosition);

            if (newCellKey != element.CellKey)
            {
                RemoveFromCellGrid(element.CellKey, index);
                AddToCellGrid(newCellKey, index);
                element.CellKey = newCellKey;
            }
        }

        /// <summary>
        /// 全位置を外部配列から一括更新する。SoAアーキテクチャ向け。
        /// </summary>
        /// <param name="hashes">更新対象のハッシュ配列</param>
        /// <param name="positions">対応する新しい位置配列</param>
        public void UpdatePositions(ReadOnlySpan<int> hashes, ReadOnlySpan<Vector3> positions)
        {
            int count = Math.Min(hashes.Length, positions.Length);
            for (int i = 0; i < count; i++)
            {
                UpdatePosition(hashes[i], positions[i]);
            }
        }

        /// <summary>
        /// 全要素のTransform位置を読み取り、セルキーが変わった場合に再ハッシュする。
        /// GameObjectが設定されていない要素（int hash版で追加）はスキップされる。
        /// </summary>
        public void Update()
        {
            for (int i = _activeCount - 1; i >= 0; i--)
            {
                ref var element = ref _elements[i];
                if (element.GameObject == null)
                {
                    // int hash版で追加された要素はスキップ（手動更新が必要）
                    // ただしDataもnullなら破棄されたGameObjectとみなして削除
                    if (element.Data == null)
                    {
                        RemoveAtIndex(i);
                    }
                    continue;
                }

                Vector3 pos = element.GameObject.transform.position;
                element.Position = pos;
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
        /// 指定した中心座標と半径内にある要素を検索する。
        /// </summary>
        public int QueryNeighbors(Vector3 center, float radius, Span<T> results)
        {
            float radiusSq = radius * radius;
            int written = 0;
            int maxResults = results.Length;

            float centerB = _useXY ? center.y : center.z;

            int cellRange = Mathf.CeilToInt(radius * _inverseCellSize);
            int centerCellX = Mathf.FloorToInt(center.x * _inverseCellSize);
            int centerCellB = Mathf.FloorToInt(centerB * _inverseCellSize);

            for (int cx = centerCellX - cellRange; cx <= centerCellX + cellRange; cx++)
            {
                for (int cb = centerCellB - cellRange; cb <= centerCellB + cellRange; cb++)
                {
                    int cellKey = cx * 73856093 ^ cb * 19349669;

                    if (!_cellHeads.TryGetValue(cellKey, out int entryIndex))
                        continue;

                    while (entryIndex >= 0)
                    {
                        ref var cellEntry = ref _cellEntries[entryIndex];
                        int elemIdx = cellEntry.ElementIndex;

                        if (elemIdx < _activeCount)
                        {
                            Vector3 pos = _elements[elemIdx].Position;
                            float dx = pos.x - center.x;
                            float db = (_useXY ? pos.y : pos.z) - centerB;
                            float distSq = dx * dx + db * db;

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
        /// 指定した中心座標と半径内にある要素を検索し、距離も返す。
        /// </summary>
        public int QueryNeighbors(Vector3 center, float radius, Span<T> results, Span<float> distances)
        {
            float radiusSq = radius * radius;
            int written = 0;
            int maxResults = Math.Min(results.Length, distances.Length);

            float centerB = _useXY ? center.y : center.z;

            int cellRange = Mathf.CeilToInt(radius * _inverseCellSize);
            int centerCellX = Mathf.FloorToInt(center.x * _inverseCellSize);
            int centerCellB = Mathf.FloorToInt(centerB * _inverseCellSize);

            for (int cx = centerCellX - cellRange; cx <= centerCellX + cellRange; cx++)
            {
                for (int cb = centerCellB - cellRange; cb <= centerCellB + cellRange; cb++)
                {
                    int cellKey = cx * 73856093 ^ cb * 19349669;

                    if (!_cellHeads.TryGetValue(cellKey, out int entryIndex))
                        continue;

                    while (entryIndex >= 0)
                    {
                        ref var cellEntry = ref _cellEntries[entryIndex];
                        int elemIdx = cellEntry.ElementIndex;

                        if (elemIdx < _activeCount)
                        {
                            Vector3 pos = _elements[elemIdx].Position;
                            float dx = pos.x - center.x;
                            float db = (_useXY ? pos.y : pos.z) - centerB;
                            float distSq = dx * dx + db * db;

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
        public bool TryGetValue(GameObject obj, out T data)
        {
            if (obj == null)
            {
                data = null;
                return false;
            }
            return TryGetValue(obj.GetHashCode(), out data);
        }

        /// <summary>
        /// int hashに関連付けられたデータを取得する。
        /// </summary>
        public bool TryGetValue(int hash, out T data)
        {
            if (TryGetIndexByHash(hash, out int index))
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
        public Vector3 GetPosition(GameObject obj)
        {
            return obj.transform.position;
        }

        /// <summary>
        /// 格納された要素の位置を返す（int hash版）。
        /// </summary>
        public Vector3 GetPosition(int hash)
        {
            if (TryGetIndexByHash(hash, out int index))
                return _elements[index].Position;

            throw new KeyNotFoundException("指定されたハッシュはコンテナに存在しません。");
        }

        /// <summary>
        /// 指定したGameObjectがコンテナに含まれているか確認する。
        /// </summary>
        public bool ContainsKey(GameObject obj)
        {
            if (obj == null) return false;
            return ContainsKey(obj.GetHashCode());
        }

        /// <summary>
        /// 指定したint hashがコンテナに含まれているか確認する。
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

        // ===== 内部ヘルパー =====

        private void RemoveAtIndex(int index)
        {
            int lastIndex = _activeCount - 1;

            RemoveFromCellGrid(_elements[index].CellKey, index);
            RemoveFromHashTable(_elements[index].HashCode, index);

            if (index < lastIndex)
            {
                var lastElement = _elements[lastIndex];

                UpdateCellGridIndex(lastElement.CellKey, lastIndex, index);
                UpdateHashTableIndex(lastElement.HashCode, lastIndex, index);

                _elements[index] = lastElement;
            }

            _elements[lastIndex] = default;
            _activeCount--;
        }

        // ===== セルキー計算 =====

        private int GetCellKey(Vector3 position)
        {
            int cellA = Mathf.FloorToInt(position.x * _inverseCellSize);
            int cellB = Mathf.FloorToInt((_useXY ? position.y : position.z) * _inverseCellSize);
            return cellA * 73856093 ^ cellB * 19349669;
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
