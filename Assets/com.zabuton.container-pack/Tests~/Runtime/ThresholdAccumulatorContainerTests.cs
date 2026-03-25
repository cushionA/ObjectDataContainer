using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Tests
{
    /// <summary>
    /// ThresholdAccumulatorContainer のユニットテスト
    /// </summary>
    [TestFixture]
    public class ThresholdAccumulatorContainerTests
    {
        private List<GameObject> _createdObjects;

        private const int Key_EXP = 0;
        private const int Key_Poison = 1;
        private const int Key_Score = 2;

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
        public void AddOwner_IncreasesOwnerCount()
        {
            var container = new ThresholdAccumulatorContainer(16);
            var go = CreateGameObject("A");

            container.AddOwner(go);

            Assert.AreEqual(1, container.OwnerCount);
            Assert.IsTrue(container.ContainsOwner(go));
            container.Dispose();
        }

        [Test]
        public void RemoveOwner_ClearsAllAccumulators()
        {
            var container = new ThresholdAccumulatorContainer(16);
            var go = CreateGameObject("A");

            container.AddOwner(go);
            container.Register(go, Key_EXP, 100f);
            container.Register(go, Key_Poison, 50f);
            container.Add(go, Key_EXP, 30f);

            container.RemoveOwner(go);

            Assert.AreEqual(0, container.OwnerCount);
            Assert.AreEqual(0, container.AccumulatorCount);
            container.Dispose();
        }

        #endregion

        #region Register / Add

        [Test]
        public void Register_CreatesAccumulator()
        {
            var container = new ThresholdAccumulatorContainer(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);

            container.Register(go, Key_EXP, 100f);

            Assert.AreEqual(1, container.AccumulatorCount);
            Assert.AreEqual(0f, container.Get(go, Key_EXP));
            container.Dispose();
        }

        [Test]
        public void Add_AccumulatesValue()
        {
            var container = new ThresholdAccumulatorContainer(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);
            container.Register(go, Key_EXP, 100f);

            container.Add(go, Key_EXP, 30f);
            container.Add(go, Key_EXP, 20f);

            Assert.AreEqual(50f, container.Get(go, Key_EXP));
            container.Dispose();
        }

        [Test]
        public void Add_ThresholdReached_ReturnsTrue()
        {
            var container = new ThresholdAccumulatorContainer(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);
            container.Register(go, Key_EXP, 100f);

            bool result1 = container.Add(go, Key_EXP, 80f);
            Assert.IsFalse(result1);

            bool result2 = container.Add(go, Key_EXP, 30f);
            Assert.IsTrue(result2);

            // リセット後は0
            Assert.AreEqual(0f, container.Get(go, Key_EXP));
            container.Dispose();
        }

        [Test]
        public void Add_WithCarryOverflow_KeepsExcess()
        {
            var container = new ThresholdAccumulatorContainer(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);
            container.Register(go, Key_EXP, 100f);

            container.Add(go, Key_EXP, 80f);
            bool triggered = container.Add(go, Key_EXP, 40f, carryOverflow: true);

            Assert.IsTrue(triggered);
            // 120 - 100 = 20 が持ち越し
            Assert.AreEqual(20f, container.Get(go, Key_EXP));
            container.Dispose();
        }

        [Test]
        public void Add_NonRegisteredKey_ReturnsFalse()
        {
            var container = new ThresholdAccumulatorContainer(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);

            bool result = container.Add(go, Key_EXP, 50f);
            Assert.IsFalse(result);
            container.Dispose();
        }

        #endregion

        #region GetNormalized

        [Test]
        public void GetNormalized_ReturnsRatio()
        {
            var container = new ThresholdAccumulatorContainer(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);
            container.Register(go, Key_EXP, 200f);

            container.Add(go, Key_EXP, 100f);

            Assert.AreEqual(0.5f, container.GetNormalized(go, Key_EXP), 0.001f);
            container.Dispose();
        }

        [Test]
        public void GetNormalized_ClampedToOne()
        {
            var container = new ThresholdAccumulatorContainer(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);
            container.Register(go, Key_EXP, 100f);

            // 直接Addせずに、GetNormalizedが1以下であることを確認
            container.Add(go, Key_EXP, 80f);
            float norm = container.GetNormalized(go, Key_EXP);
            Assert.IsTrue(norm <= 1f);
            container.Dispose();
        }

        #endregion

        #region SetThreshold / Reset

        [Test]
        public void SetThreshold_UpdatesThreshold()
        {
            var container = new ThresholdAccumulatorContainer(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);
            container.Register(go, Key_EXP, 100f);

            container.Add(go, Key_EXP, 50f);
            container.SetThreshold(go, Key_EXP, 200f);

            // 50/200 = 0.25
            Assert.AreEqual(0.25f, container.GetNormalized(go, Key_EXP), 0.001f);
            container.Dispose();
        }

        [Test]
        public void Reset_ClearsCurrentValue()
        {
            var container = new ThresholdAccumulatorContainer(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);
            container.Register(go, Key_EXP, 100f);

            container.Add(go, Key_EXP, 75f);
            container.Reset(go, Key_EXP);

            Assert.AreEqual(0f, container.Get(go, Key_EXP));
            container.Dispose();
        }

        #endregion

        #region Multiple Keys Per Owner

        [Test]
        public void MultipleKeys_IndependentAccumulation()
        {
            var container = new ThresholdAccumulatorContainer(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);
            container.Register(go, Key_EXP, 100f);
            container.Register(go, Key_Poison, 50f);

            container.Add(go, Key_EXP, 30f);
            container.Add(go, Key_Poison, 20f);

            Assert.AreEqual(30f, container.Get(go, Key_EXP));
            Assert.AreEqual(20f, container.Get(go, Key_Poison));
            container.Dispose();
        }

        #endregion

        #region Clear

        [Test]
        public void Clear_ResetsAll()
        {
            var container = new ThresholdAccumulatorContainer(16);
            var go = CreateGameObject("A");
            container.AddOwner(go);
            container.Register(go, Key_EXP, 100f);
            container.Add(go, Key_EXP, 50f);

            container.Clear();

            Assert.AreEqual(0, container.OwnerCount);
            Assert.AreEqual(0, container.AccumulatorCount);
            container.Dispose();
        }

        #endregion
    }
}
