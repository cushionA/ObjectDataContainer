using UnityEngine;
using ODC.Runtime;

namespace ODC.Examples
{
    /// <summary>
    /// ThresholdAccumulatorContainer の使用例。
    /// ダメージ蓄積によるよろめき（スタガー）システムの実装例。
    /// </summary>
    public class ThresholdAccumulatorContainerExample : MonoBehaviour
    {
        /// <summary>蓄積キー定数</summary>
        private const int Key_Stagger = 1;    // よろめきゲージ
        private const int Key_Rage = 2;        // 怒りゲージ

        private ThresholdAccumulatorContainer _accumulators;

        [SerializeField] private float _staggerThreshold = 100f;
        [SerializeField] private float _rageThreshold = 50f;

        private void Awake()
        {
            // オーナー64体、エントリ256個まで
            _accumulators = new ThresholdAccumulatorContainer(
                ownerCapacity: 64, entryCapacity: 256);
        }

        /// <summary>
        /// 敵を蓄積システムに登録する。
        /// </summary>
        public void RegisterEnemy(GameObject enemy)
        {
            _accumulators.AddOwner(enemy);

            // よろめきゲージ: 100ダメージ蓄積で発動
            _accumulators.Register(enemy, Key_Stagger, _staggerThreshold);

            // 怒りゲージ: 50ダメージ蓄積で怒り状態
            _accumulators.Register(enemy, Key_Rage, _rageThreshold);
        }

        /// <summary>
        /// ダメージを蓄積する。閾値に達したら効果発動。
        /// 超過分は自動的に繰り越される。
        /// </summary>
        public void AccumulateDamage(GameObject enemy, float damage)
        {
            // よろめきゲージに蓄積 → 閾値到達でtrueが返る
            if (_accumulators.Add(enemy, Key_Stagger, damage))
            {
                Debug.Log($"{enemy.name} がよろめき状態に！");
                // よろめきアニメーション再生など
            }

            // 怒りゲージにも蓄積
            if (_accumulators.Add(enemy, Key_Rage, damage))
            {
                Debug.Log($"{enemy.name} が怒り状態に！攻撃力上昇！");
            }
        }

        /// <summary>
        /// ゲージの進捗率を取得してUI表示に使用。
        /// </summary>
        public float GetStaggerProgress(GameObject enemy)
        {
            // 0.0 〜 1.0 の正規化された値
            return _accumulators.GetNormalized(enemy, Key_Stagger);
        }

        /// <summary>
        /// ゲージをリセットする（よろめき回復時など）。
        /// </summary>
        public void ResetStagger(GameObject enemy)
        {
            _accumulators.Reset(enemy, Key_Stagger);
        }

        public void UnregisterEnemy(GameObject enemy)
        {
            _accumulators.RemoveOwner(enemy);
        }

        private void OnDestroy()
        {
            _accumulators?.Dispose();
        }
    }
}
