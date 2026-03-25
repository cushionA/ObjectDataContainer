using System;
using NUnit.Framework;
using ODC.Runtime;

namespace ODC.Tests
{
    /// <summary>
    /// FlagComboLookupContainer のユニットテスト
    /// </summary>
    [TestFixture]
    public class FlagComboLookupContainerTests
    {
        private struct ResonanceEffect
        {
            public float DamageMultiplier;
            public int ProcStatusId;
            public ResonanceEffect(float mult, int proc = 0) { DamageMultiplier = mult; ProcStatusId = proc; }
        }

        private const ulong Fire = 1UL << 0;
        private const ulong Ice = 1UL << 1;
        private const ulong Thunder = 1UL << 2;
        private const ulong Water = 1UL << 3;

        #region Build

        [Test]
        public void Build_ValidInput_CreatesContainer()
        {
            var combos = new ulong[] { Fire | Ice, Thunder | Water };
            var effects = new ResonanceEffect[] { new(1.5f), new(1.3f) };

            var container = FlagComboLookupContainer<ResonanceEffect>.Build(combos, effects);

            Assert.AreEqual(2, container.Count);
            container.Dispose();
        }

        [Test]
        public void Build_MismatchedLengths_ThrowsException()
        {
            var combos = new ulong[] { Fire };
            var effects = new ResonanceEffect[] { new(1f), new(2f) };

            Assert.Throws<ArgumentException>(() =>
                FlagComboLookupContainer<ResonanceEffect>.Build(combos, effects));
        }

        #endregion

        #region TryGet

        [Test]
        public void TryGet_ExactMatch_ReturnsTrue()
        {
            var combos = new ulong[] { Fire | Ice };
            var effects = new ResonanceEffect[] { new(1.5f) };
            var container = FlagComboLookupContainer<ResonanceEffect>.Build(combos, effects);

            Assert.IsTrue(container.TryGet(Fire | Ice, out var effect));
            Assert.AreEqual(1.5f, effect.DamageMultiplier);
            container.Dispose();
        }

        [Test]
        public void TryGet_SupersetMatch_ReturnsTrue()
        {
            // Fire|Ice を登録 → Fire|Ice|Thunder でもマッチ
            var combos = new ulong[] { Fire | Ice };
            var effects = new ResonanceEffect[] { new(1.5f) };
            var container = FlagComboLookupContainer<ResonanceEffect>.Build(combos, effects);

            Assert.IsTrue(container.TryGet(Fire | Ice | Thunder, out var effect));
            Assert.AreEqual(1.5f, effect.DamageMultiplier);
            container.Dispose();
        }

        [Test]
        public void TryGet_PartialMatch_ReturnsFalse()
        {
            // Fire|Ice を登録 → Fire のみではマッチしない
            var combos = new ulong[] { Fire | Ice };
            var effects = new ResonanceEffect[] { new(1.5f) };
            var container = FlagComboLookupContainer<ResonanceEffect>.Build(combos, effects);

            Assert.IsFalse(container.TryGet(Fire, out _));
            container.Dispose();
        }

        [Test]
        public void TryGet_NoMatch_ReturnsFalse()
        {
            var combos = new ulong[] { Fire | Ice };
            var effects = new ResonanceEffect[] { new(1.5f) };
            var container = FlagComboLookupContainer<ResonanceEffect>.Build(combos, effects);

            Assert.IsFalse(container.TryGet(Thunder | Water, out _));
            container.Dispose();
        }

        [Test]
        public void TryGet_MultipleEntries_ReturnsFirst()
        {
            var combos = new ulong[] { Fire | Ice, Fire | Thunder };
            var effects = new ResonanceEffect[] { new(1.5f), new(1.3f) };
            var container = FlagComboLookupContainer<ResonanceEffect>.Build(combos, effects);

            // Fire|Ice|Thunder で両方マッチ → 最初の Fire|Ice が返る
            Assert.IsTrue(container.TryGet(Fire | Ice | Thunder, out var effect));
            Assert.AreEqual(1.5f, effect.DamageMultiplier);
            container.Dispose();
        }

        #endregion

        #region GetAll

        [Test]
        public void GetAll_ReturnsAllMatches()
        {
            var combos = new ulong[] { Fire | Ice, Fire | Thunder, Ice | Water };
            var effects = new ResonanceEffect[] { new(1.5f), new(1.3f), new(1.2f) };
            var container = FlagComboLookupContainer<ResonanceEffect>.Build(combos, effects);

            Span<ResonanceEffect> results = stackalloc ResonanceEffect[4];
            int count = container.GetAll(Fire | Ice | Thunder, results);

            Assert.AreEqual(2, count); // Fire|Ice と Fire|Thunder がマッチ
            container.Dispose();
        }

        [Test]
        public void GetAll_NoMatch_ReturnsZero()
        {
            var combos = new ulong[] { Fire | Ice };
            var effects = new ResonanceEffect[] { new(1.5f) };
            var container = FlagComboLookupContainer<ResonanceEffect>.Build(combos, effects);

            Span<ResonanceEffect> results = stackalloc ResonanceEffect[4];
            int count = container.GetAll(Water, results);

            Assert.AreEqual(0, count);
            container.Dispose();
        }

        #endregion

        #region Empty

        [Test]
        public void Build_Empty_Works()
        {
            var container = FlagComboLookupContainer<ResonanceEffect>.Build(new ulong[0], new ResonanceEffect[0]);

            Assert.AreEqual(0, container.Count);
            Assert.IsFalse(container.TryGet(Fire, out _));
            container.Dispose();
        }

        #endregion
    }
}
