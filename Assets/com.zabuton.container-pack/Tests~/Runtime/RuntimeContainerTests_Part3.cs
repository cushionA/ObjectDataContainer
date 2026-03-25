using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Tests
{
    /// <summary>
    /// PriorityPoolContainer、GroupContainer、CooldownContainerのユニットテスト
    /// </summary>
    [TestFixture]
    public class RuntimeContainerTests_Part3
    {
        private List<GameObject> _createdObjects;

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

        // テスト用のダミーデータクラス
        private class TestData
        {
            public string Name;
            public int Value;

            public TestData(string name, int value = 0)
            {
                Name = name;
                Value = value;
            }
        }

        #region PriorityPoolContainer Tests

        [Test]
        public void PriorityPool_Add_IncreasesCount()
        {
            var pool = new PriorityPoolContainer<TestData>(10);
            var go = CreateGameObject("A");

            pool.Add(go, new TestData("a"));

            Assert.AreEqual(1, pool.Count);

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_Add_WithDefaultPriorityAndDuration()
        {
            var pool = new PriorityPoolContainer<TestData>(10);
            var go = CreateGameObject("A");

            pool.Add(go, new TestData("a"));

            Assert.AreEqual(0f, pool.GetPriority(go));
            Assert.IsTrue(pool.TryGetValue(go, out var data));
            Assert.AreEqual("a", data.Name);

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_Add_WithCustomPriority()
        {
            var pool = new PriorityPoolContainer<TestData>(10);
            var go = CreateGameObject("A");

            pool.Add(go, new TestData("a"), priority: 5f);

            Assert.AreEqual(5f, pool.GetPriority(go));

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_Remove_DecreasesCount()
        {
            var pool = new PriorityPoolContainer<TestData>(10);
            var go = CreateGameObject("A");

            pool.Add(go, new TestData("a"));
            Assert.IsTrue(pool.Remove(go));
            Assert.AreEqual(0, pool.Count);

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_Remove_NonExistent_ReturnsFalse()
        {
            var pool = new PriorityPoolContainer<TestData>(10);
            var go = CreateGameObject("A");

            Assert.IsFalse(pool.Remove(go));

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_TryGetValue_ReturnsCorrectData()
        {
            var pool = new PriorityPoolContainer<TestData>(10);
            var go = CreateGameObject("A");
            var testData = new TestData("a", 42);

            pool.Add(go, testData);

            Assert.IsTrue(pool.TryGetValue(go, out var result));
            Assert.AreEqual("a", result.Name);
            Assert.AreEqual(42, result.Value);

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_TryGetValue_NonExistent_ReturnsFalse()
        {
            var pool = new PriorityPoolContainer<TestData>(10);
            var go = CreateGameObject("A");

            Assert.IsFalse(pool.TryGetValue(go, out _));

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_IsFull_ReturnsTrueWhenAtCapacity()
        {
            var pool = new PriorityPoolContainer<TestData>(2);
            var go1 = CreateGameObject("A");
            var go2 = CreateGameObject("B");

            pool.Add(go1, new TestData("a"));
            Assert.IsFalse(pool.IsFull);
            pool.Add(go2, new TestData("b"));
            Assert.IsTrue(pool.IsFull);

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_ContainsKey_Works()
        {
            var pool = new PriorityPoolContainer<TestData>(10);
            var go = CreateGameObject("A");

            Assert.IsFalse(pool.ContainsKey(go));
            pool.Add(go, new TestData("a"));
            Assert.IsTrue(pool.ContainsKey(go));

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_GetByIndex_ReturnsCorrectData()
        {
            var pool = new PriorityPoolContainer<TestData>(10);
            var go = CreateGameObject("A");

            pool.Add(go, new TestData("a", 99));

            var result = pool.GetByIndex(0);
            Assert.AreEqual("a", result.Name);
            Assert.AreEqual(99, result.Value);

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_UpdatePriority_ChangesPriority()
        {
            var pool = new PriorityPoolContainer<TestData>(10);
            var go = CreateGameObject("A");

            pool.Add(go, new TestData("a"), priority: 1f);
            pool.UpdatePriority(go, 10f);

            Assert.AreEqual(10f, pool.GetPriority(go));

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_TryAddOrEvict_AddsWhenNotFull()
        {
            var pool = new PriorityPoolContainer<TestData>(5);
            var go = CreateGameObject("A");

            bool result = pool.TryAddOrEvict(go, new TestData("a"), out var evicted, priority: 1f);

            Assert.IsTrue(result);
            Assert.IsNull(evicted);
            Assert.AreEqual(1, pool.Count);

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_TryAddOrEvict_EvictsLowestPriority()
        {
            var pool = new PriorityPoolContainer<TestData>(2);
            var go1 = CreateGameObject("A");
            var go2 = CreateGameObject("B");
            var go3 = CreateGameObject("C");

            pool.Add(go1, new TestData("low", 1), priority: 1f);
            pool.Add(go2, new TestData("high", 2), priority: 10f);

            bool result = pool.TryAddOrEvict(go3, new TestData("medium", 3), out var evicted, priority: 5f);

            Assert.IsTrue(result);
            Assert.IsNotNull(evicted);
            Assert.AreEqual("low", evicted.Name);
            Assert.AreEqual(2, pool.Count);
            Assert.IsTrue(pool.ContainsKey(go2));
            Assert.IsTrue(pool.ContainsKey(go3));
            Assert.IsFalse(pool.ContainsKey(go1));

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_TryAddOrEvict_FIFO_WhenEqualPriority()
        {
            var pool = new PriorityPoolContainer<TestData>(2);
            var go1 = CreateGameObject("A");
            var go2 = CreateGameObject("B");
            var go3 = CreateGameObject("C");

            pool.Add(go1, new TestData("first"), priority: 0f);
            pool.Add(go2, new TestData("second"), priority: 0f);

            // Equal priority: should evict the first-inserted
            bool result = pool.TryAddOrEvict(go3, new TestData("third"), out var evicted, priority: 0f);

            Assert.IsTrue(result);
            Assert.IsNotNull(evicted);
            Assert.AreEqual("first", evicted.Name);

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_Update_ExpiresTimedElements()
        {
            var pool = new PriorityPoolContainer<TestData>(10);
            var go = CreateGameObject("A");

            pool.Add(go, new TestData("timed"), duration: 1.0f);
            Assert.AreEqual(1, pool.Count);

            int expired = pool.Update(0.5f);
            Assert.AreEqual(0, expired);
            Assert.AreEqual(1, pool.Count);

            expired = pool.Update(0.6f);
            Assert.AreEqual(1, expired);
            Assert.AreEqual(0, pool.Count);

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_Update_WithCallback_InvokesOnExpired()
        {
            var pool = new PriorityPoolContainer<TestData>(10);
            var go = CreateGameObject("A");
            var expiredData = new TestData("timed", 42);

            pool.Add(go, expiredData, duration: 0.5f);

            var callbackResults = new List<TestData>();
            int expired = pool.Update(1.0f, data => callbackResults.Add(data));

            Assert.AreEqual(1, expired);
            Assert.AreEqual(1, callbackResults.Count);
            Assert.AreEqual("timed", callbackResults[0].Name);
            Assert.AreEqual(42, callbackResults[0].Value);

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_Update_NoExpiry_NeverExpires()
        {
            var pool = new PriorityPoolContainer<TestData>(10);
            var go = CreateGameObject("A");

            pool.Add(go, new TestData("permanent")); // default duration = -1

            int expired = pool.Update(1000f);
            Assert.AreEqual(0, expired);
            Assert.AreEqual(1, pool.Count);

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_Update_WithoutCallback_StillExpires()
        {
            var pool = new PriorityPoolContainer<TestData>(10);
            var go = CreateGameObject("A");

            pool.Add(go, new TestData("timed"), duration: 0.1f);

            int expired = pool.Update(1.0f);
            Assert.AreEqual(1, expired);
            Assert.AreEqual(0, pool.Count);

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_Clear_RemovesAll()
        {
            var pool = new PriorityPoolContainer<TestData>(10);
            pool.Add(CreateGameObject("A"), new TestData("a"));
            pool.Add(CreateGameObject("B"), new TestData("b"));

            pool.Clear();

            Assert.AreEqual(0, pool.Count);
            Assert.IsFalse(pool.IsFull);

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_MaxCapacity_ReturnsCorrectValue()
        {
            var pool = new PriorityPoolContainer<TestData>(42);
            Assert.AreEqual(42, pool.MaxCapacity);
            pool.Dispose();
        }

        [Test]
        public void PriorityPool_MultipleAddRemove_MaintainsIntegrity()
        {
            var pool = new PriorityPoolContainer<TestData>(10);
            var objects = new GameObject[5];
            for (int i = 0; i < 5; i++)
            {
                objects[i] = CreateGameObject($"Obj{i}");
                pool.Add(objects[i], new TestData($"data{i}", i), priority: i);
            }

            Assert.AreEqual(5, pool.Count);

            // Remove middle element
            pool.Remove(objects[2]);
            Assert.AreEqual(4, pool.Count);
            Assert.IsFalse(pool.ContainsKey(objects[2]));

            // Remaining elements should still be accessible
            for (int i = 0; i < 5; i++)
            {
                if (i == 2) continue;
                Assert.IsTrue(pool.TryGetValue(objects[i], out var data));
                Assert.AreEqual($"data{i}", data.Name);
            }

            pool.Dispose();
        }

        [Test]
        public void PriorityPool_Update_MultipleExpiries()
        {
            var pool = new PriorityPoolContainer<TestData>(10);

            pool.Add(CreateGameObject("A"), new TestData("a"), duration: 1.0f);
            pool.Add(CreateGameObject("B"), new TestData("b"), duration: 2.0f);
            pool.Add(CreateGameObject("C"), new TestData("c")); // permanent

            int expired = pool.Update(1.5f);
            Assert.AreEqual(1, expired);
            Assert.AreEqual(2, pool.Count);

            expired = pool.Update(1.0f);
            Assert.AreEqual(1, expired);
            Assert.AreEqual(1, pool.Count);

            pool.Dispose();
        }

        #endregion

        #region GroupContainer Tests

        [Test]
        public void Group_Add_IncreasesCount()
        {
            var container = new GroupContainer<TestData>(10, new[] { "enemies", "allies" });
            var go = CreateGameObject("A");

            container.Add(go, new TestData("a"), "enemies");

            Assert.AreEqual(1, container.Count);

            container.Dispose();
        }

        [Test]
        public void Group_Add_ToCorrectGroup()
        {
            var container = new GroupContainer<TestData>(10, new[] { "enemies", "allies" });
            var go = CreateGameObject("A");

            container.Add(go, new TestData("a"), "enemies");

            Assert.AreEqual(1, container.GetGroupCount("enemies"));
            Assert.AreEqual(0, container.GetGroupCount("allies"));

            container.Dispose();
        }

        [Test]
        public void Group_Remove_DecreasesCount()
        {
            var container = new GroupContainer<TestData>(10, new[] { "enemies" });
            var go = CreateGameObject("A");

            container.Add(go, new TestData("a"), "enemies");
            Assert.IsTrue(container.Remove(go));
            Assert.AreEqual(0, container.Count);
            Assert.AreEqual(0, container.GetGroupCount("enemies"));

            container.Dispose();
        }

        [Test]
        public void Group_Remove_NonExistent_ReturnsFalse()
        {
            var container = new GroupContainer<TestData>(10, new[] { "enemies" });
            var go = CreateGameObject("A");

            Assert.IsFalse(container.Remove(go));

            container.Dispose();
        }

        [Test]
        public void Group_MoveToGroup_ChangesGroup()
        {
            var container = new GroupContainer<TestData>(10, new[] { "enemies", "allies" });
            var go = CreateGameObject("A");

            container.Add(go, new TestData("a"), "enemies");
            Assert.AreEqual(1, container.GetGroupCount("enemies"));

            Assert.IsTrue(container.MoveToGroup(go, "allies"));
            Assert.AreEqual(0, container.GetGroupCount("enemies"));
            Assert.AreEqual(1, container.GetGroupCount("allies"));

            container.Dispose();
        }

        [Test]
        public void Group_MoveToGroup_NonExistent_ReturnsFalse()
        {
            var container = new GroupContainer<TestData>(10, new[] { "enemies", "allies" });
            var go = CreateGameObject("A");

            Assert.IsFalse(container.MoveToGroup(go, "allies"));

            container.Dispose();
        }

        [Test]
        public void Group_TryGetValue_ReturnsDataAndGroupName()
        {
            var container = new GroupContainer<TestData>(10, new[] { "enemies", "allies" });
            var go = CreateGameObject("A");

            container.Add(go, new TestData("a", 42), "enemies");

            Assert.IsTrue(container.TryGetValue(go, out var data, out var groupName));
            Assert.AreEqual("a", data.Name);
            Assert.AreEqual(42, data.Value);
            Assert.AreEqual("enemies", groupName);

            container.Dispose();
        }

        [Test]
        public void Group_TryGetValue_NonExistent_ReturnsFalse()
        {
            var container = new GroupContainer<TestData>(10, new[] { "enemies" });
            var go = CreateGameObject("A");

            Assert.IsFalse(container.TryGetValue(go, out _, out _));

            container.Dispose();
        }

        [Test]
        public void Group_GetGroup_ReturnsCorrectElements()
        {
            var container = new GroupContainer<TestData>(10, new[] { "enemies", "allies" });

            container.Add(CreateGameObject("E1"), new TestData("enemy1"), "enemies");
            container.Add(CreateGameObject("A1"), new TestData("ally1"), "allies");
            container.Add(CreateGameObject("E2"), new TestData("enemy2"), "enemies");

            Span<TestData> results = new TestData[10];
            int count = container.GetGroup("enemies", results);

            Assert.AreEqual(2, count);
            // Check both names exist in results (order may vary due to BackSwap)
            var names = new HashSet<string>();
            for (int i = 0; i < count; i++)
                names.Add(results[i].Name);
            Assert.IsTrue(names.Contains("enemy1"));
            Assert.IsTrue(names.Contains("enemy2"));

            container.Dispose();
        }

        [Test]
        public void Group_GetGroupObjects_ReturnsCorrectGameObjects()
        {
            var container = new GroupContainer<TestData>(10, new[] { "enemies", "allies" });
            var e1 = CreateGameObject("E1");
            var a1 = CreateGameObject("A1");

            container.Add(e1, new TestData("enemy1"), "enemies");
            container.Add(a1, new TestData("ally1"), "allies");

            Span<GameObject> results = new GameObject[10];
            int count = container.GetGroupObjects("enemies", results);

            Assert.AreEqual(1, count);
            Assert.AreEqual(e1, results[0]);

            container.Dispose();
        }

        [Test]
        public void Group_GetGroupCount_ReturnsCorrectCount()
        {
            var container = new GroupContainer<TestData>(10, new[] { "team_a", "team_b" });

            container.Add(CreateGameObject("1"), new TestData("a"), "team_a");
            container.Add(CreateGameObject("2"), new TestData("b"), "team_a");
            container.Add(CreateGameObject("3"), new TestData("c"), "team_b");

            Assert.AreEqual(2, container.GetGroupCount("team_a"));
            Assert.AreEqual(1, container.GetGroupCount("team_b"));

            container.Dispose();
        }

        [Test]
        public void Group_GroupExists_ReturnsTrueForDefined()
        {
            var container = new GroupContainer<TestData>(10, new[] { "enemies", "allies" });

            Assert.IsTrue(container.GroupExists("enemies"));
            Assert.IsTrue(container.GroupExists("allies"));
            Assert.IsFalse(container.GroupExists("neutral"));

            container.Dispose();
        }

        [Test]
        public void Group_ForEachInGroup_IteratesCorrectElements()
        {
            var container = new GroupContainer<TestData>(10, new[] { "enemies", "allies" });

            container.Add(CreateGameObject("E1"), new TestData("enemy1"), "enemies");
            container.Add(CreateGameObject("A1"), new TestData("ally1"), "allies");
            container.Add(CreateGameObject("E2"), new TestData("enemy2"), "enemies");

            var visited = new List<string>();
            container.ForEachInGroup("enemies", (go, data) => visited.Add(data.Name));

            Assert.AreEqual(2, visited.Count);
            Assert.IsTrue(visited.Contains("enemy1"));
            Assert.IsTrue(visited.Contains("enemy2"));

            container.Dispose();
        }

        [Test]
        public void Group_ContainsKey_Works()
        {
            var container = new GroupContainer<TestData>(10, new[] { "enemies" });
            var go = CreateGameObject("A");

            Assert.IsFalse(container.ContainsKey(go));
            container.Add(go, new TestData("a"), "enemies");
            Assert.IsTrue(container.ContainsKey(go));

            container.Dispose();
        }

        [Test]
        public void Group_Clear_RemovesAll()
        {
            var container = new GroupContainer<TestData>(10, new[] { "enemies", "allies" });

            container.Add(CreateGameObject("E1"), new TestData("a"), "enemies");
            container.Add(CreateGameObject("A1"), new TestData("b"), "allies");

            container.Clear();

            Assert.AreEqual(0, container.Count);
            Assert.AreEqual(0, container.GetGroupCount("enemies"));
            Assert.AreEqual(0, container.GetGroupCount("allies"));

            container.Dispose();
        }

        [Test]
        public void Group_MultipleAddRemove_MaintainsGroupCounts()
        {
            var container = new GroupContainer<TestData>(10, new[] { "enemies", "allies" });
            var e1 = CreateGameObject("E1");
            var e2 = CreateGameObject("E2");
            var a1 = CreateGameObject("A1");

            container.Add(e1, new TestData("e1"), "enemies");
            container.Add(e2, new TestData("e2"), "enemies");
            container.Add(a1, new TestData("a1"), "allies");

            Assert.AreEqual(2, container.GetGroupCount("enemies"));
            Assert.AreEqual(1, container.GetGroupCount("allies"));

            container.Remove(e1);

            Assert.AreEqual(1, container.GetGroupCount("enemies"));
            Assert.AreEqual(1, container.GetGroupCount("allies"));
            Assert.AreEqual(2, container.Count);

            container.Dispose();
        }

        [Test]
        public void Group_MoveToGroup_UpdatesGroupCounts()
        {
            var container = new GroupContainer<TestData>(10, new[] { "enemies", "allies" });
            var go = CreateGameObject("A");

            container.Add(go, new TestData("a"), "enemies");
            container.MoveToGroup(go, "allies");

            Assert.AreEqual(0, container.GetGroupCount("enemies"));
            Assert.AreEqual(1, container.GetGroupCount("allies"));

            // Verify TryGetValue returns the new group
            container.TryGetValue(go, out _, out var groupName);
            Assert.AreEqual("allies", groupName);

            container.Dispose();
        }

        #endregion

        #region CooldownContainer Tests

        [Test]
        public void Cooldown_AddOwner_IncreasesCount()
        {
            var container = new CooldownContainer(10);
            var go = CreateGameObject("A");

            container.AddOwner(go);

            Assert.AreEqual(1, container.OwnerCount);

            container.Dispose();
        }

        [Test]
        public void Cooldown_RemoveOwner_DecreasesCount()
        {
            var container = new CooldownContainer(10);
            var go = CreateGameObject("A");

            container.AddOwner(go);
            Assert.IsTrue(container.RemoveOwner(go));
            Assert.AreEqual(0, container.OwnerCount);

            container.Dispose();
        }

        [Test]
        public void Cooldown_RemoveOwner_NonExistent_ReturnsFalse()
        {
            var container = new CooldownContainer(10);
            var go = CreateGameObject("A");

            Assert.IsFalse(container.RemoveOwner(go));

            container.Dispose();
        }

        [Test]
        public void Cooldown_ContainsOwner_Works()
        {
            var container = new CooldownContainer(10);
            var go = CreateGameObject("A");

            Assert.IsFalse(container.ContainsOwner(go));
            container.AddOwner(go);
            Assert.IsTrue(container.ContainsOwner(go));

            container.Dispose();
        }

        [Test]
        public void Cooldown_StartCooldown_MakesCooldownActive()
        {
            var container = new CooldownContainer(10);
            var go = CreateGameObject("A");

            container.AddOwner(go);
            container.StartCooldown(go, "attack", 2.0f);

            Assert.IsFalse(container.IsCooldownReady(go, "attack"));
            Assert.AreEqual(1, container.GetActiveCooldownCount(go));

            container.Dispose();
        }

        [Test]
        public void Cooldown_IsCooldownReady_TrueWhenNoCooldown()
        {
            var container = new CooldownContainer(10);
            var go = CreateGameObject("A");

            container.AddOwner(go);

            Assert.IsTrue(container.IsCooldownReady(go, "attack"));

            container.Dispose();
        }

        [Test]
        public void Cooldown_GetRemainingTime_ReturnsCorrectTime()
        {
            var container = new CooldownContainer(10);
            var go = CreateGameObject("A");

            container.AddOwner(go);
            container.StartCooldown(go, "attack", 3.0f);

            Assert.AreEqual(3.0f, container.GetRemainingTime(go, "attack"), 0.001f);

            container.Update(1.0f);
            Assert.AreEqual(2.0f, container.GetRemainingTime(go, "attack"), 0.001f);

            container.Dispose();
        }

        [Test]
        public void Cooldown_GetRemainingTime_ZeroWhenReady()
        {
            var container = new CooldownContainer(10);
            var go = CreateGameObject("A");

            container.AddOwner(go);

            Assert.AreEqual(0f, container.GetRemainingTime(go, "attack"));

            container.Dispose();
        }

        [Test]
        public void Cooldown_GetCooldownRatio_ReturnsCorrectRatio()
        {
            var container = new CooldownContainer(10);
            var go = CreateGameObject("A");

            container.AddOwner(go);
            container.StartCooldown(go, "attack", 4.0f);

            Assert.AreEqual(1.0f, container.GetCooldownRatio(go, "attack"), 0.001f);

            container.Update(2.0f);
            Assert.AreEqual(0.5f, container.GetCooldownRatio(go, "attack"), 0.001f);

            container.Dispose();
        }

        [Test]
        public void Cooldown_GetCooldownRatio_ZeroWhenReady()
        {
            var container = new CooldownContainer(10);
            var go = CreateGameObject("A");

            container.AddOwner(go);

            Assert.AreEqual(0f, container.GetCooldownRatio(go, "attack"));

            container.Dispose();
        }

        [Test]
        public void Cooldown_Update_CompletesExpiredCooldowns()
        {
            var container = new CooldownContainer(10);
            var go = CreateGameObject("A");

            container.AddOwner(go);
            container.StartCooldown(go, "attack", 1.0f);

            Assert.IsFalse(container.IsCooldownReady(go, "attack"));

            container.Update(1.5f);

            Assert.IsTrue(container.IsCooldownReady(go, "attack"));
            Assert.AreEqual(0, container.GetActiveCooldownCount(go));

            container.Dispose();
        }

        [Test]
        public void Cooldown_MultipleCooldowns_PerObject()
        {
            var container = new CooldownContainer(10);
            var go = CreateGameObject("A");

            container.AddOwner(go);
            container.StartCooldown(go, "attack", 1.0f);
            container.StartCooldown(go, "dodge", 2.0f);
            container.StartCooldown(go, "heal", 3.0f);

            Assert.AreEqual(3, container.GetActiveCooldownCount(go));

            container.Update(1.5f);

            Assert.IsTrue(container.IsCooldownReady(go, "attack"));
            Assert.IsFalse(container.IsCooldownReady(go, "dodge"));
            Assert.IsFalse(container.IsCooldownReady(go, "heal"));
            Assert.AreEqual(2, container.GetActiveCooldownCount(go));

            container.Dispose();
        }

        [Test]
        public void Cooldown_StartCooldown_ResetsExistingCooldown()
        {
            var container = new CooldownContainer(10);
            var go = CreateGameObject("A");

            container.AddOwner(go);
            container.StartCooldown(go, "attack", 2.0f);

            container.Update(1.0f);
            Assert.AreEqual(1.0f, container.GetRemainingTime(go, "attack"), 0.001f);

            // Reset the same cooldown
            container.StartCooldown(go, "attack", 5.0f);
            Assert.AreEqual(5.0f, container.GetRemainingTime(go, "attack"), 0.001f);

            // Should still only be 1 active cooldown
            Assert.AreEqual(1, container.GetActiveCooldownCount(go));

            container.Dispose();
        }

        [Test]
        public void Cooldown_ResetAllCooldowns_ClearsAllForOwner()
        {
            var container = new CooldownContainer(10);
            var go1 = CreateGameObject("A");
            var go2 = CreateGameObject("B");

            container.AddOwner(go1);
            container.AddOwner(go2);
            container.StartCooldown(go1, "attack", 2.0f);
            container.StartCooldown(go1, "dodge", 3.0f);
            container.StartCooldown(go2, "attack", 1.0f);

            container.ResetAllCooldowns(go1);

            Assert.AreEqual(0, container.GetActiveCooldownCount(go1));
            Assert.IsTrue(container.IsCooldownReady(go1, "attack"));
            Assert.IsTrue(container.IsCooldownReady(go1, "dodge"));
            // go2 should be unaffected
            Assert.AreEqual(1, container.GetActiveCooldownCount(go2));

            container.Dispose();
        }

        [Test]
        public void Cooldown_ResetCooldown_ClearsSpecificCooldown()
        {
            var container = new CooldownContainer(10);
            var go = CreateGameObject("A");

            container.AddOwner(go);
            container.StartCooldown(go, "attack", 2.0f);
            container.StartCooldown(go, "dodge", 3.0f);

            container.ResetCooldown(go, "attack");

            Assert.IsTrue(container.IsCooldownReady(go, "attack"));
            Assert.IsFalse(container.IsCooldownReady(go, "dodge"));
            Assert.AreEqual(1, container.GetActiveCooldownCount(go));

            container.Dispose();
        }

        [Test]
        public void Cooldown_RemoveOwner_RemovesAllCooldowns()
        {
            var container = new CooldownContainer(10);
            var go = CreateGameObject("A");

            container.AddOwner(go);
            container.StartCooldown(go, "attack", 2.0f);
            container.StartCooldown(go, "dodge", 3.0f);

            container.RemoveOwner(go);

            Assert.AreEqual(0, container.OwnerCount);

            container.Dispose();
        }

        [Test]
        public void Cooldown_Clear_RemovesEverything()
        {
            var container = new CooldownContainer(10);
            var go1 = CreateGameObject("A");
            var go2 = CreateGameObject("B");

            container.AddOwner(go1);
            container.AddOwner(go2);
            container.StartCooldown(go1, "attack", 2.0f);
            container.StartCooldown(go2, "dodge", 3.0f);

            container.Clear();

            Assert.AreEqual(0, container.OwnerCount);

            container.Dispose();
        }

        [Test]
        public void Cooldown_MultipleOwners_IndependentCooldowns()
        {
            var container = new CooldownContainer(10);
            var go1 = CreateGameObject("A");
            var go2 = CreateGameObject("B");

            container.AddOwner(go1);
            container.AddOwner(go2);

            container.StartCooldown(go1, "attack", 1.0f);
            container.StartCooldown(go2, "attack", 3.0f);

            container.Update(1.5f);

            Assert.IsTrue(container.IsCooldownReady(go1, "attack"));
            Assert.IsFalse(container.IsCooldownReady(go2, "attack"));

            container.Dispose();
        }

        [Test]
        public void Cooldown_DefaultMaxCooldowns_IsOwnerTimesFour()
        {
            // This tests the constructor default: maxCooldownsTotal = maxOwners * 4
            var container = new CooldownContainer(5);
            var go = CreateGameObject("A");

            container.AddOwner(go);

            // Should be able to add at least 4 cooldowns per owner slot
            container.StartCooldown(go, "a", 1f);
            container.StartCooldown(go, "b", 1f);
            container.StartCooldown(go, "c", 1f);
            container.StartCooldown(go, "d", 1f);

            Assert.AreEqual(4, container.GetActiveCooldownCount(go));

            container.Dispose();
        }

        #endregion
    }
}
