using System;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Examples
{
    /// <summary>
    /// FlagComboLookupContainer の使用例。
    /// 元素の組み合わせによるシナジーエフェクトを検索する。
    /// </summary>
    public class FlagComboLookupContainerExample : MonoBehaviour
    {
        /// <summary>シナジーエフェクトデータ</summary>
        private struct SynergyEffect
        {
            public int EffectId;
            public float DamageMultiplier;
        }

        /// <summary>元素フラグ定義（ビット位置）</summary>
        private const int Elem_Fire = 0;
        private const int Elem_Water = 1;
        private const int Elem_Earth = 2;
        private const int Elem_Wind = 3;
        private const int Elem_Lightning = 4;
        private const int Elem_Ice = 5;

        private FlagComboLookupContainer<SynergyEffect> _synergyTable;

        private void Awake()
        {
            // シナジー組み合わせを定義
            var combos = new ulong[]
            {
                (1UL << Elem_Fire) | (1UL << Elem_Water),      // 火+水 = 蒸気爆発
                (1UL << Elem_Fire) | (1UL << Elem_Wind),       // 火+風 = 炎嵐
                (1UL << Elem_Water) | (1UL << Elem_Lightning), // 水+雷 = 感電
                (1UL << Elem_Earth) | (1UL << Elem_Wind),      // 土+風 = 砂嵐
                (1UL << Elem_Ice) | (1UL << Elem_Fire),        // 氷+火 = 融解
            };

            var effects = new SynergyEffect[]
            {
                new SynergyEffect { EffectId = 1, DamageMultiplier = 2.0f }, // 蒸気爆発
                new SynergyEffect { EffectId = 2, DamageMultiplier = 1.8f }, // 炎嵐
                new SynergyEffect { EffectId = 3, DamageMultiplier = 2.5f }, // 感電
                new SynergyEffect { EffectId = 4, DamageMultiplier = 1.5f }, // 砂嵐
                new SynergyEffect { EffectId = 5, DamageMultiplier = 1.3f }, // 融解
            };

            // 不変の検索テーブルをビルド
            _synergyTable = FlagComboLookupContainer<SynergyEffect>.Build(combos, effects);
        }

        /// <summary>
        /// 現在の元素状態からシナジーを検索。
        /// 例: 敵に火+水が付与されていたら「蒸気爆発」が発動。
        /// スーパーセットもマッチする（火+水+風 → 火+水のシナジーが発動）。
        /// </summary>
        public bool TryGetSynergy(ulong activeElements, out SynergyEffect effect)
        {
            return _synergyTable.TryGet(activeElements, out effect);
        }

        /// <summary>
        /// 複数のシナジーが同時発動する場合（3元素以上）。
        /// GetAllで全マッチをGCフリーで取得。
        /// </summary>
        public void ApplyAllSynergies(ulong activeElements)
        {
            Span<SynergyEffect> results = stackalloc SynergyEffect[8];
            int count = _synergyTable.GetAll(activeElements, results);

            if (count == 0)
            {
                Debug.Log("シナジーなし");
                return;
            }

            float totalMultiplier = 1f;
            for (int i = 0; i < count; i++)
            {
                Debug.Log($"シナジー発動: ID={results[i].EffectId}, 倍率={results[i].DamageMultiplier}x");
                totalMultiplier *= results[i].DamageMultiplier;
            }

            Debug.Log($"合計ダメージ倍率: {totalMultiplier}x");
        }

        private void OnDestroy()
        {
            _synergyTable?.Dispose();
        }
    }
}
