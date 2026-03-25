using UnityEngine;
using ODC.Runtime;

namespace ODC.Examples
{
    /// <summary>
    /// SparseSetContainer の使用例。
    /// アクティブな敵データを疎密集合で管理し、毎フレーム連続メモリで高速処理する。
    /// </summary>
    public class SparseSetContainerExample : MonoBehaviour
    {
        /// <summary>敵のステータス構造体</summary>
        private struct EnemyStats
        {
            public float Hp;
            public float Speed;
            public float AttackPower;
        }

        private SparseSetContainer<EnemyStats> _activeEnemies;

        private void Awake()
        {
            // 最大256体の敵を管理可能なコンテナを作成
            _activeEnemies = new SparseSetContainer<EnemyStats>(maxCapacity: 256);
        }

        /// <summary>
        /// 敵をアクティブ集合に登録する。
        /// </summary>
        public void RegisterEnemy(GameObject enemy, float hp, float speed, float attack)
        {
            _activeEnemies.Set(enemy, new EnemyStats
            {
                Hp = hp,
                Speed = speed,
                AttackPower = attack
            });
        }

        /// <summary>
        /// 敵を非アクティブにする（集合から除外）。
        /// BackSwap削除で配列の連続性が保たれる。
        /// </summary>
        public void DeactivateEnemy(GameObject enemy)
        {
            _activeEnemies.Remove(enemy);
        }

        /// <summary>
        /// ダメージを与える。refアクセスでゼロコピー書き換え。
        /// </summary>
        public void DealDamage(GameObject enemy, float damage)
        {
            if (!_activeEnemies.Contains(enemy)) return;

            // GetRefでコピーなしの直接参照を取得
            ref var stats = ref _activeEnemies.GetRef(enemy);
            stats.Hp -= damage;

            if (stats.Hp <= 0f)
            {
                DeactivateEnemy(enemy);
            }
        }

        /// <summary>
        /// 全アクティブ敵を一括処理。
        /// ActiveValuesはSpanなので、連続メモリをキャッシュフレンドリーに走査できる。
        /// </summary>
        private void Update()
        {
            var values = _activeEnemies.ActiveValues;
            var hashes = _activeEnemies.ActiveHashes;

            for (int i = 0; i < values.Length; i++)
            {
                // 例: 全敵のHPを毒ダメージで減少
                // Spanの要素をrefで取得すればコピー不要
                // ここではReadOnlyなので読み取りのみ
                float hp = values[i].Hp;
                float speed = values[i].Speed;

                // 実際のゲームロジック: AIの移動処理など
                // MoveEnemy(hashes[i], speed * Time.deltaTime);
            }
        }

        private void OnDestroy()
        {
            _activeEnemies?.Dispose();
        }
    }
}
