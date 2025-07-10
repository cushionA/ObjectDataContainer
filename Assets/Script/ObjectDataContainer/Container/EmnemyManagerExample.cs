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

    // �A�g���r���[�g�ŊǗ��������^���w��
    [ContainerSetting(
        structType: new[] { typeof(EnemyHealth), typeof(EnemyMovement) },
        classType: new[] { typeof(EnemyAI) }
    )]
    public partial class EnemyContainer
    {
        public partial void Dispose();
    }

    // �G�̃f�[�^�Ǘ��N���X
    public class EnemyManager : MonoBehaviour
    {
        // GameObject���L�[�ɂ����e��Dictionary
        private Dictionary<GameObject, EnemyHealth> _healthDict = new();
        private Dictionary<GameObject, EnemyMovement> _movementDict = new();
        private Dictionary<GameObject, EnemyAI> _aiDict = new();

        // �A���A�N�Z�X�p��List�iDictionary�Ɠ������K�v�j
        private List<EnemyHealth> _healthList = new();
        private List<EnemyMovement> _movementList = new();
        private List<EnemyAI> _enemyAI = new();

        // �G��ǉ�
        public void AddEnemy(GameObject enemy)
        {
            var health = new EnemyHealth { hp = 100 };
            var movement = new EnemyMovement { speed = 5f };
            var ai = enemy.GetComponent<EnemyAI>();

            // Dictionary�ɒǉ�
            _healthDict.Add(enemy, health);
            _movementDict.Add(enemy, movement);
            _aiDict.Add(enemy, ai);

            // List�ɂ��ǉ��i�������K�v�j
            _healthList.Add(health);
            _movementList.Add(movement);
            _enemyAI.Add(ai);
        }

        // ����̓G�Ƀ_���[�W
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

        // �G���폜
        private void RemoveEnemy(GameObject enemy)
        {
            if ( _healthDict.ContainsKey(enemy) )
            {
                // �C���f�b�N�X��T��
                int index = _enemyAI.IndexOf(_aiDict[enemy]);

                // �eDictionary����폜
                _healthDict.Remove(enemy);
                _movementDict.Remove(enemy);
                _aiDict.Remove(enemy);

                // �eList������폜�i������ۂK�v������j
                _healthList.RemoveAt(index);
                _movementList.RemoveAt(index);
                _enemyAI.RemoveAt(index);

                Destroy(enemy);
            }
        }

        // �S�G�̍X�V�i�A���A�N�Z�X�j
        void Update()
        {
            // List���g���ĘA���A�N�Z�X
            for ( int i = 0; i < _healthList.Count; i++ )
            {
                var health = _healthList[i];

                // AI����������HP�������X�V
                _enemyAI[i].hpRate = health.hp / health.maxHp;
            }
        }
    }

    // �g�p��
    public class FixedEnemyManager : MonoBehaviour
    {
        private EnemyContainer _enemies = new(1000);

        // �G��ǉ��i�V���v���I�j
        public void AddEnemy(GameObject enemy)
        {
            _enemies.Add(enemy,
                new EnemyHealth { hp = 100 },
                new EnemyMovement { speed = 5f },
                enemy.GetComponent<EnemyAI>()
            );
        }

        // �G���폜
        public void RemoveEnemy(GameObject enemy)
        {
            _enemies.Remove(enemy);
            Destroy(enemy);
        }

        // �S�G�̍X�V
        unsafe void Update()
        {
            UnsafeList<EnemyHealth>.ReadOnly healthList = _enemies.GetEnemyHealthReadOnly();
            Span<EnemyAI> enemyAi = _enemies.GetEnemyAIsSpan();

            // �A���������A�N�Z�X�ō�����Hp�X�V
            for ( int i = 0; i < healthList.Length; i++ )
            {
                enemyAi[i].hpRate = healthList.Ptr[i].hp / healthList.Ptr[i].maxHp;
            }
        }
    }

}
