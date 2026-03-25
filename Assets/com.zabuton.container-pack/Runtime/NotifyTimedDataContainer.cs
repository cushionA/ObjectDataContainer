using System;
using UnityEngine;

namespace ODC.Runtime
{
    /// <summary>
    /// TimedDataContainerを拡張し、要素の期限切れ時にコールバック通知を行うコンテナ。
    /// </summary>
    /// <typeparam name="T">格納するデータの型（参照型）</typeparam>
    public class NotifyTimedDataContainer<T> : TimedDataContainer<T> where T : class
    {
        /// <summary>
        /// コンストラクタ。指定された最大容量でコンテナを初期化する。
        /// </summary>
        /// <param name="maxCapacity">最大容量</param>
        public NotifyTimedDataContainer(int maxCapacity) : base(maxCapacity) { }

        /// <summary>
        /// 全タイマーを更新し、期限切れの要素をBackSwapで削除する。
        /// 期限切れ時にコールバックを呼び出す。
        /// </summary>
        /// <param name="deltaTime">経過時間（秒）</param>
        /// <param name="onExpired">期限切れ時に呼ばれるコールバック</param>
        /// <returns>削除された要素数</returns>
        public int Update(float deltaTime, Action<T> onExpired)
        {
            int removed = 0;
            for (int i = ActiveCount - 1; i >= 0; i--)
            {
                DecrementTimer(i, deltaTime);
                if (GetRemainingTime(i) <= 0f)
                {
                    onExpired?.Invoke(GetByIndex(i));
                    RemoveAtIndex(i);
                    removed++;
                }
            }
            return removed;
        }
    }
}
