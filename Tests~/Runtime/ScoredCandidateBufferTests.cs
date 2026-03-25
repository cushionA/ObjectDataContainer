using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Tests
{
    /// <summary>
    /// ScoredCandidateBuffer のユニットテスト
    /// </summary>
    [TestFixture]
    public class ScoredCandidateBufferTests
    {
        private List<GameObject> _createdObjects;

        private struct CandidateData
        {
            public int Id;
            public CandidateData(int id) { Id = id; }
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

        #region AddOwner / Submit / TryGetBest

        [Test]
        public void Submit_TryGetBest_ReturnsHighestScore()
        {
            var buffer = new ScoredCandidateBuffer<CandidateData>(16, 8);
            buffer.AddOwner(100);

            buffer.BeginEvaluation(100);
            buffer.Submit(100, new CandidateData(1), 5f);
            buffer.Submit(100, new CandidateData(2), 10f);
            buffer.Submit(100, new CandidateData(3), 3f);

            Assert.IsTrue(buffer.TryGetBest(100, out var best, out float score));
            Assert.AreEqual(2, best.Id);
            Assert.AreEqual(10f, score);
            buffer.Dispose();
        }

        [Test]
        public void TryGetBest_NoCandidates_ReturnsFalse()
        {
            var buffer = new ScoredCandidateBuffer<CandidateData>(16, 8);
            buffer.AddOwner(100);
            buffer.BeginEvaluation(100);

            Assert.IsFalse(buffer.TryGetBest(100, out _, out _));
            buffer.Dispose();
        }

        [Test]
        public void TryGetBest_NonExistentOwner_ReturnsFalse()
        {
            var buffer = new ScoredCandidateBuffer<CandidateData>(16, 8);
            Assert.IsFalse(buffer.TryGetBest(999, out _, out _));
            buffer.Dispose();
        }

        #endregion

        #region BeginEvaluation

        [Test]
        public void BeginEvaluation_ClearsPreviousCandidates()
        {
            var buffer = new ScoredCandidateBuffer<CandidateData>(16, 8);
            buffer.AddOwner(100);

            buffer.BeginEvaluation(100);
            buffer.Submit(100, new CandidateData(1), 10f);
            Assert.AreEqual(1, buffer.GetCount(100));

            buffer.BeginEvaluation(100);
            Assert.AreEqual(0, buffer.GetCount(100));
            buffer.Dispose();
        }

        #endregion

        #region Buffer Full / Eviction

        [Test]
        public void Submit_BufferFull_ReplacesLowestScore()
        {
            var buffer = new ScoredCandidateBuffer<CandidateData>(16, 3); // 3候補まで
            buffer.AddOwner(100);
            buffer.BeginEvaluation(100);

            buffer.Submit(100, new CandidateData(1), 5f);
            buffer.Submit(100, new CandidateData(2), 10f);
            buffer.Submit(100, new CandidateData(3), 3f);

            // バッファ満杯状態でスコア8を提出 → スコア3の候補を置換
            buffer.Submit(100, new CandidateData(4), 8f);

            Assert.AreEqual(3, buffer.GetCount(100));
            Assert.IsTrue(buffer.TryGetBest(100, out var best, out _));
            Assert.AreEqual(2, best.Id); // スコア10が最大
            buffer.Dispose();
        }

        [Test]
        public void Submit_BufferFull_LowerScoreIgnored()
        {
            var buffer = new ScoredCandidateBuffer<CandidateData>(16, 2);
            buffer.AddOwner(100);
            buffer.BeginEvaluation(100);

            buffer.Submit(100, new CandidateData(1), 5f);
            buffer.Submit(100, new CandidateData(2), 10f);

            // スコア2 → 既存の最低(5)より低いので無視
            buffer.Submit(100, new CandidateData(3), 2f);

            Assert.AreEqual(2, buffer.GetCount(100));
            buffer.Dispose();
        }

        #endregion

        #region GetTopK

        [Test]
        public void GetTopK_ReturnsInScoreOrder()
        {
            var buffer = new ScoredCandidateBuffer<CandidateData>(16, 8);
            buffer.AddOwner(100);
            buffer.BeginEvaluation(100);

            buffer.Submit(100, new CandidateData(1), 3f);
            buffer.Submit(100, new CandidateData(2), 10f);
            buffer.Submit(100, new CandidateData(3), 7f);
            buffer.Submit(100, new CandidateData(4), 1f);

            Span<CandidateData> results = stackalloc CandidateData[3];
            int count = buffer.GetTopK(100, results, 3);

            Assert.AreEqual(3, count);
            Assert.AreEqual(2, results[0].Id); // スコア10
            Assert.AreEqual(3, results[1].Id); // スコア7
            Assert.AreEqual(1, results[2].Id); // スコア3
            buffer.Dispose();
        }

        #endregion

        #region RemoveOwner

        [Test]
        public void RemoveOwner_BackSwapPreservesOthers()
        {
            var buffer = new ScoredCandidateBuffer<CandidateData>(16, 8);
            buffer.AddOwner(1);
            buffer.AddOwner(2);

            buffer.BeginEvaluation(1);
            buffer.Submit(1, new CandidateData(10), 5f);
            buffer.BeginEvaluation(2);
            buffer.Submit(2, new CandidateData(20), 8f);

            buffer.RemoveOwner(1);

            Assert.AreEqual(1, buffer.OwnerCount);
            Assert.IsTrue(buffer.TryGetBest(2, out var best, out _));
            Assert.AreEqual(20, best.Id);
            buffer.Dispose();
        }

        #endregion

        #region GameObject overloads

        [Test]
        public void WithGameObject_FullWorkflow()
        {
            var buffer = new ScoredCandidateBuffer<CandidateData>(16, 8);
            var go = CreateGameObject("AI");
            buffer.AddOwner(go);

            buffer.BeginEvaluation(go);
            buffer.Submit(go, new CandidateData(1), 5f);
            buffer.Submit(go, new CandidateData(2), 10f);

            Assert.IsTrue(buffer.TryGetBest(go, out var best, out _));
            Assert.AreEqual(2, best.Id);
            buffer.Dispose();
        }

        #endregion
    }
}
