using System;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Examples
{
    /// <summary>
    /// FixedSlotContainer の使用例。
    /// プレイヤーのスキルスロットシステムを実装する。
    /// </summary>
    public class FixedSlotContainerExample : MonoBehaviour
    {
        /// <summary>スキルデータ構造体</summary>
        private struct SkillSlotData
        {
            public int SkillId;       // 0 = 空スロット
            public float Cooldown;
            public int Level;
        }

        private const int MaxSlots = 4; // 1キャラクター4スロット

        private FixedSlotContainer<SkillSlotData> _skillSlots;

        private void Awake()
        {
            // 最大64キャラクター、各4スロット
            _skillSlots = new FixedSlotContainer<SkillSlotData>(
                capacity: 64, slotCount: MaxSlots);
        }

        /// <summary>キャラクターを登録（全スロット空で初期化）</summary>
        public void RegisterCharacter(GameObject character)
        {
            _skillSlots.Add(character);
        }

        /// <summary>
        /// スキルをスロットにセット。
        /// </summary>
        public void EquipSkill(GameObject character, int slotIndex, int skillId, int level)
        {
            _skillSlots.SetSlot(character, slotIndex, new SkillSlotData
            {
                SkillId = skillId,
                Cooldown = 0f,
                Level = level
            });
        }

        /// <summary>
        /// refアクセスでクールダウンをインプレース更新。
        /// コピーが発生しないため大量のスロット更新に有利。
        /// </summary>
        public void UseSkill(GameObject character, int slotIndex)
        {
            ref var slot = ref _skillSlots.GetSlotRef(character, slotIndex);
            if (slot.SkillId == 0 || slot.Cooldown > 0f)
            {
                Debug.Log("スキルが使用できません");
                return;
            }

            Debug.Log($"スキル {slot.SkillId} (Lv.{slot.Level}) 発動！");
            slot.Cooldown = 3f; // クールダウン開始
        }

        /// <summary>
        /// カーソル（アクティブスロット）を次に回転。
        /// ゲームパッドのLB/RBでスキル切り替えなどに使える。
        /// </summary>
        public void CycleActiveSkill(GameObject character)
        {
            _skillSlots.RotateCursor(character);
            var current = _skillSlots.GetAtCursor(character);
            Debug.Log($"アクティブスキル切替: ID={current.SkillId}");
        }

        /// <summary>
        /// スキルの入れ替え（ドラッグ＆ドロップUI等）。
        /// </summary>
        public void SwapSkills(GameObject character, int slotA, int slotB)
        {
            _skillSlots.SwapSlots(character, slotA, slotB);
        }

        /// <summary>
        /// 全スロットの一括表示。GetSlotsでSpanとして取得。
        /// </summary>
        public void DisplayAllSlots(GameObject character)
        {
            Span<SkillSlotData> slots = _skillSlots.GetSlots(character);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].SkillId == 0)
                    Debug.Log($"スロット{i}: [空]");
                else
                    Debug.Log($"スロット{i}: スキル{slots[i].SkillId} Lv.{slots[i].Level} CD={slots[i].Cooldown:F1}s");
            }
        }

        public void UnregisterCharacter(GameObject character)
        {
            _skillSlots.Remove(character);
        }

        private void OnDestroy()
        {
            _skillSlots?.Dispose();
        }
    }
}
