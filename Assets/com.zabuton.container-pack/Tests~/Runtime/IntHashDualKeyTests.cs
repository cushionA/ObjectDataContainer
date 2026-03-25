using System;
using NUnit.Framework;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Tests
{
    /// <summary>
    /// 全コンテナの int hash デュアルキーAPIテスト
    /// </summary>
    [TestFixture]
    public class IntHashDualKeyTests
    {
        // =============================================
        // CooldownContainer
        // =============================================

        [Test]
        public void Cooldown_IntHash_AddOwner_And_StartCooldown()
        {
            var c = new CooldownContainer(8);
            c.AddOwner(100);
            Assert.AreEqual(1, c.OwnerCount);
            Assert.IsTrue(c.ContainsOwner(100));

            c.StartCooldown(100, "attack", 2f);
            Assert.IsFalse(c.IsCooldownReady(100, "attack"));
            Assert.AreEqual(2f, c.GetRemainingTime(100, "attack"), 0.001f);
            Assert.AreEqual(1f, c.GetCooldownRatio(100, "attack"), 0.001f);
            Assert.AreEqual(1, c.GetActiveCooldownCount(100));

            c.Update(1f);
            Assert.AreEqual(1f, c.GetRemainingTime(100, "attack"), 0.001f);
            Assert.AreEqual(0.5f, c.GetCooldownRatio(100, "attack"), 0.001f);

            c.Update(1.5f);
            Assert.IsTrue(c.IsCooldownReady(100, "attack"));
            c.Dispose();
        }

        [Test]
        public void Cooldown_IntHash_RemoveOwner()
        {
            var c = new CooldownContainer(8);
            c.AddOwner(200);
            c.StartCooldown(200, "skill", 5f);

            Assert.IsTrue(c.RemoveOwner(200));
            Assert.IsFalse(c.ContainsOwner(200));
            Assert.AreEqual(0, c.OwnerCount);
            c.Dispose();
        }

        [Test]
        public void Cooldown_IntHash_ResetCooldown()
        {
            var c = new CooldownContainer(8);
            c.AddOwner(300);
            c.StartCooldown(300, "dash", 3f);

            c.ResetCooldown(300, "dash");
            Assert.IsTrue(c.IsCooldownReady(300, "dash"));

            c.StartCooldown(300, "dash", 3f);
            c.StartCooldown(300, "jump", 1f);
            c.ResetAllCooldowns(300);
            Assert.IsTrue(c.IsCooldownReady(300, "dash"));
            Assert.IsTrue(c.IsCooldownReady(300, "jump"));
            c.Dispose();
        }

        [Test]
        public void Cooldown_IntHash_DuplicateThrows()
        {
            var c = new CooldownContainer(8);
            c.AddOwner(400);
            Assert.Throws<InvalidOperationException>(() => c.AddOwner(400));
            c.Dispose();
        }

        // =============================================
        // TimedDataContainer
        // =============================================

        [Test]
        public void TimedData_IntHash_AddAndGet()
        {
            var c = new TimedDataContainer<string>(8);
            c.Add(100, "hello", 5f);

            Assert.AreEqual(1, c.Count);
            Assert.IsTrue(c.ContainsKey(100));
            Assert.IsTrue(c.TryGetValue(100, out string data, out float remaining));
            Assert.AreEqual("hello", data);
            Assert.AreEqual(5f, remaining, 0.001f);
            c.Dispose();
        }

        [Test]
        public void TimedData_IntHash_RemoveAndExpire()
        {
            var c = new TimedDataContainer<string>(8);
            c.Add(100, "a", 2f);
            c.Add(200, "b", 1f);

            Assert.IsTrue(c.Remove(100));
            Assert.IsFalse(c.ContainsKey(100));
            Assert.AreEqual(1, c.Count);

            c.Update(1.5f);
            Assert.AreEqual(0, c.Count);
            c.Dispose();
        }

        // =============================================
        // PriorityPoolContainer
        // =============================================

        [Test]
        public void PriorityPool_IntHash_AddAndGet()
        {
            var c = new PriorityPoolContainer<string>(8);
            c.Add(100, "item", 5f, 10f);

            Assert.AreEqual(1, c.Count);
            Assert.IsTrue(c.ContainsKey(100));
            Assert.IsTrue(c.TryGetValue(100, out string data));
            Assert.AreEqual("item", data);
            Assert.AreEqual(5f, c.GetPriority(100));
            c.Dispose();
        }

        [Test]
        public void PriorityPool_IntHash_RemoveAndEvict()
        {
            var c = new PriorityPoolContainer<string>(2);
            c.Add(100, "low", 1f);
            c.Add(200, "high", 10f);

            Assert.IsTrue(c.TryAddOrEvict(300, "higher", out string evicted, 5f));
            Assert.AreEqual("low", evicted);
            Assert.IsFalse(c.ContainsKey(100));
            Assert.IsTrue(c.ContainsKey(300));
            c.Dispose();
        }

        [Test]
        public void PriorityPool_IntHash_UpdatePriority()
        {
            var c = new PriorityPoolContainer<string>(8);
            c.Add(100, "x", 1f);
            c.UpdatePriority(100, 99f);
            Assert.AreEqual(99f, c.GetPriority(100));
            c.Dispose();
        }

        // =============================================
        // GroupContainer
        // =============================================

        [Test]
        public void Group_IntHash_AddAndQuery()
        {
            var c = new GroupContainer<string>(8, new[] { "ally", "enemy" });
            c.Add(100, "warrior", "ally");
            c.Add(200, "goblin", "enemy");

            Assert.AreEqual(2, c.Count);
            Assert.IsTrue(c.ContainsKey(100));

            Assert.IsTrue(c.TryGetValue(100, out string data, out string group));
            Assert.AreEqual("warrior", data);
            Assert.AreEqual("ally", group);

            Assert.AreEqual(1, c.GetGroupCount("ally"));
            Assert.AreEqual(1, c.GetGroupCount("enemy"));
            c.Dispose();
        }

        [Test]
        public void Group_IntHash_MoveAndRemove()
        {
            var c = new GroupContainer<string>(8, new[] { "alive", "dead" });
            c.Add(100, "npc", "alive");

            Assert.IsTrue(c.MoveToGroup(100, "dead"));
            Assert.AreEqual(0, c.GetGroupCount("alive"));
            Assert.AreEqual(1, c.GetGroupCount("dead"));

            Assert.IsTrue(c.Remove(100));
            Assert.AreEqual(0, c.Count);
            c.Dispose();
        }

        // =============================================
        // StackableEffectContainer
        // =============================================

        private struct TestEffect
        {
            public float DamagePerTick;
        }

        [Test]
        public void StackableEffect_IntHash_ApplyAndQuery()
        {
            var c = new StackableEffectContainer<TestEffect>(8);
            c.AddOwner(100);
            Assert.IsTrue(c.ContainsOwner(100));

            var effect = new TestEffect { DamagePerTick = 5f };
            Assert.IsTrue(c.Apply(100, 1, effect, 3, 10f));
            Assert.IsTrue(c.IsActive(100, 1));
            Assert.AreEqual(3, c.GetStacks(100, 1));
            Assert.AreEqual(1, c.GetActiveCount(100));

            Span<(int effectKey, TestEffect effectData, int stacks, float remainingTime)> results
                = stackalloc (int, TestEffect, int, float)[4];
            int count = c.GetActiveEffects(100, results);
            Assert.AreEqual(1, count);
            Assert.AreEqual(1, results[0].effectKey);
            Assert.AreEqual(3, results[0].stacks);

            c.Dispose();
        }

        [Test]
        public void StackableEffect_IntHash_RemoveOwnerCleansEffects()
        {
            var c = new StackableEffectContainer<TestEffect>(8);
            c.AddOwner(200);
            c.Apply(200, 1, default, 1, 5f);
            c.Apply(200, 2, default, 2, 10f);

            Assert.IsTrue(c.RemoveOwner(200));
            Assert.AreEqual(0, c.OwnerCount);
            Assert.AreEqual(0, c.EffectCount);
            c.Dispose();
        }

        // =============================================
        // ThresholdAccumulatorContainer
        // =============================================

        [Test]
        public void Threshold_IntHash_RegisterAndAccumulate()
        {
            var c = new ThresholdAccumulatorContainer(8);
            c.AddOwner(100);
            Assert.IsTrue(c.ContainsOwner(100));

            c.Register(100, 0, 100f);
            Assert.IsFalse(c.Add(100, 0, 50f));
            Assert.AreEqual(50f, c.Get(100, 0), 0.001f);
            Assert.AreEqual(0.5f, c.GetNormalized(100, 0), 0.001f);

            Assert.IsTrue(c.Add(100, 0, 60f));
            Assert.AreEqual(0f, c.Get(100, 0), 0.001f);
            c.Dispose();
        }

        [Test]
        public void Threshold_IntHash_SetThresholdAndReset()
        {
            var c = new ThresholdAccumulatorContainer(8);
            c.AddOwner(200);
            c.Register(200, 0, 50f);
            c.Add(200, 0, 25f);

            c.SetThreshold(200, 0, 200f);
            Assert.AreEqual(0.125f, c.GetNormalized(200, 0), 0.001f);

            c.Reset(200, 0);
            Assert.AreEqual(0f, c.Get(200, 0), 0.001f);
            c.Dispose();
        }

        [Test]
        public void Threshold_IntHash_RemoveOwner()
        {
            var c = new ThresholdAccumulatorContainer(8);
            c.AddOwner(300);
            c.Register(300, 0, 100f);
            c.Add(300, 0, 10f);

            Assert.IsTrue(c.RemoveOwner(300));
            Assert.AreEqual(0, c.OwnerCount);
            Assert.AreEqual(0, c.AccumulatorCount);
            c.Dispose();
        }

        // =============================================
        // StateMapContainer
        // =============================================

        private enum TestState { Idle, Walk, Run, Attack }

        [Test]
        public void StateMap_IntHash_AddAndGetState()
        {
            var c = new StateMapContainer<TestState>(8);
            c.Add(100, TestState.Idle);

            Assert.AreEqual(1, c.Count);
            Assert.IsTrue(c.ContainsKey(100));
            Assert.IsTrue(c.TryGetState(100, out TestState current));
            Assert.AreEqual(TestState.Idle, current);
            Assert.AreEqual(TestState.Idle, c.GetState(100));
            c.Dispose();
        }

        [Test]
        public void StateMap_IntHash_SetStateAndHistory()
        {
            var c = new StateMapContainer<TestState>(8);
            c.Add(100, TestState.Idle);

            Assert.IsTrue(c.SetState(100, TestState.Walk));
            Assert.IsTrue(c.TryGetState(100, out TestState cur, out TestState prev, out float elapsed));
            Assert.AreEqual(TestState.Walk, cur);
            Assert.AreEqual(TestState.Idle, prev);
            Assert.AreEqual(0f, elapsed, 0.001f);

            c.Update(0.5f);
            c.TryGetState(100, out _, out _, out elapsed);
            Assert.AreEqual(0.5f, elapsed, 0.001f);
            c.Dispose();
        }

        [Test]
        public void StateMap_IntHash_RemoveAndGetAllInState()
        {
            var c = new StateMapContainer<TestState>(8);
            c.Add(100, TestState.Idle);
            c.Add(200, TestState.Walk);
            c.Add(300, TestState.Idle);

            Assert.IsTrue(c.Remove(200));
            Assert.AreEqual(2, c.Count);

            Span<int> results = stackalloc int[4];
            int count = c.GetAllInState(TestState.Idle, results);
            Assert.AreEqual(2, count);
            c.Dispose();
        }

        // =============================================
        // SpatialHashContainer2D (位置注入API含む)
        // =============================================

        private class SpatialData
        {
            public int Id;
            public SpatialData(int id) => Id = id;
        }

        [Test]
        public void Spatial2D_IntHash_AddWithPosition()
        {
            var c = new SpatialHashContainer2D<SpatialData>(10f, 16);
            c.Add(100, new SpatialData(1), new Vector3(5f, 0f, 5f));

            Assert.AreEqual(1, c.Count);
            Assert.IsTrue(c.ContainsKey(100));
            Assert.IsTrue(c.TryGetValue(100, out SpatialData data));
            Assert.AreEqual(1, data.Id);
            c.Dispose();
        }

        [Test]
        public void Spatial2D_IntHash_QueryNeighbors()
        {
            var c = new SpatialHashContainer2D<SpatialData>(10f, 16);
            c.Add(100, new SpatialData(1), new Vector3(0f, 0f, 0f));
            c.Add(200, new SpatialData(2), new Vector3(5f, 0f, 0f));
            c.Add(300, new SpatialData(3), new Vector3(100f, 0f, 0f));

            Span<SpatialData> results = new SpatialData[4];
            int found = c.QueryNeighbors(Vector3.zero, 10f, results);
            Assert.AreEqual(2, found);
            c.Dispose();
        }

        [Test]
        public void Spatial2D_UpdatePosition_RehashesCorrectly()
        {
            var c = new SpatialHashContainer2D<SpatialData>(10f, 16);
            c.Add(100, new SpatialData(1), new Vector3(0f, 0f, 0f));

            // クエリ: 原点付近 → 見つかる
            Span<SpatialData> results = new SpatialData[4];
            Assert.AreEqual(1, c.QueryNeighbors(Vector3.zero, 5f, results));

            // 位置を遠くに移動
            c.UpdatePosition(100, new Vector3(200f, 0f, 200f));

            // 原点付近のクエリ → 見つからない
            Assert.AreEqual(0, c.QueryNeighbors(Vector3.zero, 5f, results));

            // 移動先付近のクエリ → 見つかる
            Assert.AreEqual(1, c.QueryNeighbors(new Vector3(200f, 0f, 200f), 5f, results));
            c.Dispose();
        }

        [Test]
        public void Spatial2D_UpdatePositions_Batch()
        {
            var c = new SpatialHashContainer2D<SpatialData>(10f, 16);
            c.Add(100, new SpatialData(1), Vector3.zero);
            c.Add(200, new SpatialData(2), Vector3.zero);

            int[] hashes = { 100, 200 };
            Vector3[] positions = { new Vector3(50f, 0f, 50f), new Vector3(50f, 0f, 50f) };
            c.UpdatePositions(hashes, positions);

            Span<SpatialData> results = new SpatialData[4];
            Assert.AreEqual(0, c.QueryNeighbors(Vector3.zero, 5f, results));
            Assert.AreEqual(2, c.QueryNeighbors(new Vector3(50f, 0f, 50f), 5f, results));
            c.Dispose();
        }

        [Test]
        public void Spatial2D_IntHash_GetPosition()
        {
            var c = new SpatialHashContainer2D<SpatialData>(10f, 16);
            var pos = new Vector3(42f, 0f, 13f);
            c.Add(100, new SpatialData(1), pos);

            Assert.AreEqual(pos, c.GetPosition(100));
            c.Dispose();
        }

        [Test]
        public void Spatial2D_IntHash_Remove()
        {
            var c = new SpatialHashContainer2D<SpatialData>(10f, 16);
            c.Add(100, new SpatialData(1), Vector3.zero);
            Assert.IsTrue(c.Remove(100));
            Assert.AreEqual(0, c.Count);
            Assert.IsFalse(c.ContainsKey(100));
            c.Dispose();
        }

        // =============================================
        // SpatialHashContainer3D
        // =============================================

        [Test]
        public void Spatial3D_IntHash_AddQueryRemove()
        {
            var c = new SpatialHashContainer3D<SpatialData>(10f, 16);
            c.Add(100, new SpatialData(1), new Vector3(0f, 0f, 0f));
            c.Add(200, new SpatialData(2), new Vector3(5f, 5f, 5f));
            c.Add(300, new SpatialData(3), new Vector3(100f, 100f, 100f));

            Assert.AreEqual(3, c.Count);

            Span<SpatialData> results = new SpatialData[4];
            int found = c.QueryNeighbors(Vector3.zero, 15f, results);
            Assert.AreEqual(2, found);

            Assert.IsTrue(c.Remove(100));
            Assert.AreEqual(2, c.Count);
            c.Dispose();
        }

        [Test]
        public void Spatial3D_IntHash_UpdatePosition()
        {
            var c = new SpatialHashContainer3D<SpatialData>(10f, 16);
            c.Add(100, new SpatialData(1), Vector3.zero);

            c.UpdatePosition(100, new Vector3(200f, 200f, 200f));

            Span<SpatialData> results = new SpatialData[4];
            Assert.AreEqual(0, c.QueryNeighbors(Vector3.zero, 5f, results));
            Assert.AreEqual(1, c.QueryNeighbors(new Vector3(200f, 200f, 200f), 5f, results));
            c.Dispose();
        }
    }
}
