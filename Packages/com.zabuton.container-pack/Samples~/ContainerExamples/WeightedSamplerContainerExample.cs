using System;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Examples
{
    /// <summary>
    /// WeightedSamplerContainer の使用例。
    /// ドロップテーブルの重み付き抽選をO(1)で実行する。
    /// </summary>
    public class WeightedSamplerContainerExample : MonoBehaviour
    {
        /// <summary>ドロップアイテムデータ</summary>
        private struct DropItem
        {
            public int ItemId;
            public int Rarity; // 0=コモン, 1=レア, 2=エピック, 3=レジェンダリー
        }

        private WeightedSamplerContainer<DropItem> _dropTable;

        private void Awake()
        {
            // ドロップアイテムと重みを定義
            var items = new DropItem[]
            {
                new DropItem { ItemId = 1001, Rarity = 0 }, // 回復薬
                new DropItem { ItemId = 1002, Rarity = 0 }, // 素材A
                new DropItem { ItemId = 2001, Rarity = 1 }, // レア武器
                new DropItem { ItemId = 2002, Rarity = 1 }, // レア防具
                new DropItem { ItemId = 3001, Rarity = 2 }, // エピック武器
                new DropItem { ItemId = 4001, Rarity = 3 }, // レジェンダリー
            };

            var weights = new float[]
            {
                40f,  // 回復薬: 40%
                30f,  // 素材A: 30%
                15f,  // レア武器: 15%
                10f,  // レア防具: 10%
                4f,   // エピック武器: 4%
                1f,   // レジェンダリー: 1%
            };

            // Walker's Alias法でO(1)抽選テーブルを構築
            // seed指定でリプレイ可能な乱数にできる
            _dropTable = WeightedSamplerContainer<DropItem>.Build(
                items, weights, seed: (uint)DateTime.Now.Ticks);
        }

        /// <summary>
        /// 敵を倒した時のドロップ抽選。O(1)で実行される。
        /// </summary>
        public DropItem RollDrop()
        {
            return _dropTable.Sample();
        }

        /// <summary>
        /// 宝箱など、重複なしで複数アイテムを抽選する場合。
        /// stackallocでGCフリー。
        /// </summary>
        public void RollTreasureChest(int itemCount)
        {
            // stackallocで一時バッファ確保（GCフリー）
            Span<DropItem> results = stackalloc DropItem[itemCount];
            int count = _dropTable.SampleDistinct(results, itemCount);

            for (int i = 0; i < count; i++)
            {
                Debug.Log($"宝箱アイテム {i + 1}: ID={results[i].ItemId}, レアリティ={results[i].Rarity}");
            }
        }

        private void OnDestroy()
        {
            _dropTable?.Dispose();
        }
    }
}
