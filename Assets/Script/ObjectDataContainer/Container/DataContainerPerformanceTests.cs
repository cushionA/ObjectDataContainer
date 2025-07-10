using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Collections.Generic;
using ToolAttribute.GenContainer;
using static ToolCodeGenerator.Tests.DataContainerPerformanceTests;
using System.Collections;

namespace ToolCodeGenerator.Tests
{
    [ContainerSetting(
            structType: new[] { typeof(EnemyHealth), typeof(EnemyMovement) },
            classType: new[] { typeof(EnemyAI) }
        )]
    public partial class TestEnemyContainer
    {
        public partial void Dispose();

        /// <summary>
        /// ��������̓G������
        /// </summary>
        /// <param name="threshold"></param>
        /// <returns></returns>
        public List<int> FindLowHealthEnemies(float threshold)
        {
            var result = new List<int>();

            for ( int i = 0; i < Count; i++ )
            {
                ref var health = ref GetEnemyHealthByIndex(i);
                if ( health.hp < threshold )
                {
                    result.Add(i);
                }
            }
            return result;
        }
    }

    [TestFixture]
    public class DataContainerPerformanceTests
    {
        private const int ENTITY_COUNT = 10000;
        private const int OPERATION_COUNT = 1000;

        // �p�t�H�[�}���X����̐ݒ�
        private const int WARMUP_COUNT = 3;
        private const int MEASUREMENT_COUNT = 10;
        private const int ITERATIONS_PER_MEASUREMENT = 5;
        private const int ITERATIONS_PER_MEASUREMENT_SINGLE = 1;

        // �L���Ɠ����f�[�^�^
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

        // ���O��������GameObject��z��ŊǗ�
        private GameObject[] _preCreatedEnemies;

        // ���O��������AI�R���|�l���g��z��ŊǗ�
        private EnemyAI[] _preCreatedAI;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            _preCreatedEnemies = new GameObject[ENTITY_COUNT];
            _preCreatedAI = new EnemyAI[ENTITY_COUNT];
            for ( int i = 0; i < ENTITY_COUNT; i++ )
            {
                var go = new GameObject($"Enemy{i}");
                _preCreatedAI[i] = go.AddComponent<EnemyAI>();
                _preCreatedEnemies[i] = go;
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            foreach ( var go in _preCreatedEnemies )
            {
                if ( go != null )
                    GameObject.DestroyImmediate(go);
            }
            _preCreatedEnemies = null;
        }

        /// <summary>
        /// �]����@�ł̃G���e�B�e�B�ǉ��̃p�t�H�[�}���X�𑪒�
        /// Dictionary�~3 + List�~3�ւ̓����ǉ������̑��x���v��
        /// </summary>
        [Test, Performance]
        public void Benchmark_Add_Traditional()
        {
            var healthDict = new Dictionary<GameObject, EnemyHealth>();
            var movementDict = new Dictionary<GameObject, EnemyMovement>();
            var aiDict = new Dictionary<GameObject, EnemyAI>();
            var healthList = new List<EnemyHealth>();
            var movementList = new List<EnemyMovement>();
            var aiList = new List<EnemyAI>();

            Measure.Method(() =>
            {
                for ( int i = 0; i < OPERATION_COUNT; i++ )
                {
                    var enemy = _preCreatedEnemies[i];
                    var health = new EnemyHealth { hp = 100, maxHp = 100 };
                    var movement = new EnemyMovement { speed = 5f };
                    var ai = _preCreatedAI[i];

                    // Dictionary�ɒǉ�
                    healthDict.Add(enemy, health);
                    movementDict.Add(enemy, movement);
                    aiDict.Add(enemy, ai);

                    // List�ɂ��ǉ��i�������K�v�j
                    healthList.Add(health);
                    movementList.Add(movement);
                    aiList.Add(ai);
                }
            })
            .SetUp(() =>
            {
                // ������
                healthDict.Clear();
                movementDict.Clear();
                aiDict.Clear();
                healthList.Clear();
                movementList.Clear();
                aiList.Clear();
            })
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT_SINGLE)
            .GC()
            .Run();
        }

        /// <summary>
        /// �c�[���ł̃G���e�B�e�B�ǉ��̃p�t�H�[�}���X�𑪒�
        /// �P���Add���\�b�h�őS�f�[�^���ꊇ�ǉ����鑬�x���v��
        /// </summary>
        [Test, Performance]
        public void Benchmark_Add_Container()
        {
            var container = new TestEnemyContainer(ENTITY_COUNT);

            Measure.Method(() =>
            {
                for ( int i = 0; i < OPERATION_COUNT; i++ )
                {
                    var enemy = _preCreatedEnemies[i];
                    container.Add(enemy,
                        new EnemyHealth { hp = 100, maxHp = 100 },
                        new EnemyMovement { speed = 5f },
                        _preCreatedAI[i]
                    );
                }
            })
                        .SetUp(() =>
                        {
                            // ������
                            container.Clear();
                        })
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT_SINGLE)
            .GC()
            .Run();
        }

        /// <summary>
        /// �]����@�ł̃G���e�B�e�B�폜�̃p�t�H�[�}���X�𑪒�
        /// IndexOf�ɂ�錟���ƕ����R���N�V��������̍폜�����̑��x���v��
        /// </summary>
        [Test, Performance]
        public void Benchmark_Remove_Traditional()
        {
            var healthDict = new Dictionary<GameObject, EnemyHealth>();
            var movementDict = new Dictionary<GameObject, EnemyMovement>();
            var aiDict = new Dictionary<GameObject, EnemyAI>();
            var healthList = new List<EnemyHealth>();
            var movementList = new List<EnemyMovement>();
            var aiList = new List<EnemyAI>();

            var random = new System.Random(12345);
            var indicesToRemove = new int[100];
            for ( int i = 0; i < 100; i++ )
            {
                indicesToRemove[i] = random.Next(OPERATION_COUNT);
            }

            Measure.Method(() =>
            {
                foreach ( var index in indicesToRemove )
                {
                    var enemy = _preCreatedEnemies[index];
                    if ( healthDict.ContainsKey(enemy) )
                    {
                        // �C���f�b�N�X��T��
                        int listIndex = aiList.IndexOf(aiDict[enemy]);

                        // �eDictionary����폜
                        healthDict.Remove(enemy);
                        movementDict.Remove(enemy);
                        aiDict.Remove(enemy);

                        // �eList������폜
                        healthList.RemoveAt(listIndex);
                        movementList.RemoveAt(listIndex);
                        aiList.RemoveAt(listIndex);
                    }
                }
            })
                            .SetUp(() =>
                           {
                               // ������
                               healthDict.Clear();
                               movementDict.Clear();
                               aiDict.Clear();
                               healthList.Clear();
                               movementList.Clear();
                               aiList.Clear();

                               // �Z�b�g�A�b�v
                               for ( int i = 0; i < OPERATION_COUNT; i++ )
                               {
                                   var enemy = _preCreatedEnemies[i];
                                   var health = new EnemyHealth { hp = 100, maxHp = 100 };
                                   var movement = new EnemyMovement { speed = 5f };
                                   var ai = _preCreatedAI[i];

                                   healthDict.Add(enemy, health);
                                   movementDict.Add(enemy, movement);
                                   aiDict.Add(enemy, ai);
                                   healthList.Add(health);
                                   movementList.Add(movement);
                                   aiList.Add(ai);
                               }
                           })
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT_SINGLE)
            .Run();
        }

        /// <summary>
        /// �c�[���ł̃G���e�B�e�B�폜�̃p�t�H�[�}���X�𑪒�
        /// �X���b�v�폜�ɂ�鍂���ȍ폜�����̑��x���v��
        /// </summary>
        [Test, Performance]
        public void Benchmark_Remove_Container()
        {
            var container = new TestEnemyContainer(ENTITY_COUNT);

            // �Z�b�g�A�b�v
            for ( int i = 0; i < OPERATION_COUNT; i++ )
            {
                var enemy = _preCreatedEnemies[i];
                container.Add(enemy,
                    new EnemyHealth { hp = 100, maxHp = 100 },
                    new EnemyMovement { speed = 5f },
                    _preCreatedAI[i]
                );
            }

            var random = new System.Random(12345);
            var indicesToRemove = new int[100];
            for ( int i = 0; i < 100; i++ )
            {
                indicesToRemove[i] = random.Next(OPERATION_COUNT);
            }

            Measure.Method(() =>
            {
                foreach ( var index in indicesToRemove )
                {
                    var enemy = _preCreatedEnemies[index];
                    container.Remove(enemy);
                }
            })
                            .SetUp(() =>
                            {
                                // �Z�b�g�A�b�v
                                for ( int i = 0; i < OPERATION_COUNT; i++ )
                                {
                                    var enemy = _preCreatedEnemies[i];
                                    container.Add(enemy,
                                        new EnemyHealth { hp = 100, maxHp = 100 },
                                        new EnemyMovement { speed = 5f },
                                        _preCreatedAI[i]
                                    );
                                }
                            })
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT_SINGLE)
            .Run();
        }

        /// <summary>
        /// �]����@�ł̃����_���A�N�Z�X�̃p�t�H�[�}���X�𑪒�
        /// ������Dictionary����ʂɃf�[�^���擾���A�퓬�͂��v�Z���鑬�x���v��
        /// </summary>
        [Test, Performance]
        public void Benchmark_RandomAccess_Traditional()
        {
            var healthDict = new Dictionary<GameObject, EnemyHealth>();
            var movementDict = new Dictionary<GameObject, EnemyMovement>();
            var aiDict = new Dictionary<GameObject, EnemyAI>();

            // �Z�b�g�A�b�v
            for ( int i = 0; i < ENTITY_COUNT; i++ )
            {
                healthDict[_preCreatedEnemies[i]] = new EnemyHealth { hp = 100 - i * 0.01f, maxHp = 100 };
                movementDict[_preCreatedEnemies[i]] = new EnemyMovement { speed = 5f + i * 0.001f };
                aiDict[_preCreatedEnemies[i]] = _preCreatedEnemies[i].GetComponent<EnemyAI>();
                aiDict[_preCreatedEnemies[i]].hpRate = (100 - i * 0.01f) / 100f;
            }

            var random = new System.Random(12345);

            Measure.Method(() =>
            {
                float totalCombatPower = 0;
                for ( int i = 0; i < OPERATION_COUNT; i++ )
                {
                    var enemy = _preCreatedEnemies[random.Next(ENTITY_COUNT)];

                    // ������Dictionary����ʂɃf�[�^���擾
                    if ( healthDict.TryGetValue(enemy, out var health) &&
                        movementDict.TryGetValue(enemy, out var movement) &&
                        aiDict.TryGetValue(enemy, out var ai) )
                    {
                        // �퓬�͂��v�Z�iHP �~ �X�s�[�h �~ AI��ԁj
                        totalCombatPower += health.hp * movement.speed * ai.hpRate;
                    }
                }
            })
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
            .Run();
        }

        /// <summary>
        /// �{�c�[���ł̃����_���A�N�Z�X�̃p�t�H�[�}���X�𑪒�
        /// �P���TryGetValue�őS�f�[�^���ꊇ�擾���A�퓬�͂��v�Z���鑬�x���v��
        /// </summary>
        [Test, Performance]
        public void Benchmark_RandomAccess_Container()
        {
            var container = new TestEnemyContainer(ENTITY_COUNT);

            // �Z�b�g�A�b�v
            for ( int i = 0; i < ENTITY_COUNT; i++ )
            {
                var ai = _preCreatedEnemies[i].GetComponent<EnemyAI>();
                ai.hpRate = (100 - i * 0.01f) / 100f;

                container.Add(_preCreatedEnemies[i],
                    new EnemyHealth { hp = 100 - i * 0.01f, maxHp = 100 },
                    new EnemyMovement { speed = 5f + i * 0.001f },
                    ai
                );
            }

            var random = new System.Random(12345);

            Measure.Method(() =>
            {
                float totalCombatPower = 0;
                for ( int i = 0; i < OPERATION_COUNT; i++ )
                {
                    var enemy = _preCreatedEnemies[random.Next(ENTITY_COUNT)];

                    // �P��̃��\�b�h�őS�f�[�^���ꊇ�擾
                    if ( container.TryGetValue(enemy,
                        out var health, out var movement,
                        out var ai, out int index) )
                    {
                        // �퓬�͂��v�Z�iHP �~ �X�s�[�h �~ AI��ԁj
                        totalCombatPower += health.hp * movement.speed * ai.hpRate;
                    }
                }
            })
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
            .Run();
        }

        /// <summary>
        /// �]����@�ł̘A���A�N�Z�X�X�V�̃p�t�H�[�}���X�𑪒�
        /// List���g�����S�G���e�B�e�B��hpRate�X�V���x���v��
        /// </summary>
        [Test, Performance]
        public void Benchmark_SequentialUpdate_Traditional()
        {
            var healthList = new List<EnemyHealth>();
            var aiList = new List<EnemyAI>();

            // �Z�b�g�A�b�v
            for ( int i = 0; i < ENTITY_COUNT; i++ )
            {
                healthList.Add(new EnemyHealth { hp = 50 + i * 0.005f, maxHp = 100 });
                aiList.Add(_preCreatedEnemies[i].GetComponent<EnemyAI>());
            }

            Measure.Method(() =>
            {
                // �L����Update���\�b�h�Ɠ�������
                for ( int iter = 0; iter < 10; iter++ )
                {
                    for ( int i = 0; i < healthList.Count; i++ )
                    {
                        var health = healthList[i];
                        aiList[i].hpRate = health.hp / health.maxHp;
                    }
                }
            })
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
            .Run();
        }

        /// <summary>
        /// �c�[���ł̘A���A�N�Z�X�X�V�̃p�t�H�[�}���X�𑪒�
        /// �œK�����ꂽ���������C�A�E�g�ł̑S�G���e�B�e�B��hpRate�X�V���x���v��
        /// </summary>
        [Test, Performance]
        public unsafe void Benchmark_SequentialUpdate_Container()
        {
            var container = new TestEnemyContainer(ENTITY_COUNT);

            // �Z�b�g�A�b�v
            for ( int i = 0; i < ENTITY_COUNT; i++ )
            {
                container.Add(_preCreatedEnemies[i],
                    new EnemyHealth { hp = 50 + i * 0.005f, maxHp = 100 },
                    new EnemyMovement { speed = 5f },
                    _preCreatedEnemies[i].GetComponent<EnemyAI>()
                );
            }

            Measure.Method(() =>
            {
                // �L����Update���\�b�h�Ɠ�������
                for ( int iter = 0; iter < 10; iter++ )
                {
                    var healthList = container.GetEnemyHealthReadOnly();
                    var enemyAis = container.GetEnemyAIsSpan();

                    for ( int i = 0; i < healthList.Length; i++ )
                    {
                        enemyAis[i].hpRate = healthList.Ptr[i].hp / healthList.Ptr[i].maxHp;
                    }
                }
            })
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
            .Run();
        }

        /// <summary>
        /// �]����@�ł̃������A���P�[�V�����𑪒�
        /// �����̃R���N�V��������������GC�A���P�[�V�����ʂ��v��
        /// </summary>
        [Test, Performance]
        public void Benchmark_MemoryAllocation_Traditional()
        {
            Measure.Method(() =>
            {
                var healthDict = new Dictionary<GameObject, EnemyHealth>(1000);
                var movementDict = new Dictionary<GameObject, EnemyMovement>(1000);
                var aiDict = new Dictionary<GameObject, EnemyAI>(1000);
                var healthList = new List<EnemyHealth>(1000);
                var movementList = new List<EnemyMovement>(1000);
                var aiList = new List<EnemyAI>(1000);

                for ( int i = 0; i < 100; i++ )
                {
                    var enemy = _preCreatedEnemies[i];
                    var health = new EnemyHealth { hp = 100, maxHp = 100 };
                    var movement = new EnemyMovement { speed = 5f };
                    var ai = _preCreatedAI[i];

                    healthDict.Add(enemy, health);
                    movementDict.Add(enemy, movement);
                    aiDict.Add(enemy, ai);
                    healthList.Add(health);
                    movementList.Add(movement);
                    aiList.Add(ai);
                }
            })
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT_SINGLE)
            .GC()
            .Run();
        }

        /// <summary>
        /// �c�[���ł̃������A���P�[�V�����𑪒�
        /// ���O�m�ۂ��ꂽ�������ɂ��[���A���P�[�V����������v��
        /// </summary>
        [Test, Performance]
        public void Benchmark_MemoryAllocation_Container()
        {
            Measure.Method(() =>
            {
                var container = new TestEnemyContainer(1000);

                for ( int i = 0; i < 100; i++ )
                {
                    var enemy = _preCreatedEnemies[i];
                    container.Add(enemy,
                        new EnemyHealth { hp = 100, maxHp = 100 },
                        new EnemyMovement { speed = 5f },
                        _preCreatedAI[i]
                    );
                }
            })
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT_SINGLE)
            .GC()
            .Run();
        }
    }

    /// <summary>
    /// ����q�ɂȂ����N���X�ɑ΂��Ă͐������R�[�h�������s���܂���B
    /// </summary>
    public class HogeClass
    {

        [ContainerSetting(
        structType: new[] { typeof(EnemyHealth), typeof(EnemyMovement) },
        classType: new[] { typeof(EnemyAI) })]
        public partial class AntiPattern
        {

        }
    }


}