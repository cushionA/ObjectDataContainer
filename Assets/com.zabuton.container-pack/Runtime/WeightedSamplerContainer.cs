using System;

namespace ODC.Runtime
{
    /// <summary>
    /// Walker's Alias Method による O(1) 重み付きサンプリングコンテナ。
    /// 構築時に確率テーブルを事前計算し、以降のサンプリングをO(1)に。
    /// Build後は不変（Add/Removeなし）。
    /// </summary>
    /// <typeparam name="T">サンプリング対象の型（unmanaged構造体）</typeparam>
    public class WeightedSamplerContainer<T> : IDisposable where T : unmanaged
    {
        private T[] _items;
        private float[] _probabilities; // Alias Methodの確率テーブル
        private int[] _aliases;         // Alias Methodのエイリアステーブル
        private int _count;
        private uint _rngState;

        /// <summary>アイテム数</summary>
        public int Count => _count;

        private WeightedSamplerContainer() { }

        /// <summary>
        /// (値, 重み) のリストからサンプラーを構築する。O(n)。
        /// Vose's Alias Method を使用。
        /// </summary>
        /// <param name="items">サンプリング対象のアイテム配列</param>
        /// <param name="weights">各アイテムの重み配列（正の値）</param>
        /// <param name="seed">乱数シード</param>
        /// <returns>構築されたサンプラー</returns>
        public static WeightedSamplerContainer<T> Build(T[] items, float[] weights, uint seed = 12345)
        {
            if (items == null || weights == null)
                throw new ArgumentNullException("items と weights は null にできません。");
            if (items.Length != weights.Length)
                throw new ArgumentException("items と weights の長さが一致しません。");
            if (items.Length == 0)
                throw new ArgumentException("空の配列からはビルドできません。");

            int n = items.Length;
            var sampler = new WeightedSamplerContainer<T>();
            sampler._count = n;
            sampler._items = new T[n];
            sampler._probabilities = new float[n];
            sampler._aliases = new int[n];
            sampler._rngState = seed != 0 ? seed : 1;

            Array.Copy(items, sampler._items, n);

            // 重みの合計を計算
            float totalWeight = 0f;
            for (int i = 0; i < n; i++)
            {
                if (weights[i] < 0f)
                    throw new ArgumentException($"重みは非負である必要があります。index={i}, weight={weights[i]}");
                totalWeight += weights[i];
            }

            if (totalWeight <= 0f)
                throw new ArgumentException("重みの合計が0以下です。");

            // 正規化された確率を計算（各確率 * n）
            float[] scaledProbs = new float[n];
            for (int i = 0; i < n; i++)
            {
                scaledProbs[i] = (weights[i] / totalWeight) * n;
            }

            // Vose's algorithm: small/large ワークリスト
            int[] small = new int[n];
            int[] large = new int[n];
            int smallCount = 0;
            int largeCount = 0;

            for (int i = 0; i < n; i++)
            {
                if (scaledProbs[i] < 1f)
                    small[smallCount++] = i;
                else
                    large[largeCount++] = i;
            }

            while (smallCount > 0 && largeCount > 0)
            {
                int s = small[--smallCount];
                int l = large[--largeCount];

                sampler._probabilities[s] = scaledProbs[s];
                sampler._aliases[s] = l;

                scaledProbs[l] = (scaledProbs[l] + scaledProbs[s]) - 1f;

                if (scaledProbs[l] < 1f)
                    small[smallCount++] = l;
                else
                    large[largeCount++] = l;
            }

            // 残りのエントリ（浮動小数点誤差によるもの）
            while (largeCount > 0)
            {
                int l = large[--largeCount];
                sampler._probabilities[l] = 1f;
                sampler._aliases[l] = l;
            }

            while (smallCount > 0)
            {
                int s = small[--smallCount];
                sampler._probabilities[s] = 1f;
                sampler._aliases[s] = s;
            }

            return sampler;
        }

        /// <summary>
        /// 重み付き確率に従い1要素をサンプリング。O(1)。
        /// </summary>
        /// <returns>サンプリングされた要素</returns>
        public T Sample()
        {
            int i = NextInt(_count);
            float p = NextFloat();
            return p < _probabilities[i] ? _items[i] : _items[_aliases[i]];
        }

        /// <summary>
        /// 重複なしでk個サンプリング。O(k)（リジェクションサンプリング）。
        /// k がアイテム数に近い場合は効率が低下する。
        /// </summary>
        /// <param name="results">結果を書き込むSpan</param>
        /// <param name="k">サンプリング数</param>
        /// <returns>実際にサンプリングされた件数</returns>
        public int SampleDistinct(Span<T> results, int k)
        {
            if (k > _count)
                throw new ArgumentException($"k ({k}) がアイテム数 ({_count}) を超えています。");
            if (k > results.Length)
                throw new ArgumentException($"k ({k}) が results の長さ ({results.Length}) を超えています。");

            if (k == _count)
            {
                // 全件返す
                for (int i = 0; i < _count; i++)
                    results[i] = _items[i];
                return k;
            }

            // Fisher-Yatesシャッフル方式: 一時インデックス配列でリジェクションなし
            // stackallocで GCアロケーションを回避（_count <= 256 まで）
            Span<int> indices = _count <= 256
                ? stackalloc int[_count]
                : new int[_count];
            for (int i = 0; i < _count; i++)
                indices[i] = i;

            int picked = 0;
            for (int i = _count - 1; i >= _count - k; i--)
            {
                int j = NextInt(i + 1);
                // swap
                int tmp = indices[i];
                indices[i] = indices[j];
                indices[j] = tmp;

                results[picked] = _items[indices[i]];
                picked++;
            }

            return picked;
        }

        /// <summary>
        /// リソースを解放する。
        /// </summary>
        public void Dispose()
        {
            _items = null;
            _probabilities = null;
            _aliases = null;
            _count = 0;
        }

        // =============================================
        // 内部RNG (xorshift32)
        // =============================================

        private uint Xorshift32()
        {
            uint x = _rngState;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _rngState = x;
            return x;
        }

        private int NextInt(int max)
        {
            // Rejection sampling でmodulo biasを排除
            uint umax = (uint)max;
            uint threshold = (uint)(-(int)umax) % umax; // = (2^32 - max) % max
            uint r;
            do
            {
                r = Xorshift32();
            } while (r < threshold);
            return (int)(r % umax);
        }

        private float NextFloat()
        {
            return (Xorshift32() & 0x7FFFFF) / (float)0x800000; // [0, 1)
        }
    }
}
