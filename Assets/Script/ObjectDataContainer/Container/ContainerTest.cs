using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using ODC.Attributes;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.PerformanceTesting;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace ToolCodeGenerator.Tests
{

    // テスト用の構造体定義（各64Byte以下）
    [System.Serializable]
    public struct PlayerStats // 32 bytes
    {
        public int health;
        public int mana;
        public float speed;
        public float damage;
        public Vector2 position;
    }

    [System.Serializable]
    public struct WeaponData // 28 bytes
    {
        public int weaponId;
        public float damage;
        public float range;
        public float fireRate;
        public int ammo;
    }

    [System.Serializable]
    public struct BuffData // 20 bytes
    {
        public int buffId;
        public float duration;
        public float effect;
        public int stackCount;
    }

    [System.Serializable]
    public struct InventoryItem // 24 bytes
    {
        public int itemId;
        public int quantity;
        public float weight;
        public int rarity;
    }

    [System.Serializable]
    public struct SkillData // 28 bytes
    {
        public int skillId;
        public int level;
        public float cooldown;
        public float manaCost;
        public bool isActive;
    }

    // テスト用のクラス定義
    public class PlayerController
    {
        public string playerName;
        public Transform transform;
        public Rigidbody rigidbody;
        public int level;

        public PlayerController(string name, int level)
        {
            this.playerName = name;
            this.level = level;
        }
    }

    public class WeaponController
    {
        public string weaponName;
        public Transform weaponTransform;
        public AudioSource audioSource;
        public ParticleSystem muzzleFlash;

        public WeaponController(string name)
        {
            this.weaponName = name;
        }
    }

    public class BuffController
    {
        public string buffName;
        public Material buffEffect;
        public Animation buffAnimation;
        public float remainingTime;

        public BuffController(string name, float time)
        {
            this.buffName = name;
            this.remainingTime = time;
        }
    }

    public class InventoryController
    {
        public string inventoryName;
        public Transform containerTransform;
        public Canvas inventoryUI;
        public int maxSlots;

        public InventoryController(string name, int slots)
        {
            this.inventoryName = name;
            this.maxSlots = slots;
        }
    }

    public class SkillController
    {
        public string skillName;
        public Animator skillAnimator;
        public ParticleSystem skillEffect;
        public bool isLearned;

        public SkillController(string name, bool learned)
        {
            this.skillName = name;
            this.isLearned = learned;
        }
    }

    // Source Generatorでコードが生成されるコンテナクラス
    [ContainerSetting(
         new[] { typeof(PlayerStats), typeof(WeaponData), typeof(BuffData), typeof(InventoryItem), typeof(SkillData) },
        new[] { typeof(PlayerController), typeof(WeaponController), typeof(BuffController), typeof(InventoryController), typeof(SkillController) }
    )]
    public unsafe partial class TestContainer
    {
        #region IDisposable
        /// <summary>
        /// リソースの解放
        /// </summary>
        public partial void Dispose();
        #endregion

        public string GetDebugString()
        {
            return $"容量{_maxCapacity}, 件数{Count}, 長さ{_playerStats.Length},キャパ{_playerStats.Capacity}";
        }

        /// <summary>
        /// このクラスインスタンス内の全てのUnsafeListフィールドのCapacity値を文字列で取得します。
        /// </summary>
        /// <returns>各UnsafeListのフィールド名とCapacityを含む文字列。
        /// UnsafeListフィールドがnullの場合はその旨のメッセージ。</returns>
        public string GetAllUnsafeListCapacities()
        {
            StringBuilder sb = new StringBuilder();

            // 各UnsafeListフィールドのCapacityを個別に取得
            // nullチェックを忘れずに行う
            sb.AppendLine($"_entries: Capacity = {(_entries.IsCreated ? _entries.Capacity.ToString() : "Not Created")}");
            sb.AppendLine($"_playerStats: Capacity = {(_playerStats.IsCreated ? _playerStats.Capacity.ToString() : "Not Created")}");
            sb.AppendLine($"_weaponData: Capacity = {(_weaponData.IsCreated ? _weaponData.Capacity.ToString() : "Not Created")}");
            sb.AppendLine($"_buffData: Capacity = {(_buffData.IsCreated ? _buffData.Capacity.ToString() : "Not Created")}");
            sb.AppendLine($"_inventoryItem: Capacity = {(_inventoryItem.IsCreated ? _inventoryItem.Capacity.ToString() : "Not Created")}");
            sb.AppendLine($"_skillData: Capacity = {(_skillData.IsCreated ? _skillData.Capacity.ToString() : "Not Created")}");

            return sb.ToString();
        }
    }

    // Dictionaryでの実装
    public class DictionaryBasedContainer : IDisposable
    {
        // 構造体用Dictionary
        public Dictionary<GameObject, PlayerStats> PlayerStatsDict { get; private set; }
        public Dictionary<GameObject, WeaponData> WeaponDataDict { get; private set; }
        public Dictionary<GameObject, BuffData> BuffDataDict { get; private set; }
        public Dictionary<GameObject, InventoryItem> InventoryItemDict { get; private set; }
        public Dictionary<GameObject, SkillData> SkillDataDict { get; private set; }

        // クラス用Dictionary
        public Dictionary<GameObject, PlayerController> PlayerControllerDict { get; private set; }
        public Dictionary<GameObject, WeaponController> WeaponControllerDict { get; private set; }
        public Dictionary<GameObject, BuffController> BuffControllerDict { get; private set; }
        public Dictionary<GameObject, InventoryController> InventoryControllerDict { get; private set; }
        public Dictionary<GameObject, SkillController> SkillControllerDict { get; private set; }

        public int Count { get; private set; }

        public DictionaryBasedContainer(int initialCapacity = 130)
        {
            PlayerStatsDict = new Dictionary<GameObject, PlayerStats>(initialCapacity);
            WeaponDataDict = new Dictionary<GameObject, WeaponData>(initialCapacity);
            BuffDataDict = new Dictionary<GameObject, BuffData>(initialCapacity);
            InventoryItemDict = new Dictionary<GameObject, InventoryItem>(initialCapacity);
            SkillDataDict = new Dictionary<GameObject, SkillData>(initialCapacity);

            PlayerControllerDict = new Dictionary<GameObject, PlayerController>(initialCapacity);
            WeaponControllerDict = new Dictionary<GameObject, WeaponController>(initialCapacity);
            BuffControllerDict = new Dictionary<GameObject, BuffController>(initialCapacity);
            InventoryControllerDict = new Dictionary<GameObject, InventoryController>(initialCapacity);
            SkillControllerDict = new Dictionary<GameObject, SkillController>(initialCapacity);

            Count = 0;
        }

        public void Add(GameObject obj,
            PlayerStats playerStats, WeaponData weaponData, BuffData buffData, InventoryItem inventoryItem, SkillData skillData,
            PlayerController playerController, WeaponController weaponController, BuffController buffController,
            InventoryController inventoryController, SkillController skillController)
        {
            PlayerStatsDict[obj] = playerStats;
            WeaponDataDict[obj] = weaponData;
            BuffDataDict[obj] = buffData;
            InventoryItemDict[obj] = inventoryItem;
            SkillDataDict[obj] = skillData;

            PlayerControllerDict[obj] = playerController;
            WeaponControllerDict[obj] = weaponController;
            BuffControllerDict[obj] = buffController;
            InventoryControllerDict[obj] = inventoryController;
            SkillControllerDict[obj] = skillController;

            Count++;
        }

        public bool TryGetValue(GameObject obj,
            out PlayerStats playerStats, out WeaponData weaponData, out BuffData buffData, out InventoryItem inventoryItem, out SkillData skillData,
            out PlayerController playerController, out WeaponController weaponController, out BuffController buffController,
            out InventoryController inventoryController, out SkillController skillController)
        {
            if ( PlayerStatsDict.ContainsKey(obj) )
            {
                PlayerStatsDict.TryGetValue(obj, out playerStats);
                WeaponDataDict.TryGetValue(obj, out weaponData);
                BuffDataDict.TryGetValue(obj, out buffData);
                InventoryItemDict.TryGetValue(obj, out inventoryItem);
                SkillDataDict.TryGetValue(obj, out skillData);
                PlayerControllerDict.TryGetValue(obj, out playerController);
                WeaponControllerDict.TryGetValue(obj, out weaponController);
                BuffControllerDict.TryGetValue(obj, out buffController);
                InventoryControllerDict.TryGetValue(obj, out inventoryController);
                SkillControllerDict.TryGetValue(obj, out skillController);
                return true;
            }
            else
            {
                playerStats = default;
                weaponData = default;
                buffData = default;
                inventoryItem = default;
                skillData = default;
                playerController = null;
                weaponController = null;
                buffController = null;
                inventoryController = null;
                skillController = null;
                return false;
            }
        }

        public bool Remove(GameObject obj)
        {
            bool removed = PlayerStatsDict.Remove(obj) &&
                          WeaponDataDict.Remove(obj) &&
                          BuffDataDict.Remove(obj) &&
                          InventoryItemDict.Remove(obj) &&
                          SkillDataDict.Remove(obj) &&
                          PlayerControllerDict.Remove(obj) &&
                          WeaponControllerDict.Remove(obj) &&
                          BuffControllerDict.Remove(obj) &&
                          InventoryControllerDict.Remove(obj) &&
                          SkillControllerDict.Remove(obj);

            if ( removed )
                Count--;
            return removed;
        }

        public void Clear()
        {
            PlayerStatsDict.Clear();
            WeaponDataDict.Clear();
            BuffDataDict.Clear();
            InventoryItemDict.Clear();
            SkillDataDict.Clear();

            PlayerControllerDict.Clear();
            WeaponControllerDict.Clear();
            BuffControllerDict.Clear();
            InventoryControllerDict.Clear();
            SkillControllerDict.Clear();

            Count = 0;
        }

        public void Dispose()
        {
            Clear();
        }
    }

    [TestFixture]
    public class ContainerPerformanceTests
    {
        // 共通のテスト用パラメータ定数
        private const int TEST_OBJECT_COUNT = 10000;
        private const int MEASUREMENT_COUNT = 10;
        private const int WARMUP_COUNT = 3;
        private const int RANDOM_SEED = 42;

        private TestContainer _generatedContainer;
        private DictionaryBasedContainer _dictionaryContainer;
        private GameObject[] _testObjects;
        private (PlayerStats, WeaponData, BuffData, InventoryItem, SkillData)[] _structArray;
        private (PlayerController, WeaponController, BuffController, InventoryController, SkillController)[] _classArray;

        [SetUp]
        public void Setup()
        {
            // テスト用のGameObjectを作成
            _testObjects = new GameObject[TEST_OBJECT_COUNT];
            _structArray = new (PlayerStats, WeaponData, BuffData, InventoryItem, SkillData)[TEST_OBJECT_COUNT];
            _classArray = new (PlayerController, WeaponController, BuffController, InventoryController, SkillController)[TEST_OBJECT_COUNT];

            for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
            {
                _testObjects[i] = new GameObject($"TestObject_{i}");
                _structArray[i] = CreateTestStructData(i);
                _classArray[i] = CreateTestClassData(i);
            }

            // コンテナ初期化
            _generatedContainer = new TestContainer(TEST_OBJECT_COUNT);
            _dictionaryContainer = new DictionaryBasedContainer(TEST_OBJECT_COUNT);
        }

        [TearDown]
        public void TearDown()
        {
            // リソースの解放
            _generatedContainer?.Dispose();
            _dictionaryContainer?.Dispose();

            // TestObjectsの破棄
            if ( _testObjects != null )
            {
                for ( int i = 0; i < _testObjects.Length; i++ )
                {
                    if ( _testObjects[i] != null )
                    {
                        UnityEngine.Object.DestroyImmediate(_testObjects[i]);
                    }
                }
                _testObjects = null;
            }
        }

        private (PlayerStats, WeaponData, BuffData, InventoryItem, SkillData) CreateTestStructData(int index)
        {
            return (
                new PlayerStats { health = 100 + index, mana = 50 + index, speed = 5.0f + index, damage = 10.0f + index, position = new Vector2(index, index) },
                new WeaponData { weaponId = index, damage = 25.0f + index, range = 10.0f + index, fireRate = 1.5f, ammo = 30 },
                new BuffData { buffId = index, duration = 10.0f, effect = 1.5f, stackCount = 1 },
                new InventoryItem { itemId = index, quantity = 1, weight = 0.5f, rarity = index % 5 },
                new SkillData { skillId = index, level = index % 10 + 1, cooldown = 5.0f, manaCost = 20.0f, isActive = index % 2 == 0 }
            );
        }

        private (PlayerController, WeaponController, BuffController, InventoryController, SkillController) CreateTestClassData(int index)
        {
            return (
                new PlayerController($"Player_{index}", index % 50 + 1),
                new WeaponController($"Weapon_{index}"),
                new BuffController($"Buff_{index}", 10.0f),
                new InventoryController($"Inventory_{index}", 20),
                new SkillController($"Skill_{index}", index % 2 == 0)
            );
        }

        #region 要素追加テスト

        /// <summary>
        /// Generated Containerへの要素追加パフォーマンスを測定します。
        /// 指定された数の要素を順次追加し、実行時間を計測します。
        /// </summary>
        [Test, Performance]
        public void AddOperation_GeneratedContainer()
        {
            Measure.Method(() =>
            {
                for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
                {
                    _generatedContainer.Add(_testObjects[i],
                        _structArray[i].Item1, _structArray[i].Item2, _structArray[i].Item3, _structArray[i].Item4, _structArray[i].Item5,
                        _classArray[i].Item1, _classArray[i].Item2, _classArray[i].Item3, _classArray[i].Item4, _classArray[i].Item5);
                }
            })
            .SetUp(() =>
            {
                _generatedContainer.Clear();
            })
            .SampleGroup("Generated Container - Add")
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .Run();
        }

        /// <summary>
        /// Dictionary Containerへの要素追加パフォーマンスを測定します。
        /// 指定された数の要素を順次追加し、実行時間を計測します。
        /// </summary>
        [Test, Performance]
        public void AddOperation_DictionaryContainer()
        {
            Measure.Method(() =>
            {
                for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
                {
                    _dictionaryContainer.Add(_testObjects[i],
                        _structArray[i].Item1, _structArray[i].Item2, _structArray[i].Item3, _structArray[i].Item4, _structArray[i].Item5,
                        _classArray[i].Item1, _classArray[i].Item2, _classArray[i].Item3, _classArray[i].Item4, _classArray[i].Item5);
                }
            })
            .SetUp(() =>
            {
                _dictionaryContainer.Clear();
            })
            .SampleGroup("Dictionary Container - Add")
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .Run();
        }

        #endregion

        #region 連続アクセステスト

        /// <summary>
        /// Generated Containerからの連続的な要素取得パフォーマンスを測定します。
        /// 全要素を順番にアクセスし、実行時間を計測します。
        /// </summary>
        [Test, Performance]
        public void SequentialAccess_GeneratedContainer()
        {
            // データを事前に追加
            for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
            {
                _generatedContainer.Add(_testObjects[i],
                    _structArray[i].Item1, _structArray[i].Item2, _structArray[i].Item3, _structArray[i].Item4, _structArray[i].Item5,
                    _classArray[i].Item1, _classArray[i].Item2, _classArray[i].Item3, _classArray[i].Item4, _classArray[i].Item5);
            }

            Measure.Method(() =>
            {
                for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
                {
                    bool found = _generatedContainer.TryGetValue(_testObjects[i],
                        out var playerStats, out var weaponData, out var buffData, out var inventoryItem, out var skillData,
                        out var playerController, out var weaponController, out var buffController,
                        out var inventoryController, out var skillController, out var index);
                }
            })
            .SampleGroup("Generated Container - Sequential Access")
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .Run();
        }

        /// <summary>
        /// Dictionary Containerからの連続的な要素取得パフォーマンスを測定します。
        /// 全要素を順番にアクセスし、実行時間を計測します。
        /// </summary>
        [Test, Performance]
        public void SequentialAccess_DictionaryContainer()
        {
            // データを事前に追加
            for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
            {
                _dictionaryContainer.Add(_testObjects[i],
                    _structArray[i].Item1, _structArray[i].Item2, _structArray[i].Item3, _structArray[i].Item4, _structArray[i].Item5,
                    _classArray[i].Item1, _classArray[i].Item2, _classArray[i].Item3, _classArray[i].Item4, _classArray[i].Item5);
            }

            Measure.Method(() =>
            {
                for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
                {
                    bool found = _dictionaryContainer.TryGetValue(_testObjects[i],
                        out var playerStats, out var weaponData, out var buffData, out var inventoryItem, out var skillData,
                        out var playerController, out var weaponController, out var buffController,
                        out var inventoryController, out var skillController);
                }
            })
            .SampleGroup("Dictionary Container - Sequential Access")
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .Run();
        }

        /// <summary>
        /// Generated Containerからの連続的な要素取得パフォーマンスを測定します。
        /// 全要素を順番にアクセスし、実行時間を計測します。
        /// </summary>
        [Test, Performance]
        public unsafe void SequentialSingleAccess_GeneratedContainer()
        {


            // データを事前に追加
            for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
            {
                _generatedContainer.Add(_testObjects[i],
                    _structArray[i].Item1, _structArray[i].Item2, _structArray[i].Item3, _structArray[i].Item4, _structArray[i].Item5,
                    _classArray[i].Item1, _classArray[i].Item2, _classArray[i].Item3, _classArray[i].Item4, _classArray[i].Item5);
            }

            var stats = _generatedContainer.GetPlayerStatsReadOnly();

            Measure.Method(() =>
            {
                for ( int i = 0; i < stats.Length; i++ )
                {
                    bool found = !stats.Ptr[i].Equals(default);
                }
            })
            .SampleGroup("Generated Container - Sequential Access")
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .Run();
        }

        /// <summary>
        /// Dictionary Containerからの連続的な要素取得パフォーマンスを測定します。
        /// 全要素を順番にアクセスし、実行時間を計測します。
        /// </summary>
        [Test, Performance]
        public void SequentialSingleAccess_DictionaryContainer()
        {
            // データを事前に追加
            for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
            {
                _dictionaryContainer.Add(_testObjects[i],
                    _structArray[i].Item1, _structArray[i].Item2, _structArray[i].Item3, _structArray[i].Item4, _structArray[i].Item5,
                    _classArray[i].Item1, _classArray[i].Item2, _classArray[i].Item3, _classArray[i].Item4, _classArray[i].Item5);
            }

            Measure.Method(() =>
            {
                PlayerStats[] stats = _dictionaryContainer.PlayerStatsDict.Values.ToArray();
                for ( int i = 0; i < stats.Length; i++ )
                {
                    bool found = !stats[i].Equals(default);
                }
            })
            .SampleGroup("Dictionary Container - Sequential Access")
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .Run();
        }

        #endregion

        #region ランダムアクセステスト

        /// <summary>
        /// Generated Containerからのランダムな要素取得パフォーマンスを測定します。
        /// ランダムな順序で要素にアクセスし、キャッシュヒット率が低い状況での性能を評価します。
        /// </summary>
        [UnityTest, Performance]
        public IEnumerator RandomAccess_GeneratedContainer()
        {
            // データを事前に追加
            for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
            {
                _generatedContainer.Add(_testObjects[i],
                    _structArray[i].Item1, _structArray[i].Item2, _structArray[i].Item3, _structArray[i].Item4, _structArray[i].Item5,
                    _classArray[i].Item1, _classArray[i].Item2, _classArray[i].Item3, _classArray[i].Item4, _classArray[i].Item5);
            }

            // ランダムアクセスパターンの生成
            var random = new System.Random(RANDOM_SEED);
            var randomIndices = new int[TEST_OBJECT_COUNT];
            for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
            {
                randomIndices[i] = random.Next(0, TEST_OBJECT_COUNT);
            }

            yield return null;

            Measure.Method(() =>
            {
                for ( int i = 0; i < randomIndices.Length; i++ )
                {
                    int index = randomIndices[i];
                    bool found = _generatedContainer.TryGetValue(_testObjects[index],
                        out var playerStats, out var weaponData, out var buffData, out var inventoryItem, out var skillData,
                        out var playerController, out var weaponController, out var buffController,
                        out var inventoryController, out var skillController, out var dataIndex);
                }
            })
            .SampleGroup("Generated Container - Random Access")
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .Run();
        }

        /// <summary>
        /// Dictionary Containerからのランダムな要素取得パフォーマンスを測定します。
        /// ランダムな順序で要素にアクセスし、キャッシュヒット率が低い状況での性能を評価します。
        /// </summary>
        [UnityTest, Performance]
        public IEnumerator RandomAccess_DictionaryContainer()
        {
            // データを事前に追加
            for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
            {
                _dictionaryContainer.Add(_testObjects[i],
                    _structArray[i].Item1, _structArray[i].Item2, _structArray[i].Item3, _structArray[i].Item4, _structArray[i].Item5,
                    _classArray[i].Item1, _classArray[i].Item2, _classArray[i].Item3, _classArray[i].Item4, _classArray[i].Item5);
            }

            // ランダムアクセスパターンの生成
            var random = new System.Random(RANDOM_SEED);
            var randomIndices = new int[TEST_OBJECT_COUNT];
            for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
            {
                randomIndices[i] = random.Next(0, TEST_OBJECT_COUNT);
            }

            yield return null;

            Measure.Method(() =>
            {
                for ( int i = 0; i < randomIndices.Length; i++ )
                {
                    int index = randomIndices[i];
                    bool found = _dictionaryContainer.TryGetValue(_testObjects[index],
                        out var playerStats, out var weaponData, out var buffData, out var inventoryItem, out var skillData,
                        out var playerController, out var weaponController, out var buffController,
                        out var inventoryController, out var skillController);
                }
            })
            .SampleGroup("Dictionary Container - Random Access")
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .Run();
        }

        #endregion

        #region 要素削除テスト

        /// <summary>
        /// Generated Containerからの要素削除パフォーマンスを測定します。
        /// 全要素を順次削除し、実行時間を計測します。
        /// </summary>
        [Test, Performance]
        public void RemoveOperation_GeneratedContainer()
        {
            Measure.Method(() =>
            {
                // データを削除
                for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
                {
                    _generatedContainer.Remove(_testObjects[i]);
                }
            })
            .SetUp(() =>
            {
                _generatedContainer.Clear();
                // データを追加
                for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
                {
                    _generatedContainer.Add(_testObjects[i],
                        _structArray[i].Item1, _structArray[i].Item2, _structArray[i].Item3, _structArray[i].Item4, _structArray[i].Item5,
                        _classArray[i].Item1, _classArray[i].Item2, _classArray[i].Item3, _classArray[i].Item4, _classArray[i].Item5);
                }
            })
            .SampleGroup("Generated Container - Remove")
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .Run();
        }

        /// <summary>
        /// Dictionary Containerからの要素削除パフォーマンスを測定します。
        /// 全要素を順次削除し、実行時間を計測します。
        /// </summary>
        [Test, Performance]
        public void RemoveOperation_DictionaryContainer()
        {
            Measure.Method(() =>
            {
                // データを削除
                for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
                {
                    _dictionaryContainer.Remove(_testObjects[i]);
                }
            })
            .SetUp(() =>
            {
                _dictionaryContainer.Clear();
                // データを追加
                for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
                {
                    _dictionaryContainer.Add(_testObjects[i],
                        _structArray[i].Item1, _structArray[i].Item2, _structArray[i].Item3, _structArray[i].Item4, _structArray[i].Item5,
                        _classArray[i].Item1, _classArray[i].Item2, _classArray[i].Item3, _classArray[i].Item4, _classArray[i].Item5);
                }
            })
            .SampleGroup("Dictionary Container - Remove")
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .Run();
        }

        #endregion

        #region 内容一致確認テスト

        /// <summary>
        /// 両コンテナの操作の内容が一致することを確認します。
        /// データの正確性と正確性を検証します。
        /// </summary>
        [Test]
        public void VerifyDataConsistency()
        {
            // 両コンテナに同じデータを追加
            for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
            {
                _generatedContainer.Add(_testObjects[i],
                    _structArray[i].Item1, _structArray[i].Item2, _structArray[i].Item3, _structArray[i].Item4, _structArray[i].Item5,
                    _classArray[i].Item1, _classArray[i].Item2, _classArray[i].Item3, _classArray[i].Item4, _classArray[i].Item5);

                _dictionaryContainer.Add(_testObjects[i],
                    _structArray[i].Item1, _structArray[i].Item2, _structArray[i].Item3, _structArray[i].Item4, _structArray[i].Item5,
                    _classArray[i].Item1, _classArray[i].Item2, _classArray[i].Item3, _classArray[i].Item4, _classArray[i].Item5);
            }

            // カウントの確認
            Assert.AreEqual(TEST_OBJECT_COUNT, _generatedContainer.Count, "Generated Container count mismatch");
            Assert.AreEqual(TEST_OBJECT_COUNT, _dictionaryContainer.Count, "Dictionary Container count mismatch");

            // 全要素の値が一致することを確認
            for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
            {
                bool foundGenerated = _generatedContainer.TryGetValue(_testObjects[i],
                    out var genPlayerStats, out var genWeaponData, out var genBuffData, out var genInventoryItem, out var genSkillData,
                    out var genPlayerController, out var genWeaponController, out var genBuffController,
                    out var genInventoryController, out var genSkillController, out int _);

                bool foundDictionary = _dictionaryContainer.TryGetValue(_testObjects[i],
                    out var dictPlayerStats, out var dictWeaponData, out var dictBuffData, out var dictInventoryItem, out var dictSkillData,
                    out var dictPlayerController, out var dictWeaponController, out var dictBuffController,
                    out var dictInventoryController, out var dictSkillController);

                Assert.IsTrue(foundGenerated, $"Generated Container: Object {i} not found");
                Assert.IsTrue(foundDictionary, $"Dictionary Container: Object {i} not found");

                // 構造体の値を比較
                Assert.AreEqual(genPlayerStats.health, dictPlayerStats.health, $"PlayerStats.health mismatch at index {i}");
                Assert.AreEqual(genWeaponData.weaponId, dictWeaponData.weaponId, $"WeaponData.weaponId mismatch at index {i}");
                Assert.AreEqual(genBuffData.buffId, dictBuffData.buffId, $"BuffData.buffId mismatch at index {i}");
                Assert.AreEqual(genInventoryItem.itemId, dictInventoryItem.itemId, $"InventoryItem.itemId mismatch at index {i}");
                Assert.AreEqual(genSkillData.skillId, dictSkillData.skillId, $"SkillData.skillId mismatch at index {i}");

                // クラスの参照を比較
                Assert.AreSame(genPlayerController, dictPlayerController, $"PlayerController reference mismatch at index {i}");
                Assert.AreSame(genWeaponController, dictWeaponController, $"WeaponController reference mismatch at index {i}");
                Assert.AreSame(genBuffController, dictBuffController, $"BuffController reference mismatch at index {i}");
                Assert.AreSame(genInventoryController, dictInventoryController, $"InventoryController reference mismatch at index {i}");
                Assert.AreSame(genSkillController, dictSkillController, $"SkillController reference mismatch at index {i}");
            }

            // 一部削除して再確認
            int removeCount = TEST_OBJECT_COUNT / 4;
            for ( int i = 0; i < removeCount; i++ )
            {
                _generatedContainer.Remove(_testObjects[i]);
                _dictionaryContainer.Remove(_testObjects[i]);
            }

            Assert.AreEqual(TEST_OBJECT_COUNT - removeCount, _generatedContainer.Count, "Generated Container count mismatch after removal");
            Assert.AreEqual(TEST_OBJECT_COUNT - removeCount, _dictionaryContainer.Count, "Dictionary Container count mismatch after removal");

            // 削除されたアイテムが存在しないことを確認
            for ( int i = 0; i < removeCount; i++ )
            {
                bool foundGenerated = _generatedContainer.TryGetValue(_testObjects[i],
                    out _, out _, out _, out _, out _,
                    out _, out _, out _, out _, out _, out _);

                bool foundDictionary = _dictionaryContainer.TryGetValue(_testObjects[i],
                    out _, out _, out _, out _, out _,
                    out _, out _, out _, out _, out _);

                Assert.IsFalse(foundGenerated, $"Generated Container: Removed object {i} still found");
                Assert.IsFalse(foundDictionary, $"Dictionary Container: Removed object {i} still found");
            }
        }

        #endregion

        #region メモリ関連テスト

        /// <summary>
        /// 両コンテナのメモリ使用量を比較します。
        /// 初期化時、データ追加後、削除後の各段階でメモリ使用量を計測します。
        /// </summary>
        [UnityTest, Performance]
        public IEnumerator MemoryEfficiencyComparison()
        {
            ProfilerRecorder gcAllocRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc");

            yield return null;

            // Generated Container のメモリ使用量測定
            gcAllocRecorder.Reset();
            var genContainer = new TestContainer(TEST_OBJECT_COUNT);
            long genInitAlloc = gcAllocRecorder.CurrentValue;

            gcAllocRecorder.Reset();
            for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
            {
                genContainer.Add(_testObjects[i],
                    _structArray[i].Item1, _structArray[i].Item2, _structArray[i].Item3, _structArray[i].Item4, _structArray[i].Item5,
                    _classArray[i].Item1, _classArray[i].Item2, _classArray[i].Item3, _classArray[i].Item4, _classArray[i].Item5);
            }
            long genAddAlloc = gcAllocRecorder.CurrentValue;

            gcAllocRecorder.Reset();
            for ( int i = 0; i < TEST_OBJECT_COUNT / 2; i++ )
            {
                genContainer.Remove(_testObjects[i]);
            }
            long genRemoveAlloc = gcAllocRecorder.CurrentValue;

            genContainer.Dispose();

            yield return null;

            // Dictionary Container のメモリ使用量測定
            gcAllocRecorder.Reset();
            var dictContainer = new DictionaryBasedContainer(TEST_OBJECT_COUNT);
            long dictInitAlloc = gcAllocRecorder.CurrentValue;

            gcAllocRecorder.Reset();
            for ( int i = 0; i < TEST_OBJECT_COUNT; i++ )
            {
                dictContainer.Add(_testObjects[i],
                    _structArray[i].Item1, _structArray[i].Item2, _structArray[i].Item3, _structArray[i].Item4, _structArray[i].Item5,
                    _classArray[i].Item1, _classArray[i].Item2, _classArray[i].Item3, _classArray[i].Item4, _classArray[i].Item5);
            }
            long dictAddAlloc = gcAllocRecorder.CurrentValue;

            gcAllocRecorder.Reset();
            for ( int i = 0; i < TEST_OBJECT_COUNT / 2; i++ )
            {
                dictContainer.Remove(_testObjects[i]);
            }
            long dictRemoveAlloc = gcAllocRecorder.CurrentValue;

            dictContainer.Dispose();

            gcAllocRecorder.Dispose();

            // 結果をログに出力
            Debug.Log($"Memory Allocation Comparison:");
            Debug.Log($"Generated Container - Init: {genInitAlloc} bytes, Add: {genAddAlloc} bytes, Remove: {genRemoveAlloc} bytes");
            Debug.Log($"Dictionary Container - Init: {dictInitAlloc} bytes, Add: {dictAddAlloc} bytes, Remove: {dictRemoveAlloc} bytes");

            // メモリ効率の検証（Generated Containerがメモリ効率的であることを期待）
            Assert.LessOrEqual(genAddAlloc, dictAddAlloc, "Generated Container should allocate less or equal memory during add operations");
        }

        /// <summary>
        /// 両コンテナのGCプレッシャーを比較します。
        /// 繰り返し操作を行い、ガベージコレクションの発生頻度を評価します。
        /// </summary>
        [Test, Performance]
        public void GCPressureComparison()
        {
            const int iterations = 100;

            // Generated Container のGCプレッシャー測定
            Measure.Method(() =>
            {
                for ( int iter = 0; iter < iterations; iter++ )
                {
                    // 追加
                    for ( int i = 0; i < 100; i++ )
                    {
                        _generatedContainer.Add(_testObjects[i],
                            _structArray[i].Item1, _structArray[i].Item2, _structArray[i].Item3, _structArray[i].Item4, _structArray[i].Item5,
                            _classArray[i].Item1, _classArray[i].Item2, _classArray[i].Item3, _classArray[i].Item4, _classArray[i].Item5);
                    }

                    // 取得
                    for ( int i = 0; i < 100; i++ )
                    {
                        _generatedContainer.TryGetValue(_testObjects[i],
                            out _, out _, out _, out _, out _,
                            out _, out _, out _, out _, out _, out _);
                    }

                    // 削除
                    for ( int i = 0; i < 100; i++ )
                    {
                        _generatedContainer.Remove(_testObjects[i]);
                    }
                }
            })
            .SetUp(() =>
            {
                _generatedContainer.Clear();
            })
            .SampleGroup("Generated Container - GC Pressure")
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .GC()
            .Run();

            // Dictionary Container のGCプレッシャー測定
            Measure.Method(() =>
            {
                for ( int iter = 0; iter < iterations; iter++ )
                {
                    // 追加
                    for ( int i = 0; i < 100; i++ )
                    {
                        _dictionaryContainer.Add(_testObjects[i],
                            _structArray[i].Item1, _structArray[i].Item2, _structArray[i].Item3, _structArray[i].Item4, _structArray[i].Item5,
                            _classArray[i].Item1, _classArray[i].Item2, _classArray[i].Item3, _classArray[i].Item4, _classArray[i].Item5);
                    }

                    // 取得
                    for ( int i = 0; i < 100; i++ )
                    {
                        _dictionaryContainer.TryGetValue(_testObjects[i],
                            out _, out _, out _, out _, out _,
                            out _, out _, out _, out _, out _);
                    }

                    // 削除
                    for ( int i = 0; i < 100; i++ )
                    {
                        _dictionaryContainer.Remove(_testObjects[i]);
                    }
                }
            })
            .SetUp(() =>
            {
                _dictionaryContainer.Clear();
            })
            .SampleGroup("Dictionary Container - GC Pressure")
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .GC()
            .Run();
        }

        #endregion
    }
}