using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Tests
{
    /// <summary>
    /// StackableEffectContainer のユニットテスト
    /// </summary>
    [TestFixture]
    public class StackableEffectContainerTests
    {
        private List<GameObject> _createdObjects;

        private struct PoisonEffect
        {
            public float DamagePerTick;
            public int ElementType;
            public PoisonEffect(float dpt, int elem = 0) { DamagePerTick = dpt; ElementType = elem; }
        }

        private const int Key_Poison = 1;
        private const int Key_Burn = 2;
        private const int Key_Shield = 3;
        private const int Key_Regen = 4;

        [SetUp]
        public void SetUp()
        {
            _createdObjects = new List<GameObject>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _createdObjects)
            {
                if (go != null)
                    GameObject.DestroyImmediate(go);
            }
            _createdObjects.Clear();
        }

        private GameObject CreateGameObject(string name = "Test")
        {
            var go = new GameObject(name);
            _createdObjects.Add(go);
            return go;
        }

        #region Owner Management

        [Test]
        public void AddOwner_IncreasesCount()
        {
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");

            container.AddOwner(go);

            Assert.AreEqual(1, container.OwnerCount);
            Assert.IsTrue(container.ContainsOwner(go));
            container.Dispose();
        }

        [Test]
        public void RemoveOwner_ClearsAllEffects()
        {
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");

            container.AddOwner(go);
            container.Apply(go, Key_Poison, new PoisonEffect(10f), duration: 5f);
            container.Apply(go, Key_Burn, new PoisonEffect(5f), duration: 3f);

            container.RemoveOwner(go);

            Assert.AreEqual(0, container.OwnerCount);
            Assert.AreEqual(0, container.EffectCount);
            container.Dispose();
        }

        #endregion

        #region Apply

        [Test]
        public void Apply_NewEffect_AddsEntry()
        {
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);

            bool result = container.Apply(go, Key_Poison, new PoisonEffect(10f), duration: 5f);

            Assert.IsTrue(result);
            Assert.AreEqual(1, container.EffectCount);
            Assert.IsTrue(container.IsActive(go, Key_Poison));
            container.Dispose();
        }

        [Test]
        public void Apply_DuplicateKey_IncreasesStacks()
        {
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);

            container.Apply(go, Key_Poison, new PoisonEffect(10f), addStacks: 1, duration: 5f);
            container.Apply(go, Key_Poison, new PoisonEffect(10f), addStacks: 2, duration: 5f);

            Assert.AreEqual(1, container.EffectCount); // 同一エフェクト
            Assert.AreEqual(3, container.GetStacks(go, Key_Poison));
            container.Dispose();
        }

        [Test]
        public void Apply_MultipleKeys_IndependentEffects()
        {
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);

            container.Apply(go, Key_Poison, new PoisonEffect(10f), duration: 5f);
            container.Apply(go, Key_Burn, new PoisonEffect(5f), duration: 3f);

            Assert.AreEqual(2, container.EffectCount);
            Assert.IsTrue(container.IsActive(go, Key_Poison));
            Assert.IsTrue(container.IsActive(go, Key_Burn));
            container.Dispose();
        }

        #endregion

        #region Remove

        [Test]
        public void Remove_ExistingEffect_ReturnsTrue()
        {
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);
            container.Apply(go, Key_Poison, new PoisonEffect(10f));

            Assert.IsTrue(container.Remove(go, Key_Poison));
            Assert.IsFalse(container.IsActive(go, Key_Poison));
            Assert.AreEqual(0, container.EffectCount);
            container.Dispose();
        }

        [Test]
        public void Remove_NonExistent_ReturnsFalse()
        {
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);

            Assert.IsFalse(container.Remove(go, Key_Poison));
            container.Dispose();
        }

        #endregion

        #region GetStacks / IsActive

        [Test]
        public void GetStacks_ReturnsCorrectCount()
        {
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);

            container.Apply(go, Key_Poison, new PoisonEffect(10f), addStacks: 3);

            Assert.AreEqual(3, container.GetStacks(go, Key_Poison));
            container.Dispose();
        }

        [Test]
        public void GetStacks_NonExistent_ReturnsZero()
        {
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);

            Assert.AreEqual(0, container.GetStacks(go, Key_Poison));
            container.Dispose();
        }

        #endregion

        #region GetActiveEffects / GetActiveCount

        [Test]
        public void GetActiveEffects_ReturnsAll()
        {
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);

            container.Apply(go, Key_Poison, new PoisonEffect(10f), addStacks: 2, duration: 5f);
            container.Apply(go, Key_Burn, new PoisonEffect(5f), addStacks: 1, duration: 3f);

            Span<(int effectKey, PoisonEffect effectData, int stacks, float remainingTime)> results =
                stackalloc (int, PoisonEffect, int, float)[8];

            int count = container.GetActiveEffects(go, results);

            Assert.AreEqual(2, count);

            // effectKeyのセットを確認
            var keys = new HashSet<int>();
            for (int i = 0; i < count; i++)
                keys.Add(results[i].effectKey);

            Assert.IsTrue(keys.Contains(Key_Poison));
            Assert.IsTrue(keys.Contains(Key_Burn));
            container.Dispose();
        }

        [Test]
        public void GetActiveCount_ReturnsCorrectNumber()
        {
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);

            container.Apply(go, Key_Poison, new PoisonEffect(10f));
            container.Apply(go, Key_Burn, new PoisonEffect(5f));
            container.Apply(go, Key_Shield, new PoisonEffect(0f));

            Assert.AreEqual(3, container.GetActiveCount(go));
            container.Dispose();
        }

        #endregion

        #region TickAll

        [Test]
        public void TickAll_ExpiresEffects()
        {
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);

            container.Apply(go, Key_Poison, new PoisonEffect(10f), duration: 2f);

            container.TickAll(1f);
            Assert.IsTrue(container.IsActive(go, Key_Poison));

            container.TickAll(1.5f);
            Assert.IsFalse(container.IsActive(go, Key_Poison));
            container.Dispose();
        }

        [Test]
        public void TickAll_CallsExpireCallback()
        {
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);

            container.Apply(go, Key_Poison, new PoisonEffect(10f), duration: 1f);

            int expiredKey = -1;
            container.TickAll(2f, onExpire: (hash, key, effect) =>
            {
                expiredKey = key;
            });

            Assert.AreEqual(Key_Poison, expiredKey);
            container.Dispose();
        }

        [Test]
        public void TickAll_CallsTickCallback()
        {
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);

            container.Apply(go, Key_Poison, new PoisonEffect(10f), duration: 10f, tickInterval: 0.5f);

            int tickCount = 0;
            // 1秒経過 → 0.5s間隔で2回tickされるはず
            container.TickAll(1f, onTick: (hash, key, effect, stacks) =>
            {
                tickCount++;
            });

            Assert.AreEqual(2, tickCount); // 1.0s / 0.5s = 2回コールバック
            container.Dispose();
        }

        [Test]
        public void TickAll_InfiniteEffect_NeverExpires()
        {
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);

            container.Apply(go, Key_Shield, new PoisonEffect(0f), duration: -1f);

            container.TickAll(1000f);
            Assert.IsTrue(container.IsActive(go, Key_Shield));
            container.Dispose();
        }

        #endregion

        #region Clear

        [Test]
        public void Clear_ResetsAll()
        {
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);
            container.Apply(go, Key_Poison, new PoisonEffect(10f));

            container.Clear();

            Assert.AreEqual(0, container.OwnerCount);
            Assert.AreEqual(0, container.EffectCount);
            container.Dispose();
        }

        #endregion

        #region RemoveOwner with Multiple Effects

        [Test]
        public void RemoveOwner_ThreeOrMoreEffects_ClearsAll()
        {
            // BackSwapで3つ以上のエフェクトが正しく全削除されることを検証
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");
            var go2 = CreateGameObject("B");
            container.AddOwner(go);
            container.AddOwner(go2);

            // goに3つのエフェクトを付与
            container.Apply(go, Key_Poison, new PoisonEffect(10f), duration: 5f);
            container.Apply(go, Key_Regen, new PoisonEffect(5f), duration: 5f);
            container.Apply(go, Key_Shield, new PoisonEffect(50f), duration: -1f);

            // go2にも1つ付与（BackSwapで移動される可能性のある要素）
            container.Apply(go2, Key_Poison, new PoisonEffect(20f), duration: 5f);

            container.RemoveOwner(go);

            Assert.AreEqual(1, container.OwnerCount);
            Assert.AreEqual(1, container.EffectCount);
            Assert.IsTrue(container.IsActive(go2, Key_Poison));
            Assert.IsFalse(container.ContainsOwner(go));
            container.Dispose();
        }

        #endregion

        #region TickAll Multiple Ticks Per Frame

        [Test]
        public void TickAll_LargeDeltaTime_FiresMultipleTicks()
        {
            var container = new StackableEffectContainer<PoisonEffect>(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);

            container.Apply(go, Key_Poison, new PoisonEffect(10f),
                duration: 10f, tickInterval: 0.5f);

            int tickCount = 0;
            container.TickAll(2.0f, onTick: (hash, key, data, stacks) =>
            {
                tickCount++;
            });

            // 2.0s / 0.5s = 4回のtick
            Assert.AreEqual(4, tickCount);
            container.Dispose();
        }

        #endregion
    }
}
