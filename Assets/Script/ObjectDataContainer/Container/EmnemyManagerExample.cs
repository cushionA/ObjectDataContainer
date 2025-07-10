using System;
using System.Collections.Generic;
using ToolAttribute.GenContainer;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace ZabutonTool
{

    public struct EnemyHealth
    {
        public float hp;
        public int maxHp;
    }

    public struct EnemyMovement
    {
        public float speed;
    }

    public class EnemyAI : MonoBehaviour
    {
        public float hpRate;
    }

    // アトリビュートで管理したい型を指定
    [ContainerSetting(
        structType: new[] { typeof(EnemyHealth), typeof(EnemyMovement) },
        classType: new[] { typeof(EnemyAI) }
    )]
    public partial class EnemyContainer
    {
        public partial void Dispose();
    }

    // 敵のデータ管理クラス
    public class EnemyManager : MonoBehaviour
    {
        // GameObjectをキーにした各種Dictionary
        private Dictionary<GameObject, EnemyHealth> _healthDict = new();
        private Dictionary<GameObject, EnemyMovement> _movementDict = new();
        private Dictionary<GameObject, EnemyAI> _aiDict = new();

        // 連続アクセス用のList（Dictionaryと同期が必要）
        private List<EnemyHealth> _healthList = new();
        private List<EnemyMovement> _movementList = new();
        private List<EnemyAI> _enemyAI = new();

        // 敵を追加
        public void AddEnemy(GameObject enemy)
        {
            var health = new EnemyHealth { hp = 100 };
            var movement = new EnemyMovement { speed = 5f };
            var ai = enemy.GetComponent<EnemyAI>();

            // Dictionaryに追加
            _healthDict.Add(enemy, health);
            _movementDict.Add(enemy, movement);
            _aiDict.Add(enemy, ai);

            // Listにも追加（同期が必要）
            _healthList.Add(health);
            _movementList.Add(movement);
            _enemyAI.Add(ai);
        }

        // 特定の敵にダメージ
        public void DamageEnemy(GameObject enemy, float damage)
        {
            if ( _healthDict.TryGetValue(enemy, out var health) )
            {
                health.hp -= damage;
                if ( health.hp <= 0 )
                {
                    RemoveEnemy(enemy);
                }
            }
        }

        // 敵を削除
        private void RemoveEnemy(GameObject enemy)
        {
            if ( _healthDict.ContainsKey(enemy) )
            {
                // インデックスを探す
                int index = _enemyAI.IndexOf(_aiDict[enemy]);

                // 各Dictionaryから削除
                _healthDict.Remove(enemy);
                _movementDict.Remove(enemy);
                _aiDict.Remove(enemy);

                // 各Listからも削除（順序を保つ必要がある）
                _healthList.RemoveAt(index);
                _movementList.RemoveAt(index);
                _enemyAI.RemoveAt(index);

                Destroy(enemy);
            }
        }

        // 全敵の更新（連続アクセス）
        void Update()
        {
            // Listを使って連続アクセス
            for ( int i = 0; i < _healthList.Count; i++ )
            {
                var health = _healthList[i];

                // AIが持つ自分のHP割合を更新
                _enemyAI[i].hpRate = health.hp / health.maxHp;
            }
        }
    }

    // 使用例
    public class FixedEnemyManager : MonoBehaviour
    {
        private EnemyContainer _enemies = new(1000);

        // 敵を追加（シンプル！）
        public void AddEnemy(GameObject enemy)
        {
            _enemies.Add(enemy,
                new EnemyHealth { hp = 100 },
                new EnemyMovement { speed = 5f },
                enemy.GetComponent<EnemyAI>()
            );
        }

        // 敵を削除
        public void RemoveEnemy(GameObject enemy)
        {
            _enemies.Remove(enemy);
            Destroy(enemy);
        }

        // 全敵の更新
        unsafe void Update()
        {
            UnsafeList<EnemyHealth>.ReadOnly healthList = _enemies.GetEnemyHealthReadOnly();
            Span<EnemyAI> enemyAi = _enemies.GetEnemyAIsSpan();

            // 連続メモリアクセスで高速でHp更新
            for ( int i = 0; i < healthList.Length; i++ )
            {
                enemyAi[i].hpRate = healthList.Ptr[i].hp / healthList.Ptr[i].maxHp;
            }
        }
    }

}
