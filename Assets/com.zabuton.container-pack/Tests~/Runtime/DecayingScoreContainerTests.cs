using System;
using NUnit.Framework;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Tests
{
    /// <summary>
    /// DecayingScoreContainer のユニットテスト
    /// </summary>
    [TestFixture]
    public class DecayingScoreContainerTests
    {
        [Test]
        public void AddOwner_And_ContainsCheck()
        {
            var c = new DecayingScoreContainer(8, 10, 0.5f);
            c.AddOwner(100);
            Assert.AreEqual(1, c.OwnerCount);
            c.Dispose();
        }

        [Test]
        public void AddOwner_Duplicate_Throws()
        {
            var c = new DecayingScoreContainer(8, 10, 0.5f);
            c.AddOwner(100);
            Assert.Throws<InvalidOperationException>(() => c.AddOwner(100));
            c.Dispose();
        }

        [Test]
        public void AddScore_And_GetScore_NoDecay()
        {
            var c = new DecayingScoreContainer(8, 10, 0.5f);
            c.AddOwner(100);

            c.AddScore(100, 200, 10f, 0f);
            float score = c.GetScore(100, 200, 0f);
            Assert.AreEqual(10f, score, 0.001f);
            c.Dispose();
        }

        [Test]
        public void AddScore_Accumulates()
        {
            var c = new DecayingScoreContainer(8, 10, 0.5f);
            c.AddOwner(100);

            c.AddScore(100, 200, 10f, 0f);
            c.AddScore(100, 200, 5f, 0f); // 同じ時刻: 10 + 5 = 15
            float score = c.GetScore(100, 200, 0f);
            Assert.AreEqual(15f, score, 0.001f);
            c.Dispose();
        }

        [Test]
        public void GetScore_Decays_Over_Time()
        {
            float decayRate = 1f; // 1秒で e^-1 ≈ 0.3679
            var c = new DecayingScoreContainer(8, 10, decayRate);
            c.AddOwner(100);

            c.AddScore(100, 200, 100f, 0f);

            float scoreAt1 = c.GetScore(100, 200, 1f);
            float expected = 100f * Mathf.Exp(-1f);
            Assert.AreEqual(expected, scoreAt1, 0.1f);

            float scoreAt2 = c.GetScore(100, 200, 2f);
            float expected2 = 100f * Mathf.Exp(-2f);
            Assert.AreEqual(expected2, scoreAt2, 0.1f);
            c.Dispose();
        }

        [Test]
        public void AddScore_After_Decay_Accumulates_OnDecayedValue()
        {
            float decayRate = 1f;
            var c = new DecayingScoreContainer(8, 10, decayRate);
            c.AddOwner(100);

            c.AddScore(100, 200, 100f, 0f);
            // t=1: decayed = 100 * e^-1 ≈ 36.79, add 50 → 86.79
            c.AddScore(100, 200, 50f, 1f);
            float score = c.GetScore(100, 200, 1f);
            float expected = 100f * Mathf.Exp(-1f) + 50f;
            Assert.AreEqual(expected, score, 0.1f);
            c.Dispose();
        }

        [Test]
        public void GetHighest_ReturnsCorrectTarget()
        {
            var c = new DecayingScoreContainer(8, 10, 0.1f);
            c.AddOwner(100);

            c.AddScore(100, 201, 10f, 0f);
            c.AddScore(100, 202, 50f, 0f);
            c.AddScore(100, 203, 30f, 0f);

            int highest = c.GetHighest(100, 0f);
            Assert.AreEqual(202, highest);
            c.Dispose();
        }

        [Test]
        public void GetHighest_NoEntries_ReturnsZero()
        {
            var c = new DecayingScoreContainer(8, 10, 0.5f);
            c.AddOwner(100);

            int highest = c.GetHighest(100, 0f);
            Assert.AreEqual(0, highest);
            c.Dispose();
        }

        [Test]
        public void GetTopK_ReturnsSorted()
        {
            var c = new DecayingScoreContainer(8, 10, 0f); // decay=0 for easy testing
            c.AddOwner(100);

            c.AddScore(100, 201, 10f, 0f);
            c.AddScore(100, 202, 50f, 0f);
            c.AddScore(100, 203, 30f, 0f);
            c.AddScore(100, 204, 5f, 0f);

            Span<int> targets = stackalloc int[3];
            Span<float> scores = stackalloc float[3];
            int count = c.GetTopK(100, 0f, targets, scores);

            Assert.AreEqual(3, count);
            Assert.AreEqual(202, targets[0]); // 50
            Assert.AreEqual(203, targets[1]); // 30
            Assert.AreEqual(201, targets[2]); // 10

            Assert.AreEqual(50f, scores[0], 0.001f);
            Assert.AreEqual(30f, scores[1], 0.001f);
            Assert.AreEqual(10f, scores[2], 0.001f);
            c.Dispose();
        }

        [Test]
        public void RemoveTarget_RemovesEntry()
        {
            var c = new DecayingScoreContainer(8, 10, 0.5f);
            c.AddOwner(100);

            c.AddScore(100, 200, 10f, 0f);
            c.RemoveTarget(100, 200);

            float score = c.GetScore(100, 200, 0f);
            Assert.AreEqual(0f, score, 0.001f);
            c.Dispose();
        }

        [Test]
        public void RemoveOwner_ClearsAllScores()
        {
            var c = new DecayingScoreContainer(8, 10, 0.5f);
            c.AddOwner(100);
            c.AddScore(100, 200, 10f, 0f);
            c.AddScore(100, 201, 20f, 0f);

            Assert.IsTrue(c.RemoveOwner(100));
            Assert.AreEqual(0, c.OwnerCount);
            Assert.AreEqual(0, c.ScoreCount);
            c.Dispose();
        }

        [Test]
        public void Clear_ResetsEverything()
        {
            var c = new DecayingScoreContainer(8, 10, 0.5f);
            c.AddOwner(100);
            c.AddScore(100, 200, 10f, 0f);

            c.Clear();
            Assert.AreEqual(0, c.OwnerCount);
            Assert.AreEqual(0, c.ScoreCount);
            c.Dispose();
        }

        [Test]
        public void MultipleOwners_IndependentScores()
        {
            var c = new DecayingScoreContainer(8, 10, 0f);
            c.AddOwner(100);
            c.AddOwner(200);

            c.AddScore(100, 300, 10f, 0f);
            c.AddScore(200, 300, 99f, 0f);

            Assert.AreEqual(10f, c.GetScore(100, 300, 0f), 0.001f);
            Assert.AreEqual(99f, c.GetScore(200, 300, 0f), 0.001f);

            int highest100 = c.GetHighest(100, 0f);
            int highest200 = c.GetHighest(200, 0f);
            Assert.AreEqual(300, highest100);
            Assert.AreEqual(300, highest200);
            c.Dispose();
        }

        [Test]
        public void BackSwap_OwnerRemoval_MaintainsIntegrity()
        {
            var c = new DecayingScoreContainer(8, 10, 0f);
            c.AddOwner(100);
            c.AddOwner(200);
            c.AddOwner(300);

            c.AddScore(100, 400, 10f, 0f);
            c.AddScore(200, 400, 20f, 0f);
            c.AddScore(300, 400, 30f, 0f);

            // 最初のオーナーを削除（BackSwapで300が100の位置に移動）
            c.RemoveOwner(100);

            Assert.AreEqual(2, c.OwnerCount);
            Assert.AreEqual(20f, c.GetScore(200, 400, 0f), 0.001f);
            Assert.AreEqual(30f, c.GetScore(300, 400, 0f), 0.001f);
            c.Dispose();
        }
    }
}
