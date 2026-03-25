# Changelog

## [1.1.0] - 2026-03-25

### Fixed
- FactionRelationContainer.Tick: BackSwap逆順走査の境界外アクセスを修正
- StackableEffectContainer.RemoveOwner: 同上
- ThresholdAccumulatorContainer.RemoveOwner: 同上

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
  - SparseSetContainer: 疎密集合（Span連続アクセス）
  - ThresholdAccumulatorContainer: 閾値付き蓄積器
  - WeightedSamplerContainer: Walker's Alias法によるO(1)重み付き抽選
  - BitFlagTableContainer: ハッシュキー × ビットフラグテーブル
  - FixedSlotContainer: エンティティ毎の固定Nスロット
  - FactionRelationContainer: 勢力間関係行列（一時オーバーライド対応）
  - HitDeduplicationContainer: ヒット重複排除（貫通対応）
  - FlagComboLookupContainer: フラグ組み合わせ → エフェクト検索
  - ScoredCandidateBuffer: スコア付き候補バッファ
  - MultiPartySequenceContainer: 複数参加者ステップシーケンス
  - StackableEffectContainer: スタック可能な時限エフェクト
- Source Generator（ODC.Attributes）
  - ContainerSetting属性によるコンテナ自動生成
  - struct型はUnsafeListで一括メモリ管理
  - class型は配列で管理
  - BackSwap削除によるO(1)削除
  - ハッシュテーブルによるO(1)ルックアップ
