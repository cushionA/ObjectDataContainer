# ObjectDataContainer (Zabuton Container Pack)

効率的にオブジェクトのデータを管理するUnity用ツール。

## 機能

### Source Generator
`[ContainerSetting]` 属性から高性能なゼロアロケーションコンテナを自動生成。SoA メモリレイアウトと `UnsafeList<T>` によるキャッシュフレンドリーな連続アクセスを実現。

### ランタイムコンテナ (21種)
ゲーム開発でよく使うパターン向けの汎用コンテナ。全て `ODC.Runtime` 名前空間。

| カテゴリ | コンテナ | 概要 |
|---------|---------|------|
| 空間 | `SpatialHashContainer2D / 3D` | 空間ハッシュによる近傍検索 |
| プール | `PriorityPoolContainer<T>` | 優先度ベースのオブジェクトプール |
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

## ドキュメント

- [Wiki](https://github.com/cushionA/ObjectDataContainer/wiki)
- [Qiita 解説記事](https://qiita.com/cushionA/items/c86d2eceb3c11c56ca7f)

## インストール

Unity Package Manager → Add package from git URL:
```
https://github.com/cushionA/ObjectDataContainer.git
```

## ライセンス

MIT License
