# Changelog

## [1.0.0] - 2025-03-25

### Added
- ランタイムコンテナ群（ODC.Runtime）
  - PriorityPoolContainer: 優先度ベースプール（FIFO退去対応）
  - SpatialHashContainer2D / 3D: 空間ハッシュコンテナ
  - RingBufferContainer: リングバッファ
  - CooldownContainer: クールダウン管理
  - TimedDataContainer: 時間制限付きデータ
  - NotifyTimedDataContainer: コールバック付き時間制限データ
  - GroupContainer: グループ管理
  - StateMapContainer: 状態マッピング
  - ComponentCache: コンポーネントキャッシュ
- Source Generator（ODC.Attributes）
  - ContainerSetting属性によるコンテナ自動生成
  - struct型はUnsafeListで一括メモリ管理
  - class型は配列で管理
  - BackSwap削除によるO(1)削除
  - ハッシュテーブルによるO(1)ルックアップ
