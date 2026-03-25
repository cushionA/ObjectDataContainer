using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Tests
{
    /// <summary>
    /// FixedSlotContainer のユニットテスト
    /// </summary>
    [TestFixture]
    public class FixedSlotContainerTests
    {
        private List<GameObject> _createdObjects;

        private struct SlotData
        {
            public int Id;
            public float Value;
            public SlotData(int id, float value = 0f) { Id = id; Value = value; }
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

        #region Add / Remove

        [Test]
        public void Add_IncreasesCount()
        {
            var container = new FixedSlotContainer<SlotData>(16, 4);
            container.Add(100);

            Assert.AreEqual(1, container.Count);
            Assert.IsTrue(container.ContainsKey(100));
            container.Dispose();
        }

        [Test]
        public void Add_WithGameObject_Works()
        {
            var container = new FixedSlotContainer<SlotData>(16, 4);
            var go = CreateGameObject("A");
            container.Add(go);

            Assert.AreEqual(1, container.Count);
            container.Dispose();
        }

        [Test]
        public void Remove_Existing_ReturnsTrue()
        {
            var container = new FixedSlotContainer<SlotData>(16, 4);
            container.Add(100);

            Assert.IsTrue(container.Remove(100));
            Assert.AreEqual(0, container.Count);
            container.Dispose();
        }

        [Test]
        public void Remove_BackSwap_PreservesOtherEntities()
        {
            var container = new FixedSlotContainer<SlotData>(16, 2);
            container.Add(1);
            container.Add(2);
            container.Add(3);

            container.SetSlot(1, 0, new SlotData(10));
            container.SetSlot(2, 0, new SlotData(20));
            container.SetSlot(3, 0, new SlotData(30));

            // 先頭を削除 → 末尾がブロックコピーで移動
            container.Remove(1);

            Assert.AreEqual(2, container.Count);
            Assert.AreEqual(20, container.GetSlot(2, 0).Id);
            Assert.AreEqual(30, container.GetSlot(3, 0).Id);
            container.Dispose();
        }

        #endregion

        #region SetSlot / GetSlot

        [Test]
        public void SetSlot_GetSlot_RoundTrip()
        {
            var container = new FixedSlotContainer<SlotData>(16, 4);
            container.Add(100);

            container.SetSlot(100, 0, new SlotData(1, 10f));
            container.SetSlot(100, 3, new SlotData(4, 40f));

            Assert.AreEqual(1, container.GetSlot(100, 0).Id);
            Assert.AreEqual(40f, container.GetSlot(100, 3).Value);
            container.Dispose();
        }

        [Test]
        public void SetSlot_InvalidSlotIndex_ThrowsException()
        {
            var container = new FixedSlotContainer<SlotData>(16, 4);
            container.Add(100);

            Assert.Throws<ArgumentOutOfRangeException>(() => container.SetSlot(100, 4, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => container.SetSlot(100, -1, default));
            container.Dispose();
        }

        [Test]
        public void GetSlotRef_MutatesInPlace()
        {
            var container = new FixedSlotContainer<SlotData>(16, 4);
            container.Add(100);
            container.SetSlot(100, 0, new SlotData(1, 0f));

            ref SlotData slot = ref container.GetSlotRef(100, 0);
            slot.Value = 999f;

            Assert.AreEqual(999f, container.GetSlot(100, 0).Value);
            container.Dispose();
        }

        #endregion

        #region Cursor

        [Test]
        public void RotateCursor_Wraps()
        {
            var container = new FixedSlotContainer<SlotData>(16, 3);
            container.Add(100);

            Assert.AreEqual(0, container.GetCursorIndex(100));
            container.RotateCursor(100);
            Assert.AreEqual(1, container.GetCursorIndex(100));
            container.RotateCursor(100);
            Assert.AreEqual(2, container.GetCursorIndex(100));
            container.RotateCursor(100);
            Assert.AreEqual(0, container.GetCursorIndex(100)); // ラップ
            container.Dispose();
        }

        [Test]
        public void GetAtCursor_ReturnsCorrectSlot()
        {
            var container = new FixedSlotContainer<SlotData>(16, 3);
            container.Add(100);
            container.SetSlot(100, 0, new SlotData(10));
            container.SetSlot(100, 1, new SlotData(20));
            container.SetSlot(100, 2, new SlotData(30));

            Assert.AreEqual(10, container.GetAtCursor(100).Id);
            container.RotateCursor(100);
            Assert.AreEqual(20, container.GetAtCursor(100).Id);
            container.RotateCursor(100);
            Assert.AreEqual(30, container.GetAtCursor(100).Id);
            container.Dispose();
        }

        [Test]
        public void GetAtCursorRef_MutatesInPlace()
        {
            var container = new FixedSlotContainer<SlotData>(16, 3);
            container.Add(100);
            container.SetSlot(100, 0, new SlotData(10, 0f));
            container.SetSlot(100, 1, new SlotData(20, 0f));

            // カーソル位置(0)のスロットをref経由で変更
            ref SlotData cursorSlot = ref container.GetAtCursorRef(100);
            cursorSlot.Value = 777f;

            Assert.AreEqual(777f, container.GetAtCursor(100).Value);

            // カーソルを進めて別スロットもref変更
            container.RotateCursor(100);
            ref SlotData nextSlot = ref container.GetAtCursorRef(100);
            nextSlot.Value = 888f;

            Assert.AreEqual(888f, container.GetAtCursor(100).Value);
            container.Dispose();
        }

        #endregion

        #region SwapSlots

        [Test]
        public void SwapSlots_ExchangesValues()
        {
            var container = new FixedSlotContainer<SlotData>(16, 4);
            container.Add(100);
            container.SetSlot(100, 0, new SlotData(10));
            container.SetSlot(100, 1, new SlotData(20));

            container.SwapSlots(100, 0, 1);

            Assert.AreEqual(20, container.GetSlot(100, 0).Id);
            Assert.AreEqual(10, container.GetSlot(100, 1).Id);
            container.Dispose();
        }

        #endregion

        #region GetSlots (Span)

        [Test]
        public void GetSlots_ReturnsAllSlots()
        {
            var container = new FixedSlotContainer<SlotData>(16, 3);
            container.Add(100);
            container.SetSlot(100, 0, new SlotData(1));
            container.SetSlot(100, 1, new SlotData(2));
            container.SetSlot(100, 2, new SlotData(3));

            var slots = container.GetSlots(100);
            Assert.AreEqual(3, slots.Length);
            Assert.AreEqual(1, slots[0].Id);
            Assert.AreEqual(2, slots[1].Id);
            Assert.AreEqual(3, slots[2].Id);
            container.Dispose();
        }

        #endregion

        #region Clear

        [Test]
        public void Clear_ResetsAll()
        {
            var container = new FixedSlotContainer<SlotData>(16, 4);
            container.Add(100);
            container.SetSlot(100, 0, new SlotData(1));

            container.Clear();

            Assert.AreEqual(0, container.Count);
            Assert.IsFalse(container.ContainsKey(100));
            container.Dispose();
        }

        #endregion
    }
}
