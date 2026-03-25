using System;
using NUnit.Framework;
using ODC.Runtime;

namespace ODC.Tests
{
    /// <summary>
    /// HitDeduplicationContainer のユニットテスト
    /// </summary>
    [TestFixture]
    public class HitDeduplicationContainerTests
    {
        #region TryRecord

        [Test]
        public void TryRecord_FirstHit_ReturnsTrue()
        {
            var container = new HitDeduplicationContainer(64);
            Assert.IsTrue(container.TryRecord(1, 100));
            Assert.AreEqual(1, container.Count);
            container.Dispose();
        }

        [Test]
        public void TryRecord_DuplicateHit_ReturnsFalse()
        {
            var container = new HitDeduplicationContainer(64);
            container.TryRecord(1, 100);
            Assert.IsFalse(container.TryRecord(1, 100));
            container.Dispose();
        }

        [Test]
        public void TryRecord_DifferentTargets_BothTrue()
        {
            var container = new HitDeduplicationContainer(64);
            Assert.IsTrue(container.TryRecord(1, 100));
            Assert.IsTrue(container.TryRecord(1, 200));
            Assert.AreEqual(2, container.Count);
            container.Dispose();
        }

        [Test]
        public void TryRecord_DifferentEvents_BothTrue()
        {
            var container = new HitDeduplicationContainer(64);
            Assert.IsTrue(container.TryRecord(1, 100));
            Assert.IsTrue(container.TryRecord(2, 100));
            Assert.AreEqual(2, container.Count);
            container.Dispose();
        }

        [Test]
        public void TryRecord_WithPierce_AllowsMultiple()
        {
            var container = new HitDeduplicationContainer(64);

            // maxPierce=3: 3回まで許可
            Assert.IsTrue(container.TryRecord(1, 100, maxPierce: 3));
            Assert.IsTrue(container.TryRecord(1, 100, maxPierce: 3));
            Assert.IsTrue(container.TryRecord(1, 100, maxPierce: 3));
            Assert.IsFalse(container.TryRecord(1, 100, maxPierce: 3));
            container.Dispose();
        }

        [Test]
        public void TryRecord_WithPierceRemaining_Decrements()
        {
            var container = new HitDeduplicationContainer(64);

            int pierce = 2;
            Assert.IsTrue(container.TryRecord(1, 100, ref pierce));
            Assert.AreEqual(1, pierce);
            container.Dispose();
        }

        #endregion

        #region Release

        [Test]
        public void Release_ClearsEventRecords()
        {
            var container = new HitDeduplicationContainer(64);
            container.TryRecord(1, 100);
            container.TryRecord(1, 200);
            container.TryRecord(2, 100);

            container.Release(1);

            Assert.AreEqual(1, container.Count);
            // event=2 はまだ残っている
            Assert.IsFalse(container.TryRecord(2, 100));
            // event=1 は解放されたので再度ヒット可能
            Assert.IsTrue(container.TryRecord(1, 100));
            container.Dispose();
        }

        #endregion

        #region GetHitCount

        [Test]
        public void GetHitCount_ReturnsCorrectCount()
        {
            var container = new HitDeduplicationContainer(64);
            container.TryRecord(1, 100, maxPierce: 0); // 無制限
            container.TryRecord(1, 100, maxPierce: 0);
            container.TryRecord(1, 100, maxPierce: 0);

            Assert.AreEqual(3, container.GetHitCount(1, 100));
            container.Dispose();
        }

        [Test]
        public void GetHitCount_NonExistent_ReturnsZero()
        {
            var container = new HitDeduplicationContainer(64);
            Assert.AreEqual(0, container.GetHitCount(1, 100));
            container.Dispose();
        }

        #endregion

        #region Clear

        [Test]
        public void Clear_ResetsAll()
        {
            var container = new HitDeduplicationContainer(64);
            container.TryRecord(1, 100);
            container.TryRecord(2, 200);

            container.Clear();

            Assert.AreEqual(0, container.Count);
            Assert.IsTrue(container.TryRecord(1, 100)); // 再度ヒット可能
            container.Dispose();
        }

        #endregion
    }
}
