using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Tests
{
    /// <summary>
    /// BitFlagTableContainer のユニットテスト
    /// </summary>
    [TestFixture]
    public class BitFlagTableContainerTests
    {
        private List<GameObject> _createdObjects;

        // テスト用フラグ定数
        private const ulong Flag_A = 1UL << 0;
        private const ulong Flag_B = 1UL << 1;
        private const ulong Flag_C = 1UL << 2;
        private const ulong Flag_D = 1UL << 3;
        private const ulong Flag_Max = 1UL << 63;

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

        #region Add / Remove / ContainsKey

        [Test]
        public void Add_SingleEntity_CountIsOne()
        {
            var container = new BitFlagTableContainer(16);
            container.Add(100);

            Assert.AreEqual(1, container.Count);
            Assert.IsTrue(container.ContainsKey(100));
            container.Dispose();
        }

        [Test]
        public void Add_DuplicateHash_ThrowsException()
        {
            var container = new BitFlagTableContainer(16);
            container.Add(100);

            Assert.Throws<InvalidOperationException>(() => container.Add(100));
            container.Dispose();
        }

        [Test]
        public void Add_CapacityFull_ThrowsException()
        {
            var container = new BitFlagTableContainer(2);
            container.Add(1);
            container.Add(2);

            Assert.Throws<InvalidOperationException>(() => container.Add(3));
            container.Dispose();
        }

        [Test]
        public void Add_WithGameObject_Works()
        {
            var container = new BitFlagTableContainer(16);
            var go = CreateGameObject("A");

            container.Add(go);
            Assert.IsTrue(container.ContainsKey(go));
            container.Dispose();
        }

        [Test]
        public void Remove_Existing_ReturnsTrue()
        {
            var container = new BitFlagTableContainer(16);
            container.Add(100);

            Assert.IsTrue(container.Remove(100));
            Assert.AreEqual(0, container.Count);
            container.Dispose();
        }

        [Test]
        public void Remove_NonExistent_ReturnsFalse()
        {
            var container = new BitFlagTableContainer(16);
            Assert.IsFalse(container.Remove(999));
            container.Dispose();
        }

        [Test]
        public void Remove_BackSwapIntegrity()
        {
            var container = new BitFlagTableContainer(16);
            container.Add(1);
            container.Add(2);
            container.Add(3);

            container.SetFlag(1, Flag_A);
            container.SetFlag(2, Flag_B);
            container.SetFlag(3, Flag_C);

            container.Remove(1);

            Assert.AreEqual(2, container.Count);
            Assert.IsFalse(container.ContainsKey(1));
            Assert.IsTrue(container.HasAll(2, Flag_B));
            Assert.IsTrue(container.HasAll(3, Flag_C));
            container.Dispose();
        }

        #endregion

        #region SetFlag / ClearFlag / ToggleFlag

        [Test]
        public void SetFlag_SingleBit_HasAllReturnsTrue()
        {
            var container = new BitFlagTableContainer(16);
            container.Add(100);

            container.SetFlag(100, Flag_A);

            Assert.IsTrue(container.HasAll(100, Flag_A));
            container.Dispose();
        }

        [Test]
        public void SetFlag_MultipleBits_HasAllChecksAll()
        {
            var container = new BitFlagTableContainer(16);
            container.Add(100);

            container.SetFlag(100, Flag_A | Flag_B);

            Assert.IsTrue(container.HasAll(100, Flag_A | Flag_B));
            Assert.IsTrue(container.HasAll(100, Flag_A));
            Assert.IsFalse(container.HasAll(100, Flag_A | Flag_C));
            container.Dispose();
        }

        [Test]
        public void SetFlag_Bit63_Works()
        {
            var container = new BitFlagTableContainer(16);
            container.Add(100);

            container.SetFlag(100, Flag_Max);

            Assert.IsTrue(container.HasAll(100, Flag_Max));
            container.Dispose();
        }

        [Test]
        public void ClearFlag_RemovesBits()
        {
            var container = new BitFlagTableContainer(16);
            container.Add(100);

            container.SetFlag(100, Flag_A | Flag_B | Flag_C);
            container.ClearFlag(100, Flag_B);

            Assert.IsTrue(container.HasAll(100, Flag_A));
            Assert.IsFalse(container.HasAny(100, Flag_B));
            Assert.IsTrue(container.HasAll(100, Flag_C));
            container.Dispose();
        }

        [Test]
        public void ToggleFlag_FlipsBits()
        {
            var container = new BitFlagTableContainer(16);
            container.Add(100);

            container.SetFlag(100, Flag_A);
            container.ToggleFlag(100, Flag_A | Flag_B);

            // Flag_A was on -> off, Flag_B was off -> on
            Assert.IsFalse(container.HasAny(100, Flag_A));
            Assert.IsTrue(container.HasAll(100, Flag_B));
            container.Dispose();
        }

        [Test]
        public void SetFlag_NonExistentHash_ThrowsException()
        {
            var container = new BitFlagTableContainer(16);
            Assert.Throws<KeyNotFoundException>(() => container.SetFlag(999, Flag_A));
            container.Dispose();
        }

        #endregion

        #region HasAll / HasAny

        [Test]
        public void HasAny_WithPartialMatch_ReturnsTrue()
        {
            var container = new BitFlagTableContainer(16);
            container.Add(100);
            container.SetFlag(100, Flag_A);

            Assert.IsTrue(container.HasAny(100, Flag_A | Flag_B));
            container.Dispose();
        }

        [Test]
        public void HasAny_NoMatch_ReturnsFalse()
        {
            var container = new BitFlagTableContainer(16);
            container.Add(100);
            container.SetFlag(100, Flag_A);

            Assert.IsFalse(container.HasAny(100, Flag_B | Flag_C));
            container.Dispose();
        }

        #endregion

        #region GetFlags / AggregateAll

        [Test]
        public void GetFlags_ReturnsCombinedFlags()
        {
            var container = new BitFlagTableContainer(16);
            container.Add(100);

            container.SetFlag(100, Flag_A);
            container.SetFlag(100, Flag_C);

            Assert.AreEqual(Flag_A | Flag_C, container.GetFlags(100));
            container.Dispose();
        }

        [Test]
        public void AggregateAll_ORsAllEntities()
        {
            var container = new BitFlagTableContainer(16);
            container.Add(1);
            container.Add(2);
            container.Add(3);

            container.SetFlag(1, Flag_A);
            container.SetFlag(2, Flag_B);
            container.SetFlag(3, Flag_C);

            Assert.AreEqual(Flag_A | Flag_B | Flag_C, container.AggregateAll());
            container.Dispose();
        }

        [Test]
        public void AggregateAll_Empty_ReturnsZero()
        {
            var container = new BitFlagTableContainer(16);
            Assert.AreEqual(0UL, container.AggregateAll());
            container.Dispose();
        }

        #endregion

        #region QueryHashes

        [Test]
        public void QueryHashes_FiltersCorrectly()
        {
            var container = new BitFlagTableContainer(16);
            container.Add(1);
            container.Add(2);
            container.Add(3);

            container.SetFlag(1, Flag_A);
            container.SetFlag(2, Flag_B);
            container.SetFlag(3, Flag_A | Flag_C);

            Span<int> results = stackalloc int[16];
            int count = container.QueryHashes(Flag_A, results);

            Assert.AreEqual(2, count);
            var hashSet = new HashSet<int>();
            for (int i = 0; i < count; i++)
                hashSet.Add(results[i]);

            Assert.IsTrue(hashSet.Contains(1));
            Assert.IsTrue(hashSet.Contains(3));
            container.Dispose();
        }

        [Test]
        public void QueryHashes_NoMatch_ReturnsZero()
        {
            var container = new BitFlagTableContainer(16);
            container.Add(1);
            container.SetFlag(1, Flag_A);

            Span<int> results = stackalloc int[16];
            int count = container.QueryHashes(Flag_D, results);

            Assert.AreEqual(0, count);
            container.Dispose();
        }

        #endregion

        #region Clear

        [Test]
        public void Clear_ResetsAll()
        {
            var container = new BitFlagTableContainer(16);
            container.Add(1);
            container.SetFlag(1, Flag_A);

            container.Clear();

            Assert.AreEqual(0, container.Count);
            Assert.IsFalse(container.ContainsKey(1));
            container.Dispose();
        }

        #endregion
    }
}
