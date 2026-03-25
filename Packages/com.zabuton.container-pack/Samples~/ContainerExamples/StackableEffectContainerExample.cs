using System;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Examples
{
    /// <summary>
    /// StackableEffectContainer の使用例。
    /// MMO風のバフ/デバフシステムを実装する。
    /// </summary>
    public class StackableEffectContainerExample : MonoBehaviour
    {
        /// <summary>エフェクトデータ構造体</summary>
        private struct EffectData
        {
            public float ValuePerStack;  // 1スタック毎の効果量
            public int EffectType;       // 0=バフ, 1=デバフ, 2=DoT
        }

        /// <summary>エフェクトキー定数</summary>
        private const int Effect_AttackUp = 1;     // 攻撃力アップ
        private const int Effect_DefenseDown = 2;  // 防御力ダウン
        private const int Effect_Poison = 3;       // 毒（DoT）
        private const int Effect_Regen = 4;        // リジェネ（HoT）
        private const int Effect_Shield = 5;       // シールド（永続）

        private StackableEffectContainer<EffectData> _effects;

        private void Awake()
        {
            _effects = new StackableEffectContainer<EffectData>(maxOwners: 128);
        }

        /// <summary>キャラクターを登録</summary>
        public void RegisterCharacter(GameObject character)
        {
            _effects.AddOwner(character);
        }

        /// <summary>
        /// 攻撃力バフを付与。同じバフを重ねがけでスタック増加。
        /// </summary>
        public void ApplyAttackBuff(GameObject character, int stacks, float duration)
        {
            _effects.Apply(character, Effect_AttackUp,
                new EffectData { ValuePerStack = 5f, EffectType = 0 },
                addStacks: stacks, duration: duration);

            int totalStacks = _effects.GetStacks(character, Effect_AttackUp);
            Debug.Log($"攻撃力UP {totalStacks}スタック（+{totalStacks * 5f}）");
        }

        /// <summary>
        /// 毒を付与。tickIntervalで定期的にダメージを与える。
        /// </summary>
        public void ApplyPoison(GameObject character, float dps, float duration)
        {
            _effects.Apply(character, Effect_Poison,
                new EffectData { ValuePerStack = dps, EffectType = 2 },
                addStacks: 1, duration: duration, tickInterval: 1f);
        }

        /// <summary>
        /// 永続エフェクト（duration=-1で無期限）。
        /// </summary>
        public void ApplyPermanentShield(GameObject character)
        {
            _effects.Apply(character, Effect_Shield,
                new EffectData { ValuePerStack = 50f, EffectType = 0 },
                addStacks: 1, duration: -1f);
        }

        /// <summary>
        /// エフェクトを手動で解除（デバフ解除スキル等）。
        /// </summary>
        public void Dispel(GameObject character, int effectKey)
        {
            _effects.Remove(character, effectKey);
        }

        /// <summary>
        /// UIのステータスアイコン表示用。
        /// GetActiveEffectsでGCフリーに全エフェクト情報を取得。
        /// </summary>
        public void UpdateStatusUI(GameObject character)
        {
            Span<(int effectKey, EffectData data, int stacks, float remaining)> buf =
                stackalloc (int, EffectData, int, float)[16];

            int count = _effects.GetActiveEffects(character, buf);

            for (int i = 0; i < count; i++)
            {
                var (key, data, stacks, remaining) = buf[i];
                string type = data.EffectType switch
                {
                    0 => "バフ",
                    1 => "デバフ",
                    2 => "DoT",
                    _ => "不明"
                };

                // 永続エフェクトの場合
                string timeStr = remaining < 0 ? "永続" : $"{remaining:F1}秒";
                Debug.Log($"[{type}] key={key} x{stacks} 効果={data.ValuePerStack * stacks} 残り={timeStr}");
            }
        }

        /// <summary>
        /// アクティブなエフェクト数だけ必要な場合（O(1)で取得）。
        /// </summary>
        public int GetActiveEffectCount(GameObject character)
        {
            return _effects.GetActiveCount(character);
        }

        private void Update()
        {
            // 全エフェクトの時間経過を更新。
            // onTick: 定期ダメージ/回復の適用
            // onExpire: エフェクト終了時の後処理
            _effects.TickAll(Time.deltaTime,
                onTick: (ownerHash, effectKey, data, stacks) =>
                {
                    if (data.EffectType == 2) // DoT
                    {
                        float damage = data.ValuePerStack * stacks;
                        Debug.Log($"毒ダメージ: {damage} (hash={ownerHash})");
                        // 実際にはダメージ適用処理を呼ぶ
                    }
                },
                onExpire: (ownerHash, effectKey, data) =>
                {
                    Debug.Log($"エフェクト終了: key={effectKey} (hash={ownerHash})");
                    // VFX停止、ステータス再計算等
                });
        }

        public void UnregisterCharacter(GameObject character)
        {
            _effects.RemoveOwner(character);
        }

        private void OnDestroy()
        {
            _effects?.Dispose();
        }
    }
}
