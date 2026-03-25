# Zabuton Container Pack

[![Unity 6+](https://img.shields.io/badge/Unity-6000.0%2B-blue)](https://unity.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](Assets/com.zabuton.container-pack/LICENSE.md)

Unity向け高性能データコンテナパッケージ。Source Generatorによる自動コード生成と汎用ランタイムコンテナを提供します。

**[English](README.md)**

## 特徴

- **Source Generator**: `[ContainerSetting]` 属性から最適化されたコンテナを自動生成。SoAメモリレイアウトによるゼロアロケーションのAdd/Remove/Get
- **ランタイムコンテナ**: ゲーム開発でよく使うパターン向けの汎用コンテナ21種

| カテゴリ | コンテナ | 用途 |
|---------|----------|------|
| 空間 | `SpatialHashContainer2D / 3D` | 空間ハッシュによる近傍検索 |
| プール | `PriorityPoolContainer<T>` | 優先度ベースのプール（FIFO退去対応） |
| バッファ | `RingBufferContainer<T>` | 固定サイズリングバッファ |
| タイマー | `CooldownContainer` | クールダウン管理 |
| タイマー | `TimedDataContainer<T>` | 自動期限付きデータ |
| タイマー | `NotifyTimedDataContainer<T>` | コールバック付き期限データ |
| 状態 | `StateMapContainer<TState>` | ステートマシン |
| グループ | `GroupContainer<TGroup>` | グループ管理 |
| キャッシュ | `ComponentCache<T>` | GetComponentキャッシュ |
| 集合 | `SparseSetContainer<T>` | 疎密集合 (dense配列 + ハッシュ) |
| 蓄積 | `ThresholdAccumulatorContainer` | 閾値到達判定付き蓄積器 |
| 確率 | `WeightedSamplerContainer<T>` | Walker's Alias法 O(1)重み付き抽選 |
| ビット | `BitFlagTableContainer` | ハッシュ×ビットフラグテーブル |
| スロット | `FixedSlotContainer<T>` | エンティティ毎の固定Nスロット |
| 関係 | `FactionRelationContainer` | 勢力間関係行列 |
| 重複排除 | `HitDeduplicationContainer` | ヒット重複排除 (貫通対応) |
| コンボ | `FlagComboLookupContainer<TEffect>` | フラグ組み合わせ→エフェクト検索 |
| 評価 | `ScoredCandidateBuffer<T>` | スコア付き候補バッファ |
| シーケンス | `MultiPartySequenceContainer<TState>` | 複数参加者シーケンス |
| エフェクト | `StackableEffectContainer<TEffect>` | スタック可能な時限エフェクト |

## インストール

### Git URL（Unity Package Manager）

```
https://github.com/cushionA/ObjectDataContainer.git#package
```

1. **Window > Package Manager** を開く
2. **+** > **Add package from git URL...** をクリック
3. 上記のURLを貼り付け

### 動作要件

- Unity 6000.0 以上
- Burst 1.8.23 以上
- Collections 2.4.3 以上
- Mathematics 1.3.2 以上

## クイックスタート

### Source Generator

```csharp
using ODC.Attributes;

public struct Health { public float hp; public int maxHp; }
public struct Movement { public float speed; }
public class AI : MonoBehaviour { public float hpRate; }

// 高性能コンテナが自動生成される
[ContainerSetting(
    structType: new[] { typeof(Health), typeof(Movement) },
    classType: new[] { typeof(AI) }
)]
public partial class EnemyContainer
{
    public partial void Dispose();
}
```

```csharp
// 使用例
var container = new EnemyContainer(1000);

container.Add(gameObject,
    new Health { hp = 100, maxHp = 100 },
    new Movement { speed = 5f },
    ai
);

if (container.TryGetValue(gameObject, out var health, out var movement, out var ai, out int index))
{
    // ゼロアロケーションでアクセス
}

container.Remove(gameObject);
container.Dispose();
```

### ランタイムコンテナ

```csharp
using ODC.Runtime;

// 優先度プール（満杯時は最低優先度を自動退去）
var pool = new PriorityPoolContainer<BuffData>(maxCapacity: 10);
pool.Add(gameObject, buffData, priority: 5f, duration: 10f);
pool.TryAddOrEvict(newObj, newBuff, out var evicted, priority: 3f);
pool.Update(Time.deltaTime, onExpired: buff => Debug.Log($"期限切れ: {buff}"));
```

## ライセンス

[MIT](Assets/com.zabuton.container-pack/LICENSE.md)
