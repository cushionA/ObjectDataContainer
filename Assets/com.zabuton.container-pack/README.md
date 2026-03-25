# Zabuton Container Pack

Unity向け高性能データコンテナパッケージ。Source Generatorによる自動コード生成と、汎用ランタイムコンテナを提供します。

## 機能

### ランタイムコンテナ (`ODC.Runtime`)
| コンテナ | 用途 |
|----------|------|
| `PriorityPoolContainer<T>` | 優先度ベースのプール。満杯時に最低優先度を退去 |
| `SpatialHashContainer2D/3D` | 空間ハッシュによる近傍検索 |
| `RingBufferContainer<T>` | 固定サイズのリングバッファ |
| `CooldownContainer` | クールダウン管理 |
| `TimedDataContainer<T>` | 時間制限付きデータ管理 |
| `NotifyTimedDataContainer<T>` | 期限切れコールバック付き時間制限データ |
| `GroupContainer` | グループベースのGameObject管理 |
| `StateMapContainer<TKey,TValue>` | 状態マッピング |
| `ComponentCache<T>` | コンポーネントキャッシュ |

### Source Generator (`ODC.Attributes`)
`[ContainerSetting]` 属性を付与した `partial class` に対して、struct/classデータを一括管理する高性能コンテナコードを自動生成します。

```csharp
using ODC.Attributes;

[ContainerSetting(
    structType: new[] { typeof(Health), typeof(Movement) },
    classType: new[] { typeof(AIController) }
)]
public partial class EnemyContainer
{
    public partial void Dispose();
}
```

## インストール

### Git URL（推奨）
Unity Package Manager → Add package from git URL:
```
https://github.com/your-repo/container-pack.git
```

### ローカルパス
Unity Package Manager → Add package from disk → `package.json` を選択

## 動作要件
- Unity 6000.0 以降
- com.unity.collections 2.1.0+
- com.unity.burst 1.8.0+
- com.unity.mathematics 1.3.0+

## ライセンス
[LICENSE.md](LICENSE.md) を参照してください。
