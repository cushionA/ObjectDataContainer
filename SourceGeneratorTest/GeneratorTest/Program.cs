
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using UnityEngine;

namespace GeneratorTest
{
    #region
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Source Generator Test Program ===");
            Console.WriteLine();

            var container = new TestDataContainer(maxCapacity: 100);

            try
            {
                //// 基本機能テスト
                //RunBasicTests();

                //// Enum-based API テスト
                //RunEnumBasedTests();

                //// パフォーマンステスト
                //RunPerformanceTests();

                //// エラーハンドリングテスト
                //RunErrorHandlingTests();

                Console.WriteLine("\n✅ All tests completed successfully!");
            }
            catch ( Exception ex )
            {
                Console.WriteLine($"\n❌ Test failed: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        //static void RunBasicTests()
        //{
        //    Console.WriteLine("🧪 Running Basic Functionality Tests...");

        //    using var container = new TestDataContainer(maxCapacity: 100);

        //    // テストデータ作成
        //    var testObjects = CreateTestData(10);

        //    // データ追加テスト
        //    Console.WriteLine("  📝 Testing Add operations...");
        //    for ( int i = 0; i < testObjects.Count; i++ )
        //    {
        //        var obj = testObjects[i];
        //        var playerStats = new PlayerStats(100 - i * 5, 50 + i * 2, 5.0f + i * 0.1f, new Vector3(i, 0, i));
        //        var weaponData = new WeaponData(25 + i * 5, 1.0f + i * 0.1f, 3.0f, true);
        //        var buffData = new BuffData(10.0f + i, i % 3, 1.5f);
        //        var playerController = obj.GetComponent<PlayerController>();
        //        var weaponController = obj.GetComponent<WeaponController>();

        //        int index = container.Add(obj, playerStats, weaponData, buffData, playerController, weaponController);
        //        Console.WriteLine($"    Added entity {i} at index {index}");
        //    }

        //    Console.WriteLine($"  📊 Container stats: Count={container.Count}, Usage={container.UsageRatio:P}");

        //    // データ取得テスト
        //    Console.WriteLine("  🔍 Testing Get operations...");
        //    var firstObj = testObjects[0];
        //    if ( container.TryGetValue(firstObj, out PlayerStats stats, out WeaponData weapon,
        //        out BuffData buff, out PlayerController player, out WeaponController weaponCtrl, out int idx) )
        //    {
        //        Console.WriteLine($"    Retrieved data for {player.playerName}:");
        //        Console.WriteLine($"      {stats}");
        //        Console.WriteLine($"      {weapon}");
        //        Console.WriteLine($"      {buff}");
        //    }

        //    // データ更新テスト
        //    Console.WriteLine("  ✏️ Testing Update operations...");
        //    ref PlayerStats statsRef = ref container.GetPlayerStatsByIndex(0);
        //    int originalHealth = statsRef.health;
        //    statsRef.health = 999;
        //    Console.WriteLine($"    Updated health: {originalHealth} -> {statsRef.health}");

        //    // データ削除テスト
        //    Console.WriteLine("  🗑️ Testing Remove operations...");
        //    int originalCount = container.Count;
        //    bool removed = container.Remove(testObjects[5]);
        //    Console.WriteLine($"    Removed entity: {removed}, Count: {originalCount} -> {container.Count}");

        //    Console.WriteLine("  ✅ Basic tests completed");
        //    Console.WriteLine();
        //}

        //static void RunEnumBasedTests()
        //{
        //    Console.WriteLine("🔢 Running Enum-based API Tests...");

        //    using var container = new TestDataContainer(maxCapacity: 50);
        //    var testObj = CreateTestData(1)[0];

        //    // データ追加
        //    var playerStats = new PlayerStats(100, 50, 5.0f, Vector3.zero);
        //    var weaponData = new WeaponData(50, 1.5f, 4.0f, true);
        //    var buffData = new BuffData(15.0f, 1, 2.0f);
        //    var playerController = testObj.GetComponent<PlayerController>();
        //    var weaponController = testObj.GetComponent<WeaponController>();

        //    container.Add(testObj, playerStats, weaponData, buffData, playerController, weaponController);
        //    int hash = testObj.GetHashCode();

        //    // Generic Enum-based API テスト
        //    Console.WriteLine("  🎯 Testing Generic Enum-based API...");
        //    if ( container.TryGetStructData<PlayerStats>(hash, TestDataContainer.StructDataType.PlayerStats, out PlayerStats retrievedStats) )
        //    {
        //        Console.WriteLine($"    Retrieved PlayerStats: {retrievedStats}");
        //    }

        //    if ( container.TryGetStructData<WeaponData>(hash, TestDataContainer.StructDataType.WeaponData, out WeaponData retrievedWeapon) )
        //    {
        //        Console.WriteLine($"    Retrieved WeaponData: {retrievedWeapon}");
        //    }

        //    if ( container.TryGetClassData<PlayerController>(hash, TestDataContainer.ClassDataType.PlayerController, out PlayerController retrievedPlayer) )
        //    {
        //        Console.WriteLine($"    Retrieved PlayerController: {retrievedPlayer}");
        //    }

        //    // Type-specific API テスト
        //    Console.WriteLine("  🚀 Testing Type-specific API...");
        //    ref PlayerStats directStats = ref container.GetPlayerStatsRef(hash);
        //    Console.WriteLine($"    Direct access PlayerStats: {directStats}");
        //    directStats.health = 150;
        //    Console.WriteLine($"    Modified health to: {directStats.health}");

        //    if ( container.TryGetPlayerController(hash, out PlayerController directPlayer) )
        //    {
        //        Console.WriteLine($"    Direct access PlayerController: {directPlayer}");
        //    }

        //    WeaponController directWeapon = container.GetWeaponController(hash);
        //    Console.WriteLine($"    Direct access WeaponController: {directWeapon}");

        //    Console.WriteLine("  ✅ Enum-based tests completed");
        //    Console.WriteLine();
        //}

        //static void RunPerformanceTests()
        //{
        //    Console.WriteLine("⚡ Running Performance Tests...");

        //    const int testSize = 10000;
        //    using var container = new TestDataContainer(maxCapacity: testSize);
        //    var testObjects = CreateTestData(testSize);

        //    // 追加のパフォーマンステスト
        //    Console.WriteLine($"  📈 Testing Add performance with {testSize} entities...");
        //    var stopwatch = Stopwatch.StartNew();

        //    for ( int i = 0; i < testSize; i++ )
        //    {
        //        var obj = testObjects[i];
        //        var playerStats = new PlayerStats(100, 50, 5.0f, new Vector3(i, 0, i));
        //        var weaponData = new WeaponData(25, 1.0f, 3.0f, true);
        //        var buffData = new BuffData(10.0f, 1, 1.5f);
        //        var playerController = obj.GetComponent<PlayerController>();
        //        var weaponController = obj.GetComponent<WeaponController>();

        //        container.Add(obj, playerStats, weaponData, buffData, playerController, weaponController);
        //    }

        //    stopwatch.Stop();
        //    Console.WriteLine($"    Add operations: {stopwatch.ElapsedMilliseconds}ms ({testSize / stopwatch.Elapsed.TotalSeconds:F0} ops/sec)");

        //    // アクセスのパフォーマンステスト
        //    Console.WriteLine("  🔍 Testing access performance...");
        //    stopwatch.Restart();

        //    for ( int i = 0; i < testSize; i++ )
        //    {
        //        ref PlayerStats stats = ref container.GetPlayerStatsByIndex(i);
        //        stats.health = stats.health + 1; // 簡単な変更
        //    }

        //    stopwatch.Stop();
        //    Console.WriteLine($"    Direct access operations: {stopwatch.ElapsedMilliseconds}ms ({testSize / stopwatch.Elapsed.TotalSeconds:F0} ops/sec)");

        //    // Enum-based アクセスのパフォーマンス
        //    stopwatch.Restart();
        //    var hashes = new int[Math.Min(1000, testSize)];
        //    for ( int i = 0; i < hashes.Length; i++ )
        //    {
        //        hashes[i] = testObjects[i].GetHashCode();
        //    }

        //    foreach ( var hash in hashes )
        //    {
        //        if ( container.TryGetStructData<PlayerStats>(hash, TestDataContainer.StructDataType.PlayerStats, out PlayerStats stats) )
        //        {
        //            // データアクセステスト
        //        }
        //    }

        //    stopwatch.Stop();
        //    Console.WriteLine($"    Enum-based access: {stopwatch.ElapsedMilliseconds}ms ({hashes.Length / stopwatch.Elapsed.TotalSeconds:F0} ops/sec)");

        //    Console.WriteLine("  ✅ Performance tests completed");
        //    Console.WriteLine();
        //}

        //static void RunErrorHandlingTests()
        //{
        //    Console.WriteLine("🛡️ Running Error Handling Tests...");

        //    using var container = new TestDataContainer(maxCapacity: 10);

        //    // 存在しないハッシュでのアクセステスト
        //    Console.WriteLine("  ❓ Testing non-existent hash access...");
        //    int fakeHash = 99999;

        //    if ( !container.TryGetStructData<PlayerStats>(fakeHash, TestDataContainer.StructDataType.PlayerStats, out PlayerStats stats) )
        //    {
        //        Console.WriteLine("    ✅ Correctly returned false for non-existent hash");
        //    }

        //    // 範囲外インデックスアクセステスト
        //    Console.WriteLine("  📏 Testing out-of-range index access...");
        //    try
        //    {
        //        ref PlayerStats invalidStats = ref container.GetPlayerStatsByIndex(100);
        //        Console.WriteLine("    ❌ Should have thrown exception");
        //    }
        //    catch ( ArgumentOutOfRangeException )
        //    {
        //        Console.WriteLine("    ✅ Correctly threw ArgumentOutOfRangeException");
        //    }

        //    // null GameObjectテスト
        //    Console.WriteLine("  🚫 Testing null GameObject handling...");
        //    if ( !container.Remove(null) )
        //    {
        //        Console.WriteLine("    ✅ Correctly handled null GameObject");
        //    }

        //    Console.WriteLine("  ✅ Error handling tests completed");
        //    Console.WriteLine();
        //}

        //static List<GameObject> CreateTestData(int count)
        //{
        //    var objects = new List<GameObject>();

        //    for ( int i = 0; i < count; i++ )
        //    {
        //        var obj = new GameObject($"TestEntity_{i}");
        //        obj.transform.position = new Vector3(i * 2, 0, i % 5);

        //        var playerController = obj.AddComponent<PlayerController>();
        //        playerController.playerName = $"Player_{i}";
        //        playerController.level = 1 + i / 5;
        //        playerController.experience = i * 100;

        //        var weaponController = obj.AddComponent<WeaponController>();
        //        weaponController.weaponName = $"Weapon_{i}";
        //        weaponController.weaponType = (i % 3) switch
        //        {
        //            0 => "Sword",
        //            1 => "Bow",
        //            _ => "Staff"
        //        };
        //        weaponController.durability = 100 - (i % 20);

        //        objects.Add(obj);
        //    }

        //    return objects;
        //}
    }

    #endregion
}