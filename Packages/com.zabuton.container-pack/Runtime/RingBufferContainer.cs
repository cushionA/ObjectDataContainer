using System;

namespace ODC.Runtime
{
    /// <summary>
    /// 固定サイズのリングバッファコンテナ。構造体データ用。
    /// バッファが満杯の場合、最も古いデータが自動的に上書きされる。
    /// コンストラクション後のヒープアロケーションなし。
    /// </summary>
    /// <typeparam name="T">格納するデータの型（値型）</typeparam>
    public class RingBufferContainer<T> where T : struct
    {
        private T[] _buffer;
        private int _head;
        private int _count;
        private int _capacity;

        /// <summary>現在の要素数</summary>
        public int Count => _count;

        /// <summary>バッファの容量</summary>
        public int Capacity => _capacity;

        /// <summary>バッファが満杯かどうか</summary>
        public bool IsFull => _count == _capacity;

        /// <summary>バッファが空かどうか</summary>
        public bool IsEmpty => _count == 0;

        /// <summary>
        /// コンストラクタ。指定された容量でリングバッファを初期化する。
        /// </summary>
        /// <param name="capacity">バッファの容量</param>
        public RingBufferContainer(int capacity)
        {
            _capacity = capacity;
            _buffer = new T[capacity];
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// 要素をバッファの末尾に追加する。バッファが満杯の場合、最も古い要素が上書きされる。
        /// </summary>
        /// <param name="item">追加する要素</param>
        public void Push(T item)
        {
            int writeIndex = (_head + _count) % _capacity;
            if (_count == _capacity)
            {
                _head = (_head + 1) % _capacity;
            }
            else
            {
                _count++;
            }
            _buffer[writeIndex] = item;
        }

        /// <summary>
        /// 最新の要素への参照を返す。
        /// </summary>
        /// <returns>最新の要素への参照</returns>
        /// <exception cref="InvalidOperationException">バッファが空の場合</exception>
        public ref T PeekLatest()
        {
            if (_count == 0)
                throw new InvalidOperationException("バッファが空です。");
            int index = (_head + _count - 1) % _capacity;
            return ref _buffer[index];
        }

        /// <summary>
        /// 最も古い要素への参照を返す。
        /// </summary>
        /// <returns>最も古い要素への参照</returns>
        /// <exception cref="InvalidOperationException">バッファが空の場合</exception>
        public ref T PeekOldest()
        {
            if (_count == 0)
                throw new InvalidOperationException("バッファが空です。");
            return ref _buffer[_head];
        }

        /// <summary>
        /// インデクサ。0が最も古い要素、Count-1が最新の要素。
        /// </summary>
        /// <param name="index">論理インデックス（0=最古、Count-1=最新）</param>
        /// <returns>要素への参照</returns>
        /// <exception cref="IndexOutOfRangeException">インデックスが範囲外の場合</exception>
        public ref T this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                    throw new IndexOutOfRangeException($"インデックス {index} は範囲外です。Count={_count}");
                return ref _buffer[(_head + index) % _capacity];
            }
        }

        /// <summary>
        /// バッファの内容を時系列順（古い→新しい）でSpanにコピーする。
        /// コマンド入力のパターンマッチング等、連続メモリが必要な場合に使用する。
        /// </summary>
        /// <param name="destination">コピー先のSpan。Countと同じかそれ以上の長さが必要。</param>
        /// <returns>コピーされた要素数</returns>
        /// <exception cref="ArgumentException">destinationがCount未満の場合</exception>
        public int CopyTo(Span<T> destination)
        {
            if (destination.Length < _count)
                throw new ArgumentException($"コピー先が不足しています。必要={_count}, 実際={destination.Length}");

            if (_count == 0) return 0;

            int tail = _head + _count;
            if (tail <= _capacity)
            {
                // ラップなし: 1ブロックコピー
                new ReadOnlySpan<T>(_buffer, _head, _count).CopyTo(destination);
            }
            else
            {
                // ラップあり: 2ブロックコピー
                int firstLen = _capacity - _head;
                new ReadOnlySpan<T>(_buffer, _head, firstLen).CopyTo(destination);
                new ReadOnlySpan<T>(_buffer, 0, _count - firstLen).CopyTo(destination.Slice(firstLen));
            }

            return _count;
        }

        /// <summary>
        /// バッファの内容をゼロコピーで2つのReadOnlySpanとして返す。
        /// first → second の順で読むと時系列順（古い→新しい）になる。
        /// ラップしていない場合、secondは空になる。
        /// </summary>
        /// <param name="first">前半部分（古い側）</param>
        /// <param name="second">後半部分（新しい側）。ラップなしの場合は空。</param>
        public void AsSpans(out ReadOnlySpan<T> first, out ReadOnlySpan<T> second)
        {
            if (_count == 0)
            {
                first = ReadOnlySpan<T>.Empty;
                second = ReadOnlySpan<T>.Empty;
                return;
            }

            int tail = _head + _count;
            if (tail <= _capacity)
            {
                // ラップなし
                first = new ReadOnlySpan<T>(_buffer, _head, _count);
                second = ReadOnlySpan<T>.Empty;
            }
            else
            {
                // ラップあり
                int firstLen = _capacity - _head;
                first = new ReadOnlySpan<T>(_buffer, _head, firstLen);
                second = new ReadOnlySpan<T>(_buffer, 0, _count - firstLen);
            }
        }

        /// <summary>
        /// バッファの全要素をクリアし、初期状態に戻す。
        /// </summary>
        public void Clear()
        {
            Array.Clear(_buffer, 0, _capacity);
            _head = 0;
            _count = 0;
        }
    }
}
