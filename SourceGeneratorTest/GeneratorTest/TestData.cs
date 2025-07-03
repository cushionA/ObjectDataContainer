using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using ToolCodeGenerator;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

// テスト用の構造体定義
public struct PlayerStats
{
    public int health;
    public int mana;
    public float speed;
    public Vector3 position;

    public PlayerStats(int health, int mana, float speed, UnityEngine.Vector3 position)
    {
        this.health = health;
        this.mana = mana;
        this.speed = speed;
        this.position = position;
    }

    public override string ToString() => $"PlayerStats(HP:{health}, MP:{mana}, Speed:{speed}, Pos:{position})";
}

public struct WeaponData
{
    public int damage;
    public float attackSpeed;
    public float range;
    public bool isActive;

    public WeaponData(int damage, float attackSpeed, float range, bool isActive = true)
    {
        this.damage = damage;
        this.attackSpeed = attackSpeed;
        this.range = range;
        this.isActive = isActive;
    }

    public override string ToString() => $"WeaponData(DMG:{damage}, AS:{attackSpeed}, Range:{range}, Active:{isActive})";
}

public struct BuffData
{
    public float duration;
    public int effectId;
    public float strength;

    public BuffData(float duration, int effectId, float strength)
    {
        this.duration = duration;
        this.effectId = effectId;
        this.strength = strength;
    }

    public override string ToString() => $"BuffData(Duration:{duration}, ID:{effectId}, Strength:{strength})";
}

// テスト用のクラス定義
public class PlayerController : MonoBehaviour
{
    public string playerName = "";
    public int level = 1;
    public int experience = 0;

    public PlayerController() { }

    public PlayerController(string name, int level = 1)
    {
        playerName = name;
        this.level = level;
    }

    public override string ToString() => $"PlayerController({playerName}, Lv.{level}, Exp:{experience})";
}

public class WeaponController : MonoBehaviour
{
    public string weaponName = "";
    public string weaponType = "";
    public int durability = 100;

    public WeaponController() { }

    public WeaponController(string name, string type = "Sword")
    {
        weaponName = name;
        weaponType = type;
    }

    public override string ToString() => $"WeaponController({weaponName} [{weaponType}], Durability:{durability})";
}

// Source Generatorでコード生成されるコンテナクラス
[ContainerSetting(
    structType: new[] { typeof(PlayerStats), typeof(WeaponData), typeof(BuffData) },
    classType: new[] { typeof(PlayerController), typeof(WeaponController) }
)]
public unsafe partial class TestDataContainer : IDisposable
{
    #region 定数

    /// <summary>
    /// デフォルトの最大容量
    /// </summary>
    private const int DEFAULT_MAX_CAPACITY = 130;

    /// <summary>
    /// バケット数（ハッシュテーブルのサイズ）
    /// </summary>
    private const int BUCKET_COUNT = 191;  // 素数を使用

    #endregion

    #region コンストラクタ

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="maxCapacity">最大容量（デフォルト: 100）</param>
    /// <param name="allocator">メモリアロケータ（デフォルト: Persistent）</param>
    public TestDataContainer(int maxCapacity = DEFAULT_MAX_CAPACITY, Allocator allocator = Allocator.Persistent)
    {
        InitializeContainer(maxCapacity, allocator);
    }

    /// <summary>
    /// コンテナの初期化処理（Source Generatorで実装される）
    /// </summary>
    partial void InitializeContainer(int maxCapacity, Allocator allocator);

    #endregion

    #region ユーティリティ

    /// <summary>
    /// すべてのエントリをクリア
    /// </summary>
    public void Clear()
    {
        ClearAllData();
    }

    /// <summary>
    /// データクリア処理（Source Generatorで実装される）
    /// </summary>
    partial void ClearAllData();

    #endregion

    // ContainsKey と ContainsKeyByHash は生成コードで完全実装される

    #region IDisposable

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        DisposeResources();
    }

    /// <summary>
    /// リソース解放処理（Source Generatorで実装される）
    /// </summary>
    partial void DisposeResources();

    #endregion
}


