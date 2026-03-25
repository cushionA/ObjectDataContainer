using UnityEngine;
using ODC.Runtime;

namespace ODC.Examples
{
    /// <summary>
    /// MultiPartySequenceContainer の使用例。
    /// 2人協力コンボ（合体技）のシーケンスを管理する。
    /// </summary>
    public class MultiPartySequenceContainerExample : MonoBehaviour
    {
        /// <summary>コンボの共有状態</summary>
        private struct ComboState
        {
            public int ComboId;
            public int TotalDamage;
            public int CurrentPhase;
        }

        private MultiPartySequenceContainer<ComboState> _comboSequences;

        private void Awake()
        {
            // 最大8つの同時コンボシーケンスを管理
            _comboSequences = new MultiPartySequenceContainer<ComboState>(maxSequences: 8);
        }

        /// <summary>
        /// 協力コンボを開始する。
        /// 2人のプレイヤーが3ステップを順番にクリアする必要がある。
        /// 各ステップには5秒の入力猶予がある。
        /// </summary>
        /// <param name="player1">プレイヤー1</param>
        /// <param name="player2">プレイヤー2</param>
        /// <param name="comboId">コンボの種類</param>
        /// <returns>シーケンスID</returns>
        public int StartCoopCombo(GameObject player1, GameObject player2, int comboId)
        {
            int seqId = _comboSequences.Begin(
                participantHashes: new[] { player1.GetHashCode(), player2.GetHashCode() },
                totalSteps: 3,
                windowPerStep: 5f,
                initialState: new ComboState
                {
                    ComboId = comboId,
                    TotalDamage = 0,
                    CurrentPhase = 0
                });

            Debug.Log($"協力コンボ開始！ ID={comboId}, シーケンス={seqId}");
            return seqId;
        }

        /// <summary>
        /// プレイヤーがコンボ入力を行う。
        /// 全参加者が入力すると次のステップに進む。
        /// </summary>
        /// <param name="sequenceId">シーケンスID</param>
        /// <param name="player">入力したプレイヤー</param>
        /// <param name="damage">このステップでのダメージ寄与</param>
        /// <returns>全員が入力してステップが進んだ場合true</returns>
        public bool PlayerInput(int sequenceId, GameObject player, int damage)
        {
            // 現在の状態を取得
            if (!_comboSequences.TryGetSequence(sequenceId, out var state, out int step, out bool completed))
                return false;

            if (completed)
            {
                Debug.Log("コンボは既に完了しています");
                return false;
            }

            // 新しい状態を作成（ダメージを加算）
            var newState = new ComboState
            {
                ComboId = state.ComboId,
                TotalDamage = state.TotalDamage + damage,
                CurrentPhase = step + 1
            };

            // 参加者のadvanceを記録
            bool stepped = _comboSequences.Advance(sequenceId, player.GetHashCode(), newState);

            if (stepped)
            {
                Debug.Log($"ステップ {step + 1} 完了！ 合計ダメージ: {newState.TotalDamage}");

                // コンボ完了チェック
                if (_comboSequences.IsCompleted(sequenceId))
                {
                    Debug.Log($"協力コンボ完成！ 最終ダメージ: {newState.TotalDamage}");
                    // コンボ演出の再生、ダメージ適用等
                }
            }
            else
            {
                Debug.Log($"プレイヤー {player.name} が入力。相方の入力を待機中...");
            }

            return stepped;
        }

        /// <summary>
        /// コンボを手動でキャンセル。
        /// </summary>
        public void CancelCombo(int sequenceId)
        {
            _comboSequences.End(sequenceId);
            Debug.Log("コンボがキャンセルされました");
        }

        private void Update()
        {
            // タイムアウト管理: 時間内に全員が入力しなかった場合、
            // シーケンスが自動的に削除される。
            _comboSequences.Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            _comboSequences?.Dispose();
        }
    }
}
