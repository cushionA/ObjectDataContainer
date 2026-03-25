using System;

namespace ODC.Runtime
{
    /// <summary>
    /// 陣営間の関係を管理するコンテナ。
    /// 最大8陣営の関係をO(1)で参照・更新できる。
    /// 混乱魔法等の一時的な関係上書きをスタック管理で実現。
    /// </summary>
    public class FactionRelationContainer : IDisposable
    {
        /// <summary>陣営間関係を表す列挙型</summary>
        public enum Relation : byte
        {
            Neutral = 0,
            Allied = 1,
            Hostile = 2,
        }

        /// <summary>一時関係上書きエントリ</summary>
        private struct TempRelation
        {
            public int FactionA;
            public int FactionB;
            public Relation OriginalAB;  // A→B の元関係
            public Relation OriginalBA;  // B→A の元関係
            public float RemainingTime;
        }

        /// <summary>最大陣営数</summary>
        public const int MaxFactions = 8;

        private Relation[] _relations;   // MaxFactions x MaxFactions = 64 entries
        private int _factionCount;

        private TempRelation[] _tempRelations;
        private int _tempCount;
        private int _maxTempRelations;

        /// <summary>現在の陣営数</summary>
        public int FactionCount => _factionCount;

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        /// <param name="maxTempRelations">最大一時関係数（デフォルト: 16）</param>
        public FactionRelationContainer(int maxTempRelations = 16)
        {
            _relations = new Relation[MaxFactions * MaxFactions];
            _factionCount = 0;
            _maxTempRelations = maxTempRelations;
            _tempRelations = new TempRelation[maxTempRelations];
            _tempCount = 0;
        }

        /// <summary>
        /// 新しい陣営を登録する。登録順にID（0〜7）が割り当てられる。
        /// </summary>
        /// <returns>割り当てられた陣営ID</returns>
        /// <exception cref="InvalidOperationException">陣営数が上限に達した場合</exception>
        public int RegisterFaction()
        {
            if (_factionCount >= MaxFactions)
                throw new InvalidOperationException($"陣営数が上限（{MaxFactions}）に達しています。");

            int id = _factionCount;
            _factionCount++;
            return id;
        }

        /// <summary>
        /// 2陣営間の関係を取得。O(1)。
        /// </summary>
        /// <param name="factionA">陣営A ID</param>
        /// <param name="factionB">陣営B ID</param>
        /// <returns>関係</returns>
        public Relation Get(int factionA, int factionB)
        {
            ValidateFaction(factionA);
            ValidateFaction(factionB);
            if (factionA == factionB)
                return Relation.Allied;
            return _relations[factionA * MaxFactions + factionB];
        }

        /// <summary>
        /// 2陣営間の関係を設定（対称性を自動保証）。O(1)。
        /// </summary>
        /// <param name="factionA">陣営A ID</param>
        /// <param name="factionB">陣営B ID</param>
        /// <param name="relation">設定する関係</param>
        public void Set(int factionA, int factionB, Relation relation)
        {
            ValidateFaction(factionA);
            ValidateFaction(factionB);
            if (factionA == factionB) return;

            _relations[factionA * MaxFactions + factionB] = relation;
            _relations[factionB * MaxFactions + factionA] = relation;
        }

        /// <summary>
        /// 一方向のみ関係を設定（非対称）。O(1)。
        /// </summary>
        public void SetOneWay(int factionA, int factionB, Relation relation)
        {
            ValidateFaction(factionA);
            ValidateFaction(factionB);
            if (factionA == factionB) return;

            _relations[factionA * MaxFactions + factionB] = relation;
        }

        /// <summary>
        /// 一時的な関係上書き（混乱魔法用）。指定秒数後にTickで元に戻る。
        /// 双方向（A→B, B→A）を同時に上書きする。SetOneWayで非対称関係を設定していた場合も
        /// 両方向が同じ関係に上書きされる点に注意。期限切れ時は元の非対称関係に正しく復元される。
        /// </summary>
        /// <param name="factionA">陣営A ID</param>
        /// <param name="factionB">陣営B ID</param>
        /// <param name="relation">一時的に設定する関係</param>
        /// <param name="duration">持続時間（秒）</param>
        public void SetTemporary(int factionA, int factionB, Relation relation, float duration)
        {
            ValidateFaction(factionA);
            ValidateFaction(factionB);
            if (factionA == factionB) return;

            // 既存の一時関係を検索（二重適用でオリジナルを失わないようにする）
            for (int i = 0; i < _tempCount; i++)
            {
                ref var existing = ref _tempRelations[i];
                if ((existing.FactionA == factionA && existing.FactionB == factionB) ||
                    (existing.FactionA == factionB && existing.FactionB == factionA))
                {
                    // 既存エントリを更新（OriginalAB/BAは保持）
                    existing.RemainingTime = duration;
                    _relations[factionA * MaxFactions + factionB] = relation;
                    _relations[factionB * MaxFactions + factionA] = relation;
                    return;
                }
            }

            if (_tempCount >= _maxTempRelations)
                throw new InvalidOperationException("一時関係数が上限に達しています。");

            // 元の関係を双方向保存
            Relation originalAB = _relations[factionA * MaxFactions + factionB];
            Relation originalBA = _relations[factionB * MaxFactions + factionA];

            _tempRelations[_tempCount] = new TempRelation
            {
                FactionA = factionA,
                FactionB = factionB,
                OriginalAB = originalAB,
                OriginalBA = originalBA,
                RemainingTime = duration
            };
            _tempCount++;

            // 対称的に上書き
            _relations[factionA * MaxFactions + factionB] = relation;
            _relations[factionB * MaxFactions + factionA] = relation;
        }

        /// <summary>
        /// 指定陣営が敵対する全陣営リストをSpanに書き込む。O(N)。
        /// </summary>
        /// <param name="faction">対象の陣営ID</param>
        /// <param name="results">結果を書き込むSpan</param>
        /// <returns>書き込まれた件数</returns>
        public int GetHostile(int faction, Span<int> results)
        {
            ValidateFaction(faction);
            int count = 0;
            for (int i = 0; i < _factionCount && count < results.Length; i++)
            {
                if (i != faction && _relations[faction * MaxFactions + i] == Relation.Hostile)
                {
                    results[count] = i;
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 指定陣営が同盟する全陣営リストをSpanに書き込む。O(N)。
        /// </summary>
        /// <param name="faction">対象の陣営ID</param>
        /// <param name="results">結果を書き込むSpan</param>
        /// <returns>書き込まれた件数</returns>
        public int GetAllied(int faction, Span<int> results)
        {
            ValidateFaction(faction);
            int count = 0;
            for (int i = 0; i < _factionCount && count < results.Length; i++)
            {
                if (i != faction && _relations[faction * MaxFactions + i] == Relation.Allied)
                {
                    results[count] = i;
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 一時関係の時間を更新する。期限切れは元の関係に戻す。
        /// </summary>
        /// <param name="deltaTime">経過時間（秒）</param>
        public void Tick(float deltaTime)
        {
            int i = _tempCount - 1;
            while (i >= 0)
            {
                _tempRelations[i].RemainingTime -= deltaTime;
                if (_tempRelations[i].RemainingTime <= 0f)
                {
                    // 元の関係を双方向復元
                    int a = _tempRelations[i].FactionA;
                    int b = _tempRelations[i].FactionB;

                    _relations[a * MaxFactions + b] = _tempRelations[i].OriginalAB;
                    _relations[b * MaxFactions + a] = _tempRelations[i].OriginalBA;

                    // BackSwap削除
                    int lastIndex = _tempCount - 1;
                    if (i != lastIndex)
                    {
                        _tempRelations[i] = _tempRelations[lastIndex];
                    }
                    _tempRelations[lastIndex] = default;
                    _tempCount--;
                    // BackSwapで新要素が来た可能性 → iをデクリメントしない
                }
                else
                {
                    i--;
                }
            }
        }

        /// <summary>
        /// コンテナの全データをクリアする。
        /// </summary>
        public void Clear()
        {
            Array.Clear(_relations, 0, _relations.Length);
            _factionCount = 0;
            _tempCount = 0;
        }

        /// <summary>
        /// リソースを解放する。
        /// </summary>
        public void Dispose()
        {
            Clear();
            _relations = null;
            _tempRelations = null;
        }

        private void ValidateFaction(int faction)
        {
            if (faction < 0 || faction >= _factionCount)
                throw new ArgumentOutOfRangeException(nameof(faction), $"陣営ID {faction} は範囲外です。(登録数: {_factionCount})");
        }
    }
}
