using System;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Examples
{
    /// <summary>
    /// BitFlagTableContainer の使用例。
    /// キャラクターの状態異常フラグをビット演算で高速管理する。
    /// </summary>
    public class BitFlagTableContainerExample : MonoBehaviour
    {
        /// <summary>状態異常のビットインデックス定義</summary>
        private const int Flag_Poison = 0;
        private const int Flag_Burn = 1;
        private const int Flag_Freeze = 2;
        private const int Flag_Stun = 3;
        private const int Flag_Silence = 4;
        private const int Flag_Blind = 5;

        /// <summary>マスク定義: 行動不能系の状態異常</summary>
        private const ulong Mask_Incapacitated = (1UL << Flag_Freeze) | (1UL << Flag_Stun);

        /// <summary>マスク定義: DoT（持続ダメージ）系</summary>
        private const ulong Mask_DoT = (1UL << Flag_Poison) | (1UL << Flag_Burn);

        private BitFlagTableContainer _statusFlags;

        private void Awake()
        {
            _statusFlags = new BitFlagTableContainer(capacity: 256);
        }

        /// <summary>キャラクターを登録</summary>
        public void Register(GameObject character)
        {
            _statusFlags.Add(character);
        }

        /// <summary>状態異常を付与</summary>
        public void ApplyStatus(GameObject character, int flagIndex)
        {
            _statusFlags.SetFlag(character, 1UL << flagIndex);
        }

        /// <summary>状態異常を解除</summary>
        public void RemoveStatus(GameObject character, int flagIndex)
        {
            _statusFlags.ClearFlag(character, 1UL << flagIndex);
        }

        /// <summary>
        /// キャラクターが行動不能かどうかを判定。
        /// HasAnyで複数ビットのいずれかがセットされているか高速判定。
        /// </summary>
        public bool IsIncapacitated(GameObject character)
        {
            return _statusFlags.HasAny(character, Mask_Incapacitated);
        }

        /// <summary>
        /// DoT系の状態異常を全て持っているか（ビット全一致）。
        /// 例: 毒＋炎上の相乗効果判定。
        /// </summary>
        public bool HasAllDoTs(GameObject character)
        {
            return _statusFlags.HasAll(character, Mask_DoT);
        }

        /// <summary>
        /// DoT系の状態異常を持つ全キャラクターを取得。
        /// QueryHashesでマスクに一致するエントリをSpanに書き出す。
        /// </summary>
        public void ProcessDoTDamage()
        {
            Span<int> affectedHashes = stackalloc int[64];
            int count = _statusFlags.QueryHashes(Mask_DoT, affectedHashes);

            for (int i = 0; i < count; i++)
            {
                // 各キャラクターにDoTダメージを適用
                // affectedHashes[i] がGameObjectのハッシュ値
                Debug.Log($"DoTダメージ適用: hash={affectedHashes[i]}");
            }
        }

        /// <summary>
        /// デバッグ用: 全キャラクターのフラグをOR集約して
        /// 現在フィールド上に存在する状態異常の種類を確認。
        /// </summary>
        public ulong GetAllActiveStatusTypes()
        {
            return _statusFlags.AggregateAll();
        }

        public void Unregister(GameObject character)
        {
            _statusFlags.Remove(character);
        }

        private void OnDestroy()
        {
            _statusFlags?.Dispose();
        }
    }
}
