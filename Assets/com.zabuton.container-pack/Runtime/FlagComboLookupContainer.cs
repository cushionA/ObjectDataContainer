using System;

namespace ODC.Runtime
{
    /// <summary>
    /// ulong ビットフラグの組み合わせをキーに、事前登録したエフェクトをO(1)で取得する
    /// 読み取り専用テーブルコンテナ。
    /// 構築時 O(n)、検索時 O(n)（登録数nは通常小さい）。
    /// TEffect は unmanaged 構造体で自由に定義する。
    /// </summary>
    /// <typeparam name="TEffect">エフェクトの型（unmanaged構造体）</typeparam>
    public class FlagComboLookupContainer<TEffect> : IDisposable where TEffect : unmanaged
    {
        private struct ComboEntry
        {
            public ulong FlagCombo;
            public TEffect Effect;
        }

        private ComboEntry[] _entries;
        private int _count;

        /// <summary>登録済みコンボ数</summary>
        public int Count => _count;

        private FlagComboLookupContainer() { }

        /// <summary>
        /// (フラグ組み合わせ, エフェクト) ペアリストからテーブルを構築する。
        /// </summary>
        /// <param name="combos">フラグ組み合わせ配列</param>
        /// <param name="effects">対応するエフェクト配列</param>
        /// <returns>構築されたルックアップコンテナ</returns>
        public static FlagComboLookupContainer<TEffect> Build(ulong[] combos, TEffect[] effects)
        {
            if (combos == null || effects == null)
                throw new ArgumentNullException("combos と effects は null にできません。");
            if (combos.Length != effects.Length)
                throw new ArgumentException("combos と effects の長さが一致しません。");

            var container = new FlagComboLookupContainer<TEffect>();
            int n = combos.Length;
            container._entries = new ComboEntry[n];
            container._count = n;

            for (int i = 0; i < n; i++)
            {
                container._entries[i] = new ComboEntry
                {
                    FlagCombo = combos[i],
                    Effect = effects[i]
                };
            }

            return container;
        }

        /// <summary>
        /// 入力フラグに対してマッチする最初のエフェクトを返す。
        /// マッチ条件: 登録されたcomboの全ビットがinputFlagsに含まれている。
        /// </summary>
        /// <param name="inputFlags">入力フラグ</param>
        /// <param name="effect">マッチしたエフェクト</param>
        /// <returns>マッチした場合true</returns>
        public bool TryGet(ulong inputFlags, out TEffect effect)
        {
            for (int i = 0; i < _count; i++)
            {
                if ((_entries[i].FlagCombo & inputFlags) == _entries[i].FlagCombo)
                {
                    effect = _entries[i].Effect;
                    return true;
                }
            }

            effect = default;
            return false;
        }

        /// <summary>
        /// 入力フラグにマッチする全エフェクトをSpanに書き込む。
        /// </summary>
        /// <param name="inputFlags">入力フラグ</param>
        /// <param name="results">結果を書き込むSpan</param>
        /// <returns>マッチした件数</returns>
        public int GetAll(ulong inputFlags, Span<TEffect> results)
        {
            int count = 0;
            for (int i = 0; i < _count && count < results.Length; i++)
            {
                if ((_entries[i].FlagCombo & inputFlags) == _entries[i].FlagCombo)
                {
                    results[count] = _entries[i].Effect;
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// リソースを解放する。
        /// </summary>
        public void Dispose()
        {
            _entries = null;
            _count = 0;
        }
    }
}
