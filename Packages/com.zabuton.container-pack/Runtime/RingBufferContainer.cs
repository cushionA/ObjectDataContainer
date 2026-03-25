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
