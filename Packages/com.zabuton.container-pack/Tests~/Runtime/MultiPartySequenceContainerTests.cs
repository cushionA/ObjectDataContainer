using System;
using NUnit.Framework;
using ODC.Runtime;

namespace ODC.Tests
{
    /// <summary>
    /// MultiPartySequenceContainer のユニットテスト
    /// </summary>
    [TestFixture]
    public class MultiPartySequenceContainerTests
    {
        private struct CoopState
        {
            public int ComboId;
            public int Progress;
            public CoopState(int comboId, int progress = 0) { ComboId = comboId; Progress = progress; }
        }

        #region Begin

        [Test]
        public void Begin_CreatesSequence()
        {
            var container = new MultiPartySequenceContainer<CoopState>(16);
            int seqId = container.Begin(
                new[] { 100, 200 },
                totalSteps: 3,
                windowPerStep: 5f,
                new CoopState(42));

            Assert.AreEqual(1, container.ActiveCount);
            Assert.IsTrue(container.TryGetSequence(seqId, out var state, out int step, out bool completed));
            Assert.AreEqual(42, state.ComboId);
            Assert.AreEqual(0, step);
            Assert.IsFalse(completed);
            container.Dispose();
        }

        [Test]
        public void Begin_ReturnsUniqueIds()
        {
            var container = new MultiPartySequenceContainer<CoopState>(16);
            int id1 = container.Begin(new[] { 100 }, 1, 5f, default);
            int id2 = container.Begin(new[] { 200 }, 1, 5f, default);

            Assert.AreNotEqual(id1, id2);
            container.Dispose();
        }

        #endregion

        #region Advance

        [Test]
        public void Advance_SingleParticipant_CompletesStep()
        {
            var container = new MultiPartySequenceContainer<CoopState>(16);
            int seqId = container.Begin(
                new[] { 100 },
                totalSteps: 2,
                windowPerStep: 5f,
                new CoopState(1));

            bool advanced = container.Advance(seqId, 100, new CoopState(1, 1));
            Assert.IsTrue(advanced);

            container.TryGetSequence(seqId, out _, out int step, out _);
            Assert.AreEqual(1, step);
            container.Dispose();
        }

        [Test]
        public void Advance_MultipleParticipants_RequiresAll()
        {
            var container = new MultiPartySequenceContainer<CoopState>(16);
            int seqId = container.Begin(
                new[] { 100, 200 },
                totalSteps: 1,
                windowPerStep: 5f,
                new CoopState(1));

            // 1人目だけ → まだ進まない
            bool result1 = container.Advance(seqId, 100, new CoopState(1, 1));
            Assert.IsFalse(result1);

            // 2人目 → ステップ進行
            bool result2 = container.Advance(seqId, 200, new CoopState(1, 1));
            Assert.IsTrue(result2);
            container.Dispose();
        }

        [Test]
        public void Advance_AllSteps_MarksCompleted()
        {
            var container = new MultiPartySequenceContainer<CoopState>(16);
            int seqId = container.Begin(
                new[] { 100 },
                totalSteps: 2,
                windowPerStep: 5f,
                new CoopState(1));

            container.Advance(seqId, 100, new CoopState(1, 1));
            container.Advance(seqId, 100, new CoopState(1, 2));

            Assert.IsTrue(container.IsCompleted(seqId));
            container.Dispose();
        }

        [Test]
        public void Advance_DuplicateInSameStep_Ignored()
        {
            var container = new MultiPartySequenceContainer<CoopState>(16);
            int seqId = container.Begin(
                new[] { 100, 200 },
                totalSteps: 1,
                windowPerStep: 5f,
                new CoopState(1));

            container.Advance(seqId, 100, default);
            // 同じ参加者が再度advance → 無視
            bool result = container.Advance(seqId, 100, default);
            Assert.IsFalse(result);
            container.Dispose();
        }

        #endregion

        #region Tick / Timeout

        [Test]
        public void Tick_TimeoutRemovesSequence()
        {
            var container = new MultiPartySequenceContainer<CoopState>(16);
            container.Begin(
                new[] { 100 },
                totalSteps: 1,
                windowPerStep: 2f,
                new CoopState(1));

            container.Tick(1f);
            Assert.AreEqual(1, container.ActiveCount);

            container.Tick(1.5f);
            Assert.AreEqual(0, container.ActiveCount);
            container.Dispose();
        }

        [Test]
        public void Tick_CompletedSequenceNotTimedOut()
        {
            var container = new MultiPartySequenceContainer<CoopState>(16);
            int seqId = container.Begin(
                new[] { 100 },
                totalSteps: 1,
                windowPerStep: 2f,
                new CoopState(1));

            container.Advance(seqId, 100, default);
            Assert.IsTrue(container.IsCompleted(seqId));

            // タイムアウト分を経過させても完了済みなので削除されない
            container.Tick(5f);
            Assert.AreEqual(1, container.ActiveCount);
            container.Dispose();
        }

        #endregion

        #region End

        [Test]
        public void End_RemovesSequence()
        {
            var container = new MultiPartySequenceContainer<CoopState>(16);
            int seqId = container.Begin(new[] { 100 }, 1, 5f, default);

            container.End(seqId);
            Assert.AreEqual(0, container.ActiveCount);
            container.Dispose();
        }

        #endregion

        #region Clear

        [Test]
        public void Clear_ResetsAll()
        {
            var container = new MultiPartySequenceContainer<CoopState>(16);
            container.Begin(new[] { 100 }, 1, 5f, default);
            container.Begin(new[] { 200 }, 1, 5f, default);

            container.Clear();
            Assert.AreEqual(0, container.ActiveCount);
            container.Dispose();
        }

        #endregion

        #region Participant Pool Compaction

        [Test]
        public void BeginEnd_Repeated_DoesNotExhaustParticipantPool()
        {
            // 参加者プールが正しくcompactされることを検証
            var container = new MultiPartySequenceContainer<CoopState>(4, maxParticipantsTotal: 8);

            // 4回Begin/Endを繰り返す → compactionなしなら8人分で枯渇する
            for (int cycle = 0; cycle < 8; cycle++)
            {
                int seqId = container.Begin(new[] { cycle * 10, cycle * 10 + 1 }, 1, 5f, default);
                container.End(seqId);
            }

            // まだ新規シーケンスを作成できる
            int finalSeq = container.Begin(new[] { 100, 101 }, 1, 5f, default);
            Assert.AreEqual(1, container.ActiveCount);
            container.Dispose();
        }

        #endregion
    }
}
