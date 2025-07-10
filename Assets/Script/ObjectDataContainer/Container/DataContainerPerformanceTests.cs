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
        /// 特定条件の敵を検索
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

        // パフォーマンス測定の設定
        private const int WARMUP_COUNT = 3;
        private const int MEASUREMENT_COUNT = 10;
        private const int ITERATIONS_PER_MEASUREMENT = 5;
        private const int ITERATIONS_PER_MEASUREMENT_SINGLE = 1;

        // 記事と同じデータ型
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

        // 事前生成したGameObjectを配列で管理
        private GameObject[] _preCreatedEnemies;

        // 事前生成したAIコンポネントを配列で管理
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
        /// 従来手法でのエンティティ追加のパフォーマンスを測定
        /// Dictionary×3 + List×3への同期追加処理の速度を計測
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

                    // Dictionaryに追加
                    healthDict.Add(enemy, health);
                    movementDict.Add(enemy, movement);
                    aiDict.Add(enemy, ai);

                    // Listにも追加（同期が必要）
                    healthList.Add(health);
                    movementList.Add(movement);
                    aiList.Add(ai);
                }
            })
            .SetUp(() =>
            {
                // 初期化
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
        /// ツールでのエンティティ追加のパフォーマンスを測定
        /// 単一のAddメソッドで全データを一括追加する速度を計測
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
                            // 初期化
                            container.Clear();
                        })
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT_SINGLE)
            .GC()
            .Run();
        }

        /// <summary>
        /// 従来手法でのエンティティ削除のパフォーマンスを測定
        /// IndexOfによる検索と複数コレクションからの削除処理の速度を計測
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
                        // インデックスを探す
                        int listIndex = aiList.IndexOf(aiDict[enemy]);

                        // 各Dictionaryから削除
                        healthDict.Remove(enemy);
                        movementDict.Remove(enemy);
                        aiDict.Remove(enemy);

                        // 各Listからも削除
                        healthList.RemoveAt(listIndex);
                        movementList.RemoveAt(listIndex);
                        aiList.RemoveAt(listIndex);
                    }
                }
            })
                            .SetUp(() =>
                           {
                               // 初期化
                               healthDict.Clear();
                               movementDict.Clear();
                               aiDict.Clear();
                               healthList.Clear();
                               movementList.Clear();
                               aiList.Clear();

                               // セットアップ
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
        /// ツールでのエンティティ削除のパフォーマンスを測定
        /// スワップ削除による高速な削除処理の速度を計測
        /// </summary>
        [Test, Performance]
        public void Benchmark_Remove_Container()
        {
            var container = new TestEnemyContainer(ENTITY_COUNT);

            // セットアップ
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
                                // セットアップ
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
        /// 従来手法でのランダムアクセスのパフォーマンスを測定
        /// 複数のDictionaryから個別にデータを取得し、戦闘力を計算する速度を計測
        /// </summary>
        [Test, Performance]
        public void Benchmark_RandomAccess_Traditional()
        {
            var healthDict = new Dictionary<GameObject, EnemyHealth>();
            var movementDict = new Dictionary<GameObject, EnemyMovement>();
            var aiDict = new Dictionary<GameObject, EnemyAI>();

            // セットアップ
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

                    // 複数のDictionaryから個別にデータを取得
                    if ( healthDict.TryGetValue(enemy, out var health) &&
                        movementDict.TryGetValue(enemy, out var movement) &&
                        aiDict.TryGetValue(enemy, out var ai) )
                    {
                        // 戦闘力を計算（HP × スピード × AI状態）
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
        /// 本ツールでのランダムアクセスのパフォーマンスを測定
        /// 単一のTryGetValueで全データを一括取得し、戦闘力を計算する速度を計測
        /// </summary>
        [Test, Performance]
        public void Benchmark_RandomAccess_Container()
        {
            var container = new TestEnemyContainer(ENTITY_COUNT);

            // セットアップ
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

                    // 単一のメソッドで全データを一括取得
                    if ( container.TryGetValue(enemy,
                        out var health, out var movement,
                        out var ai, out int index) )
                    {
                        // 戦闘力を計算（HP × スピード × AI状態）
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
        /// 従来手法での連続アクセス更新のパフォーマンスを測定
        /// Listを使った全エンティティのhpRate更新速度を計測
        /// </summary>
        [Test, Performance]
        public void Benchmark_SequentialUpdate_Traditional()
        {
            var healthList = new List<EnemyHealth>();
            var aiList = new List<EnemyAI>();

            // セットアップ
            for ( int i = 0; i < ENTITY_COUNT; i++ )
            {
                healthList.Add(new EnemyHealth { hp = 50 + i * 0.005f, maxHp = 100 });
                aiList.Add(_preCreatedEnemies[i].GetComponent<EnemyAI>());
            }

            Measure.Method(() =>
            {
                // 記事のUpdateメソッドと同じ処理
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
        /// ツールでの連続アクセス更新のパフォーマンスを測定
        /// 最適化されたメモリレイアウトでの全エンティティのhpRate更新速度を計測
        /// </summary>
        [Test, Performance]
        public unsafe void Benchmark_SequentialUpdate_Container()
        {
            var container = new TestEnemyContainer(ENTITY_COUNT);

            // セットアップ
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
                // 記事のUpdateメソッドと同じ処理
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
        /// 従来手法でのメモリアロケーションを測定
        /// 複数のコレクション初期化時のGCアロケーション量を計測
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
        /// ツールでのメモリアロケーションを測定
        /// 事前確保されたメモリによるゼロアロケーション動作を計測
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
    /// 入れ子になったクラスに対しては正しくコード生成が行えません。
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