using UnityEngine;
using ODC.Runtime;

namespace ODC.Examples
{
    /// <summary>
    /// HitDeduplicationContainer の使用例。
    /// 攻撃判定の多重ヒットを防止し、貫通攻撃のヒット数を制限する。
    /// </summary>
    public class HitDeduplicationContainerExample : MonoBehaviour
    {
        private HitDeduplicationContainer _hitDedup;

        private void Awake()
        {
            _hitDedup = new HitDeduplicationContainer(capacity: 512);
        }

        /// <summary>
        /// 近接攻撃のヒット判定。
        /// 同一攻撃で同じ敵に2回ヒットしないよう重複排除。
        /// </summary>
        /// <param name="attackEvent">攻撃イベントのハッシュ（攻撃者+攻撃ID等で生成）</param>
        /// <param name="target">ヒットした対象</param>
        /// <returns>有効なヒットならtrue</returns>
        public bool TryMeleeHit(int attackEvent, GameObject target)
        {
            // maxPierce=1 → 各ターゲットに1回だけヒット
            return _hitDedup.TryRecord(attackEvent, target.GetHashCode(), maxPierce: 1);
        }

        /// <summary>
        /// 貫通弾のヒット判定。
        /// 最大3体まで貫通し、残り貫通数を追跡する。
        /// </summary>
        /// <param name="bulletEvent">弾丸イベントのハッシュ</param>
        /// <param name="target">ヒットした対象</param>
        /// <param name="remainingPierce">残り貫通数（呼び出し側で管理）</param>
        /// <returns>有効なヒットならtrue</returns>
        public bool TryPiercingHit(int bulletEvent, GameObject target, ref int remainingPierce)
        {
            // pierce残数がref経由で自動減少
            bool hit = _hitDedup.TryRecord(bulletEvent, target.GetHashCode(), ref remainingPierce);

            if (hit)
            {
                Debug.Log($"貫通ヒット！ 残り貫通数: {remainingPierce}");

                // 貫通数が0になったら弾丸を破壊
                if (remainingPierce <= 0)
                {
                    Debug.Log("貫通上限到達 → 弾丸消滅");
                }
            }

            return hit;
        }

        /// <summary>
        /// 攻撃イベント終了時に記録を解放。
        /// 例: 攻撃アニメーション終了時、弾丸破壊時に呼ぶ。
        /// </summary>
        public void EndAttackEvent(int attackEvent)
        {
            int totalHits = _hitDedup.GetHitCount(attackEvent);
            Debug.Log($"攻撃終了: 合計{totalHits}ヒット");

            _hitDedup.Release(attackEvent);
        }

        private void OnDestroy()
        {
            _hitDedup?.Dispose();
        }
    }
}
