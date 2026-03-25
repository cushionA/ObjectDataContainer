using System;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Examples
{
    /// <summary>
    /// FactionRelationContainer の使用例。
    /// ゲーム内の勢力間関係（敵対・中立・同盟）を管理する。
    /// </summary>
    public class FactionRelationContainerExample : MonoBehaviour
    {
        private FactionRelationContainer _factions;

        // 勢力ID
        private int _playerFaction;
        private int _goblinFaction;
        private int _undeadFaction;
        private int _merchantFaction;

        private void Awake()
        {
            _factions = new FactionRelationContainer();

            // 勢力を登録（最大8勢力）
            _playerFaction = _factions.RegisterFaction();
            _goblinFaction = _factions.RegisterFaction();
            _undeadFaction = _factions.RegisterFaction();
            _merchantFaction = _factions.RegisterFaction();

            // 初期関係を設定（Setは対称: A→BもB→Aも同時に設定される）
            _factions.Set(_playerFaction, _goblinFaction, Relation.Hostile);
            _factions.Set(_playerFaction, _undeadFaction, Relation.Hostile);
            _factions.Set(_playerFaction, _merchantFaction, Relation.Allied);
            _factions.Set(_goblinFaction, _undeadFaction, Relation.Neutral);
            _factions.Set(_goblinFaction, _merchantFaction, Relation.Hostile);
        }

        /// <summary>
        /// AIがターゲットに攻撃すべきかを判定。
        /// </summary>
        public bool ShouldAttack(int attackerFaction, int targetFaction)
        {
            return _factions.Get(attackerFaction, targetFaction) == Relation.Hostile;
        }

        /// <summary>
        /// 特定勢力の全敵対勢力を取得。
        /// AIのターゲット選定に使用。
        /// </summary>
        public void FindEnemies(int factionId)
        {
            Span<int> enemies = stackalloc int[8];
            int count = _factions.GetHostile(factionId, enemies);

            Debug.Log($"勢力{factionId}の敵対勢力数: {count}");
            for (int i = 0; i < count; i++)
            {
                Debug.Log($"  敵対勢力: {enemies[i]}");
            }
        }

        /// <summary>
        /// 一時的な同盟（イベント等）。
        /// 指定時間後に自動的に元の関係に戻る。
        /// </summary>
        public void FormTemporaryAlliance(int factionA, int factionB, float duration)
        {
            // 例: ゴブリンと一時同盟（30秒間）
            _factions.SetTemporary(factionA, factionB, Relation.Allied, duration);
            Debug.Log($"勢力{factionA}と{factionB}が{duration}秒間の一時同盟を締結");
        }

        /// <summary>
        /// 非対称な関係変更（片方だけ）。
        /// 例: ゴブリンはプレイヤーを中立と見なすが、プレイヤーは引き続き敵対視。
        /// </summary>
        public void SetOneWayNeutral()
        {
            _factions.SetOneWay(_goblinFaction, _playerFaction, Relation.Neutral);
        }

        private void Update()
        {
            // 一時関係の時間経過を更新。
            // 期限切れの一時関係は自動的に元の関係に復元される。
            _factions.Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            _factions?.Dispose();
        }
    }
}
