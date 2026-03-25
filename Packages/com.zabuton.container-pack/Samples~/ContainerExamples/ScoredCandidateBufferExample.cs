using System;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Examples
{
    /// <summary>
    /// ScoredCandidateBuffer の使用例。
    /// AIの攻撃対象選定（ターゲティングシステム）を実装する。
    /// </summary>
    public class ScoredCandidateBufferExample : MonoBehaviour
    {
        /// <summary>候補ターゲットのデータ</summary>
        private struct TargetCandidate
        {
            public int EntityHash;
            public float Distance;
            public float ThreatLevel;
        }

        private ScoredCandidateBuffer<TargetCandidate> _targetBuffer;

        [SerializeField] private float _maxRange = 30f;

        private void Awake()
        {
            // 最大32体のAI、各AIが最大8候補まで保持
            _targetBuffer = new ScoredCandidateBuffer<TargetCandidate>(
                ownerCapacity: 32, maxCandidatesPerOwner: 8);
        }

        /// <summary>AIエージェントを登録</summary>
        public void RegisterAgent(GameObject agent)
        {
            _targetBuffer.AddOwner(agent);
        }

        /// <summary>
        /// 毎フレーム呼ばれるターゲット評価処理。
        /// BeginEvaluationで前回フレームの候補をクリアし、新しく評価し直す。
        /// </summary>
        public void EvaluateTargets(GameObject agent, GameObject[] potentialTargets)
        {
            // 前回の候補をクリア
            _targetBuffer.BeginEvaluation(agent);

            var agentPos = agent.transform.position;

            for (int i = 0; i < potentialTargets.Length; i++)
            {
                var target = potentialTargets[i];
                if (target == null) continue;

                float distance = Vector3.Distance(agentPos, target.transform.position);
                if (distance > _maxRange) continue;

                // スコア計算: 近い＋脅威度が高い = 高スコア
                float distanceScore = 1f - (distance / _maxRange);  // 0〜1
                float threatScore = 0.5f; // 実際にはターゲットの攻撃力等から計算
                float totalScore = distanceScore * 0.6f + threatScore * 0.4f;

                // 候補を登録。バッファ満杯なら最低スコアの候補を自動退去。
                _targetBuffer.Submit(agent, new TargetCandidate
                {
                    EntityHash = target.GetHashCode(),
                    Distance = distance,
                    ThreatLevel = threatScore
                }, totalScore);
            }
        }

        /// <summary>
        /// 最優先ターゲットを取得。
        /// </summary>
        public bool GetBestTarget(GameObject agent, out TargetCandidate target, out float score)
        {
            return _targetBuffer.TryGetBest(agent, out target, out score);
        }

        /// <summary>
        /// 上位3体を取得（範囲攻撃のターゲット選定等）。
        /// </summary>
        public int GetTopTargets(GameObject agent, Span<TargetCandidate> results, int maxCount)
        {
            return _targetBuffer.GetTopK(agent, results, maxCount);
        }

        public void UnregisterAgent(GameObject agent)
        {
            _targetBuffer.RemoveOwner(agent);
        }

        private void OnDestroy()
        {
            _targetBuffer?.Dispose();
        }
    }
}
