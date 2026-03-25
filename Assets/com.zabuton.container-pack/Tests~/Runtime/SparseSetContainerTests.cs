using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Tests
{
    /// <summary>
    /// SparseSetContainer のユニットテスト
    /// </summary>
    [TestFixture]
    public class SparseSetContainerTests
    {
        private List<GameObject> _createdObjects;

        private struct TestValue
        {
            public int Id;
            public float Score;

            public TestValue(int id, float score = 0f)
            {
                Id = id;
                Score = score;
            }
        }

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

        #region Set / Count

        [Test]
        public void Set_SingleElement_CountIsOne()
        {
            var container = new SparseSetContainer<TestValue>(16);
            container.Set(100, new TestValue(1));

            Assert.AreEqual(1, container.Count);
            container.Dispose();
        }

        [Test]
        public void Set_MultipleElements_CountIncreases()
        {
            var container = new SparseSetContainer<TestValue>(16);
            container.Set(1, new TestValue(1));
            container.Set(2, new TestValue(2));
            container.Set(3, new TestValue(3));

            Assert.AreEqual(3, container.Count);
            container.Dispose();
        }

        [Test]
        public void Set_DuplicateHash_OverwritesValue()
        {
            var container = new SparseSetContainer<TestValue>(16);
            container.Set(100, new TestValue(1, 10f));
            container.Set(100, new TestValue(1, 99f));

            Assert.AreEqual(1, container.Count);
            Assert.IsTrue(container.TryGet(100, out var val));
            Assert.AreEqual(99f, val.Score);
            container.Dispose();
        }

        [Test]
        public void Set_CapacityFull_ThrowsException()
        {
            var container = new SparseSetContainer<TestValue>(2);
            container.Set(1, new TestValue(1));
            container.Set(2, new TestValue(2));

            Assert.Throws<InvalidOperationException>(() => container.Set(3, new TestValue(3)));
            container.Dispose();
        }

        [Test]
        public void Set_WithGameObject_Works()
        {
            var container = new SparseSetContainer<TestValue>(16);
            var go = CreateGameObject("A");

            container.Set(go, new TestValue(1));

            Assert.AreEqual(1, container.Count);
            Assert.IsTrue(container.Contains(go));
            container.Dispose();
        }

        #endregion

        #region Remove

        [Test]
        public void Remove_ExistingElement_ReturnsTrue()
        {
            var container = new SparseSetContainer<TestValue>(16);
            container.Set(100, new TestValue(1));

            Assert.IsTrue(container.Remove(100));
            Assert.AreEqual(0, container.Count);
            container.Dispose();
        }

        [Test]
        public void Remove_NonExistent_ReturnsFalse()
        {
            var container = new SparseSetContainer<TestValue>(16);
            Assert.IsFalse(container.Remove(999));
            container.Dispose();
        }

        [Test]
        public void Remove_BackSwapIntegrity()
        {
            var container = new SparseSetContainer<TestValue>(16);
            container.Set(1, new TestValue(1));
            container.Set(2, new TestValue(2));
            container.Set(3, new TestValue(3));

            // 先頭を削除 → 末尾(hash=3)が先頭に移動
            container.Remove(1);

            Assert.AreEqual(2, container.Count);
            Assert.IsFalse(container.Contains(1));
            Assert.IsTrue(container.TryGet(2, out var val2));
            Assert.AreEqual(2, val2.Id);
            Assert.IsTrue(container.TryGet(3, out var val3));
            Assert.AreEqual(3, val3.Id);
            container.Dispose();
        }

        [Test]
        public void Remove_WithGameObject_Works()
        {
            var container = new SparseSetContainer<TestValue>(16);
            var go = CreateGameObject("A");

            container.Set(go, new TestValue(1));
            Assert.IsTrue(container.Remove(go));
            Assert.AreEqual(0, container.Count);
            container.Dispose();
        }

        #endregion

        #region Contains

        [Test]
        public void Contains_Existing_ReturnsTrue()
        {
            var container = new SparseSetContainer<TestValue>(16);
            container.Set(42, new TestValue(1));

            Assert.IsTrue(container.Contains(42));
            container.Dispose();
        }

        [Test]
        public void Contains_NonExistent_ReturnsFalse()
        {
            var container = new SparseSetContainer<TestValue>(16);
            Assert.IsFalse(container.Contains(42));
            container.Dispose();
        }

        #endregion

        #region TryGet

        [Test]
        public void TryGet_Existing_ReturnsTrueAndValue()
        {
            var container = new SparseSetContainer<TestValue>(16);
            container.Set(10, new TestValue(5, 3.14f));

            Assert.IsTrue(container.TryGet(10, out var val));
            Assert.AreEqual(5, val.Id);
            Assert.AreEqual(3.14f, val.Score);
            container.Dispose();
        }

        [Test]
        public void TryGet_NonExistent_ReturnsFalse()
        {
            var container = new SparseSetContainer<TestValue>(16);
            Assert.IsFalse(container.TryGet(999, out _));
            container.Dispose();
        }

        #endregion

        #region GetRef

        [Test]
        public void GetRef_MutateValue_ReflectedInContainer()
        {
            var container = new SparseSetContainer<TestValue>(16);
            container.Set(10, new TestValue(1, 0f));

            ref TestValue valRef = ref container.GetRef(10);
            valRef.Score = 100f;

            Assert.IsTrue(container.TryGet(10, out var val));
            Assert.AreEqual(100f, val.Score);
            container.Dispose();
        }

        [Test]
        public void GetRef_NonExistent_ThrowsKeyNotFoundException()
        {
            var container = new SparseSetContainer<TestValue>(16);
            Assert.Throws<KeyNotFoundException>(() => container.GetRef(999));
            container.Dispose();
        }

        #endregion

        #region ActiveValues / ActiveHashes

        [Test]
        public void ActiveValues_ReturnsContiguousSpan()
        {
            var container = new SparseSetContainer<TestValue>(16);
            container.Set(1, new TestValue(10));
            container.Set(2, new TestValue(20));
            container.Set(3, new TestValue(30));

            var span = container.ActiveValues;
            Assert.AreEqual(3, span.Length);

            // 値が全て含まれていることを確認（順序はBackSwapにより保証されない場合がある）
            var ids = new HashSet<int>();
            for (int i = 0; i < span.Length; i++)
                ids.Add(span[i].Id);

            Assert.IsTrue(ids.Contains(10));
            Assert.IsTrue(ids.Contains(20));
            Assert.IsTrue(ids.Contains(30));
            container.Dispose();
        }

        [Test]
        public void ActiveHashes_MatchesActiveValues()
        {
            var container = new SparseSetContainer<TestValue>(16);
            container.Set(100, new TestValue(1));
            container.Set(200, new TestValue(2));

            var hashes = container.ActiveHashes;
            Assert.AreEqual(2, hashes.Length);

            var hashSet = new HashSet<int>();
            for (int i = 0; i < hashes.Length; i++)
                hashSet.Add(hashes[i]);

            Assert.IsTrue(hashSet.Contains(100));
            Assert.IsTrue(hashSet.Contains(200));
            container.Dispose();
        }

        #endregion

        #region Clear

        [Test]
        public void Clear_ResetsCount()
        {
            var container = new SparseSetContainer<TestValue>(16);
            container.Set(1, new TestValue(1));
            container.Set(2, new TestValue(2));

            container.Clear();

            Assert.AreEqual(0, container.Count);
            Assert.IsFalse(container.Contains(1));
            Assert.IsFalse(container.Contains(2));
            container.Dispose();
        }

        #endregion

        #region Add after Remove (エントリ再利用)

        [Test]
        public void SetAfterRemove_ReusesEntries()
        {
            var container = new SparseSetContainer<TestValue>(4);
            container.Set(1, new TestValue(1));
            container.Set(2, new TestValue(2));

            container.Remove(1);
            container.Set(3, new TestValue(3));

            Assert.AreEqual(2, container.Count);
            Assert.IsFalse(container.Contains(1));
            Assert.IsTrue(container.Contains(2));
            Assert.IsTrue(container.Contains(3));
            container.Dispose();
        }

        #endregion
    }
}
