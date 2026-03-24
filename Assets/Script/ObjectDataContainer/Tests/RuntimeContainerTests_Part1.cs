using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Tests
{
    /// <summary>
    /// TimedDataContainer, NotifyTimedDataContainer, RingBufferContainer の単体テスト。
    /// TDD方式: 実装より先にテストを記述。
    /// </summary>
    [TestFixture]
    public class RuntimeContainerTests_Part1
    {
        // テスト用のクラスデータ
        private class TestData
        {
            public string Name;
            public int Value;

            public TestData(string name, int value)
            {
                Name = name;
                Value = value;
            }
        }

        // テスト用のGameObject群
        private GameObject[] _gameObjects;

        [SetUp]
        public void SetUp()
        {
            _gameObjects = new GameObject[20];
            for (int i = 0; i < _gameObjects.Length; i++)
            {
                _gameObjects[i] = new GameObject($"TestObj_{i}");
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _gameObjects)
            {
                if (go != null)
                    GameObject.DestroyImmediate(go);
            }
            _gameObjects = null;
        }

        // =============================================
        // TimedDataContainer テスト
        // =============================================

        #region TimedDataContainer - 基本操作

        [Test]
        public void TimedDataContainer_Add_CountIncreases()
        {
            var container = new TimedDataContainer<TestData>(10);
            Assert.AreEqual(0, container.Count);

            container.Add(_gameObjects[0], new TestData("A", 1), 5f);
            Assert.AreEqual(1, container.Count);

            container.Add(_gameObjects[1], new TestData("B", 2), 3f);
            Assert.AreEqual(2, container.Count);

            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_Add_ReturnsCorrectIndex()
        {
            var container = new TimedDataContainer<TestData>(10);

            int idx0 = container.Add(_gameObjects[0], new TestData("A", 1), 5f);
            int idx1 = container.Add(_gameObjects[1], new TestData("B", 2), 3f);

            Assert.AreEqual(0, idx0);
            Assert.AreEqual(1, idx1);

            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_ContainsKey_ReturnsTrueForExisting()
        {
            var container = new TimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("A", 1), 5f);

            Assert.IsTrue(container.ContainsKey(_gameObjects[0]));
            Assert.IsFalse(container.ContainsKey(_gameObjects[1]));

            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_Remove_DecreasesCount()
        {
            var container = new TimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("A", 1), 5f);
            container.Add(_gameObjects[1], new TestData("B", 2), 3f);

            bool removed = container.Remove(_gameObjects[0]);
            Assert.IsTrue(removed);
            Assert.AreEqual(1, container.Count);
            Assert.IsFalse(container.ContainsKey(_gameObjects[0]));
            Assert.IsTrue(container.ContainsKey(_gameObjects[1]));

            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_Remove_NonExistentReturnsFalse()
        {
            var container = new TimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("A", 1), 5f);

            bool removed = container.Remove(_gameObjects[1]);
            Assert.IsFalse(removed);
            Assert.AreEqual(1, container.Count);

            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_TryGetValue_ReturnsDataAndTime()
        {
            var container = new TimedDataContainer<TestData>(10);
            var data = new TestData("A", 42);
            container.Add(_gameObjects[0], data, 5f);

            bool found = container.TryGetValue(_gameObjects[0], out var result, out float remaining);
            Assert.IsTrue(found);
            Assert.AreEqual("A", result.Name);
            Assert.AreEqual(42, result.Value);
            Assert.AreEqual(5f, remaining, 0.001f);

            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_TryGetValue_ReturnsFalseForMissing()
        {
            var container = new TimedDataContainer<TestData>(10);

            bool found = container.TryGetValue(_gameObjects[0], out var result, out float remaining);
            Assert.IsFalse(found);
            Assert.IsNull(result);

            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_GetByIndex_ReturnsCorrectData()
        {
            var container = new TimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("A", 1), 5f);
            container.Add(_gameObjects[1], new TestData("B", 2), 3f);

            Assert.AreEqual("A", container.GetByIndex(0).Name);
            Assert.AreEqual("B", container.GetByIndex(1).Name);

            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_MaxCapacity_ReturnsConfiguredValue()
        {
            var container = new TimedDataContainer<TestData>(42);
            Assert.AreEqual(42, container.MaxCapacity);
            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_Clear_ResetsAll()
        {
            var container = new TimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("A", 1), 5f);
            container.Add(_gameObjects[1], new TestData("B", 2), 3f);

            container.Clear();
            Assert.AreEqual(0, container.Count);
            Assert.IsFalse(container.ContainsKey(_gameObjects[0]));
            Assert.IsFalse(container.ContainsKey(_gameObjects[1]));

            container.Dispose();
        }

        #endregion

        #region TimedDataContainer - Update/タイマー

        [Test]
        public void TimedDataContainer_Update_DecrementsTimers()
        {
            var container = new TimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("A", 1), 5f);

            container.Update(2f);

            container.TryGetValue(_gameObjects[0], out _, out float remaining);
            Assert.AreEqual(3f, remaining, 0.001f);

            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_Update_RemovesExpired()
        {
            var container = new TimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("A", 1), 2f);
            container.Add(_gameObjects[1], new TestData("B", 2), 5f);

            int removed = container.Update(3f);
            Assert.AreEqual(1, removed);
            Assert.AreEqual(1, container.Count);
            Assert.IsFalse(container.ContainsKey(_gameObjects[0]));
            Assert.IsTrue(container.ContainsKey(_gameObjects[1]));

            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_Update_RemovesMultipleExpired()
        {
            var container = new TimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("A", 1), 1f);
            container.Add(_gameObjects[1], new TestData("B", 2), 2f);
            container.Add(_gameObjects[2], new TestData("C", 3), 10f);

            int removed = container.Update(3f);
            Assert.AreEqual(2, removed);
            Assert.AreEqual(1, container.Count);
            Assert.IsTrue(container.ContainsKey(_gameObjects[2]));

            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_Update_ZeroRemoved_WhenNoExpired()
        {
            var container = new TimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("A", 1), 10f);

            int removed = container.Update(1f);
            Assert.AreEqual(0, removed);
            Assert.AreEqual(1, container.Count);

            container.Dispose();
        }

        #endregion

        #region TimedDataContainer - BackSwap検証

        [Test]
        public void TimedDataContainer_BackSwap_MaintainsDataIntegrity()
        {
            // 3要素追加して先頭を削除 → BackSwapで最後尾が先頭に移動
            var container = new TimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("First", 1), 5f);
            container.Add(_gameObjects[1], new TestData("Second", 2), 5f);
            container.Add(_gameObjects[2], new TestData("Third", 3), 5f);

            container.Remove(_gameObjects[0]);

            // BackSwap後: Third が index 0 に移動、Second は index 1 のまま
            Assert.AreEqual(2, container.Count);
            Assert.IsTrue(container.ContainsKey(_gameObjects[1]));
            Assert.IsTrue(container.ContainsKey(_gameObjects[2]));

            // TryGetValueで両方のデータが正しく取得できることを確認
            container.TryGetValue(_gameObjects[1], out var data1, out _);
            container.TryGetValue(_gameObjects[2], out var data2, out _);
            Assert.AreEqual("Second", data1.Name);
            Assert.AreEqual("Third", data2.Name);

            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_BackSwap_AfterTimerExpiry_CorrectLookup()
        {
            var container = new TimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("Short", 1), 1f);
            container.Add(_gameObjects[1], new TestData("Long", 2), 100f);
            container.Add(_gameObjects[2], new TestData("Medium", 3), 50f);

            // Short期限切れ → BackSwapでMediumがindex 0に移動
            container.Update(2f);

            Assert.AreEqual(2, container.Count);
            container.TryGetValue(_gameObjects[1], out var longData, out float longTime);
            container.TryGetValue(_gameObjects[2], out var medData, out float medTime);

            Assert.AreEqual("Long", longData.Name);
            Assert.AreEqual("Medium", medData.Name);
            Assert.AreEqual(98f, longTime, 0.001f);
            Assert.AreEqual(48f, medTime, 0.001f);

            container.Dispose();
        }

        #endregion

        #region TimedDataContainer - Span

        [Test]
        public void TimedDataContainer_ActiveDataSpan_ReflectsActiveElements()
        {
            var container = new TimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("A", 1), 5f);
            container.Add(_gameObjects[1], new TestData("B", 2), 3f);

            var span = container.ActiveDataSpan;
            Assert.AreEqual(2, span.Length);

            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_ActiveTimerSpan_ReflectsTimers()
        {
            var container = new TimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("A", 1), 5f);
            container.Add(_gameObjects[1], new TestData("B", 2), 3f);

            var span = container.ActiveTimerSpan;
            Assert.AreEqual(2, span.Length);
            Assert.AreEqual(5f, span[0], 0.001f);
            Assert.AreEqual(3f, span[1], 0.001f);

            container.Dispose();
        }

        #endregion

        #region TimedDataContainer - エッジケース

        [Test]
        public void TimedDataContainer_Add_NullGameObject_Throws()
        {
            var container = new TimedDataContainer<TestData>(10);
            Assert.Throws<ArgumentNullException>(() =>
                container.Add(null, new TestData("A", 1), 5f));
            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_Add_WhenFull_Throws()
        {
            var container = new TimedDataContainer<TestData>(2);
            container.Add(_gameObjects[0], new TestData("A", 1), 5f);
            container.Add(_gameObjects[1], new TestData("B", 2), 5f);

            Assert.Throws<InvalidOperationException>(() =>
                container.Add(_gameObjects[2], new TestData("C", 3), 5f));

            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_Update_EmptyContainer_ReturnsZero()
        {
            var container = new TimedDataContainer<TestData>(10);
            int removed = container.Update(1f);
            Assert.AreEqual(0, removed);
            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_Remove_LastElement_WorksCorrectly()
        {
            var container = new TimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("Only", 1), 5f);

            bool removed = container.Remove(_gameObjects[0]);
            Assert.IsTrue(removed);
            Assert.AreEqual(0, container.Count);

            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_AddAfterRemove_ReusesEntries()
        {
            var container = new TimedDataContainer<TestData>(5);

            // 追加して削除を繰り返し、エントリ再利用を確認
            container.Add(_gameObjects[0], new TestData("A", 1), 5f);
            container.Add(_gameObjects[1], new TestData("B", 2), 5f);
            container.Remove(_gameObjects[0]);
            container.Remove(_gameObjects[1]);

            // 再追加が正常に動作
            int idx = container.Add(_gameObjects[2], new TestData("C", 3), 5f);
            Assert.AreEqual(0, idx);
            Assert.AreEqual(1, container.Count);
            Assert.IsTrue(container.ContainsKey(_gameObjects[2]));

            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_Dispose_ClearsAll()
        {
            var container = new TimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("A", 1), 5f);
            container.Dispose();

            Assert.AreEqual(0, container.Count);
        }

        #endregion

        // =============================================
        // NotifyTimedDataContainer テスト
        // =============================================

        #region NotifyTimedDataContainer

        [Test]
        public void NotifyTimedDataContainer_InheritsBaseFunctionality()
        {
            var container = new NotifyTimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("A", 1), 5f);

            Assert.AreEqual(1, container.Count);
            Assert.IsTrue(container.ContainsKey(_gameObjects[0]));

            container.Dispose();
        }

        [Test]
        public void NotifyTimedDataContainer_Update_CallsOnExpired()
        {
            var container = new NotifyTimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("Expiring", 1), 1f);
            container.Add(_gameObjects[1], new TestData("Staying", 2), 10f);

            var expiredItems = new List<TestData>();
            int removed = container.Update(2f, item => expiredItems.Add(item));

            Assert.AreEqual(1, removed);
            Assert.AreEqual(1, expiredItems.Count);
            Assert.AreEqual("Expiring", expiredItems[0].Name);

            container.Dispose();
        }

        [Test]
        public void NotifyTimedDataContainer_Update_MultipleExpiredCallbacks()
        {
            var container = new NotifyTimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("A", 1), 1f);
            container.Add(_gameObjects[1], new TestData("B", 2), 2f);
            container.Add(_gameObjects[2], new TestData("C", 3), 100f);

            var expiredItems = new List<TestData>();
            int removed = container.Update(5f, item => expiredItems.Add(item));

            Assert.AreEqual(2, removed);
            Assert.AreEqual(2, expiredItems.Count);

            // コールバックが呼ばれたデータの名前をチェック（順序は逆順の可能性あり）
            var names = new HashSet<string>();
            foreach (var item in expiredItems)
                names.Add(item.Name);

            Assert.IsTrue(names.Contains("A"));
            Assert.IsTrue(names.Contains("B"));

            container.Dispose();
        }

        [Test]
        public void NotifyTimedDataContainer_Update_NullCallback_NoThrow()
        {
            var container = new NotifyTimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("A", 1), 1f);

            Assert.DoesNotThrow(() => container.Update(2f, null));
            Assert.AreEqual(0, container.Count);

            container.Dispose();
        }

        [Test]
        public void NotifyTimedDataContainer_Update_NoExpired_NoCallback()
        {
            var container = new NotifyTimedDataContainer<TestData>(10);
            container.Add(_gameObjects[0], new TestData("A", 1), 100f);

            bool callbackCalled = false;
            int removed = container.Update(1f, _ => callbackCalled = true);

            Assert.AreEqual(0, removed);
            Assert.IsFalse(callbackCalled);

            container.Dispose();
        }

        #endregion

        // =============================================
        // RingBufferContainer テスト
        // =============================================

        #region RingBufferContainer - 基本操作

        [Test]
        public void RingBuffer_Push_IncreasesCount()
        {
            var buffer = new RingBufferContainer<int>(5);
            Assert.AreEqual(0, buffer.Count);
            Assert.IsTrue(buffer.IsEmpty);

            buffer.Push(10);
            Assert.AreEqual(1, buffer.Count);
            Assert.IsFalse(buffer.IsEmpty);

            buffer.Push(20);
            Assert.AreEqual(2, buffer.Count);
        }

        [Test]
        public void RingBuffer_Capacity_ReturnsConfiguredValue()
        {
            var buffer = new RingBufferContainer<int>(8);
            Assert.AreEqual(8, buffer.Capacity);
        }

        [Test]
        public void RingBuffer_PeekLatest_ReturnsMostRecent()
        {
            var buffer = new RingBufferContainer<int>(5);
            buffer.Push(10);
            buffer.Push(20);
            buffer.Push(30);

            Assert.AreEqual(30, buffer.PeekLatest());
        }

        [Test]
        public void RingBuffer_PeekOldest_ReturnsFirst()
        {
            var buffer = new RingBufferContainer<int>(5);
            buffer.Push(10);
            buffer.Push(20);
            buffer.Push(30);

            Assert.AreEqual(10, buffer.PeekOldest());
        }

        [Test]
        public void RingBuffer_Indexer_ZeroIsOldest()
        {
            var buffer = new RingBufferContainer<int>(5);
            buffer.Push(100);
            buffer.Push(200);
            buffer.Push(300);

            Assert.AreEqual(100, buffer[0]);
            Assert.AreEqual(200, buffer[1]);
            Assert.AreEqual(300, buffer[2]);
        }

        [Test]
        public void RingBuffer_IsFull_WhenAtCapacity()
        {
            var buffer = new RingBufferContainer<int>(3);
            Assert.IsFalse(buffer.IsFull);

            buffer.Push(1);
            buffer.Push(2);
            buffer.Push(3);

            Assert.IsTrue(buffer.IsFull);
        }

        [Test]
        public void RingBuffer_Clear_ResetsState()
        {
            var buffer = new RingBufferContainer<int>(5);
            buffer.Push(1);
            buffer.Push(2);
            buffer.Push(3);

            buffer.Clear();
            Assert.AreEqual(0, buffer.Count);
            Assert.IsTrue(buffer.IsEmpty);
            Assert.IsFalse(buffer.IsFull);
        }

        #endregion

        #region RingBufferContainer - 上書き動作

        [Test]
        public void RingBuffer_Push_OverwritesOldest_WhenFull()
        {
            var buffer = new RingBufferContainer<int>(3);
            buffer.Push(1);
            buffer.Push(2);
            buffer.Push(3);
            // バッファ満杯: [1, 2, 3]

            buffer.Push(4);
            // 1が上書きされる: [4, 2, 3] → oldest=2

            Assert.AreEqual(3, buffer.Count); // カウントは増えない
            Assert.AreEqual(2, buffer.PeekOldest());
            Assert.AreEqual(4, buffer.PeekLatest());
            Assert.AreEqual(2, buffer[0]);
            Assert.AreEqual(3, buffer[1]);
            Assert.AreEqual(4, buffer[2]);
        }

        [Test]
        public void RingBuffer_Push_MultipleOverwrites()
        {
            var buffer = new RingBufferContainer<int>(3);
            buffer.Push(1);
            buffer.Push(2);
            buffer.Push(3);
            buffer.Push(4);
            buffer.Push(5);
            // 1,2が上書き: oldest=3, latest=5

            Assert.AreEqual(3, buffer[0]); // oldest
            Assert.AreEqual(4, buffer[1]);
            Assert.AreEqual(5, buffer[2]); // latest
        }

        [Test]
        public void RingBuffer_Push_FullWrapAround()
        {
            var buffer = new RingBufferContainer<int>(3);
            // 2周以上の書き込み
            for (int i = 1; i <= 9; i++)
                buffer.Push(i);

            // 最後の3つが残る: 7, 8, 9
            Assert.AreEqual(7, buffer[0]);
            Assert.AreEqual(8, buffer[1]);
            Assert.AreEqual(9, buffer[2]);
        }

        #endregion

        #region RingBufferContainer - structデータ

        private struct DamageRecord
        {
            public float Damage;
            public float Timestamp;
        }

        [Test]
        public void RingBuffer_WorksWithStructs()
        {
            var buffer = new RingBufferContainer<DamageRecord>(4);
            buffer.Push(new DamageRecord { Damage = 10f, Timestamp = 1.0f });
            buffer.Push(new DamageRecord { Damage = 25f, Timestamp = 2.5f });

            Assert.AreEqual(10f, buffer.PeekOldest().Damage, 0.001f);
            Assert.AreEqual(25f, buffer.PeekLatest().Damage, 0.001f);
        }

        [Test]
        public void RingBuffer_Indexer_ReturnsRef()
        {
            var buffer = new RingBufferContainer<DamageRecord>(4);
            buffer.Push(new DamageRecord { Damage = 10f, Timestamp = 1.0f });

            // ref戻り値でデータを変更可能であることを確認
            ref var record = ref buffer[0];
            record.Damage = 99f;

            Assert.AreEqual(99f, buffer[0].Damage, 0.001f);
        }

        #endregion

        #region RingBufferContainer - エッジケース

        [Test]
        public void RingBuffer_PeekLatest_ThrowsWhenEmpty()
        {
            var buffer = new RingBufferContainer<int>(5);
            Assert.Throws<InvalidOperationException>(() => { var _ = buffer.PeekLatest(); });
        }

        [Test]
        public void RingBuffer_PeekOldest_ThrowsWhenEmpty()
        {
            var buffer = new RingBufferContainer<int>(5);
            Assert.Throws<InvalidOperationException>(() => { var _ = buffer.PeekOldest(); });
        }

        [Test]
        public void RingBuffer_Indexer_ThrowsOutOfRange()
        {
            var buffer = new RingBufferContainer<int>(5);
            buffer.Push(1);
            buffer.Push(2);

            Assert.Throws<IndexOutOfRangeException>(() => { var _ = buffer[2]; });
            Assert.Throws<IndexOutOfRangeException>(() => { var _ = buffer[-1]; });
        }

        [Test]
        public void RingBuffer_SingleCapacity()
        {
            var buffer = new RingBufferContainer<int>(1);
            buffer.Push(10);
            Assert.AreEqual(10, buffer.PeekLatest());
            Assert.AreEqual(10, buffer.PeekOldest());

            buffer.Push(20);
            Assert.AreEqual(20, buffer.PeekLatest());
            Assert.AreEqual(20, buffer.PeekOldest());
            Assert.AreEqual(1, buffer.Count);
        }

        [Test]
        public void RingBuffer_ClearThenReuse()
        {
            var buffer = new RingBufferContainer<int>(3);
            buffer.Push(1);
            buffer.Push(2);
            buffer.Push(3);
            buffer.Clear();

            buffer.Push(10);
            Assert.AreEqual(1, buffer.Count);
            Assert.AreEqual(10, buffer[0]);
            Assert.AreEqual(10, buffer.PeekLatest());
            Assert.AreEqual(10, buffer.PeekOldest());
        }

        #endregion

        // =============================================
        // 統合テスト: 複数操作の組み合わせ
        // =============================================

        #region 統合テスト

        [Test]
        public void TimedDataContainer_AddRemoveAdd_MaintainsIntegrity()
        {
            var container = new TimedDataContainer<TestData>(5);

            container.Add(_gameObjects[0], new TestData("A", 1), 5f);
            container.Add(_gameObjects[1], new TestData("B", 2), 5f);
            container.Add(_gameObjects[2], new TestData("C", 3), 5f);

            container.Remove(_gameObjects[1]);
            Assert.AreEqual(2, container.Count);

            // 新規追加
            container.Add(_gameObjects[3], new TestData("D", 4), 5f);
            Assert.AreEqual(3, container.Count);

            // 全てのキーが正しく参照可能
            Assert.IsTrue(container.ContainsKey(_gameObjects[0]));
            Assert.IsFalse(container.ContainsKey(_gameObjects[1]));
            Assert.IsTrue(container.ContainsKey(_gameObjects[2]));
            Assert.IsTrue(container.ContainsKey(_gameObjects[3]));

            // 全てのデータが正しく取得可能
            container.TryGetValue(_gameObjects[0], out var a, out _);
            container.TryGetValue(_gameObjects[2], out var c, out _);
            container.TryGetValue(_gameObjects[3], out var d, out _);
            Assert.AreEqual("A", a.Name);
            Assert.AreEqual("C", c.Name);
            Assert.AreEqual("D", d.Name);

            container.Dispose();
        }

        [Test]
        public void TimedDataContainer_FillAndExpireAll()
        {
            var container = new TimedDataContainer<TestData>(3);
            container.Add(_gameObjects[0], new TestData("A", 1), 1f);
            container.Add(_gameObjects[1], new TestData("B", 2), 2f);
            container.Add(_gameObjects[2], new TestData("C", 3), 3f);

            Assert.AreEqual(3, container.Count);

            int removed = container.Update(4f);
            Assert.AreEqual(3, removed);
            Assert.AreEqual(0, container.Count);

            // 再追加可能
            container.Add(_gameObjects[3], new TestData("D", 4), 5f);
            Assert.AreEqual(1, container.Count);

            container.Dispose();
        }

        [Test]
        public void RingBuffer_Vector3_GameDataUseCase()
        {
            // ゲームで位置履歴を保存する典型的なケース
            var positionHistory = new RingBufferContainer<Vector3>(5);

            positionHistory.Push(new Vector3(0, 0, 0));
            positionHistory.Push(new Vector3(1, 0, 0));
            positionHistory.Push(new Vector3(2, 0, 0));
            positionHistory.Push(new Vector3(3, 0, 0));
            positionHistory.Push(new Vector3(4, 0, 0));
            positionHistory.Push(new Vector3(5, 0, 0)); // 上書き

            Assert.AreEqual(5, positionHistory.Count);
            Assert.AreEqual(new Vector3(1, 0, 0), positionHistory.PeekOldest());
            Assert.AreEqual(new Vector3(5, 0, 0), positionHistory.PeekLatest());
        }

        #endregion
    }
}
