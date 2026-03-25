using System;
using System.Collections.Generic;
using NUnit.Framework;
using ODC.Runtime;

namespace ODC.Tests
{
    /// <summary>
    /// WeightedSamplerContainer のユニットテスト
    /// </summary>
    [TestFixture]
    public class WeightedSamplerContainerTests
    {
        private struct DropItem
        {
            public int ItemId;
            public DropItem(int id) { ItemId = id; }
        }

        #region Build

        [Test]
        public void Build_ValidInput_CreatesContainer()
        {
            var items = new DropItem[] { new(1), new(2), new(3) };
            var weights = new float[] { 1f, 1f, 1f };

            var sampler = WeightedSamplerContainer<DropItem>.Build(items, weights);

            Assert.AreEqual(3, sampler.Count);
            sampler.Dispose();
        }

        [Test]
        public void Build_EmptyArray_ThrowsException()
        {
            Assert.Throws<ArgumentException>(() =>
                WeightedSamplerContainer<DropItem>.Build(new DropItem[0], new float[0]));
        }

        [Test]
        public void Build_MismatchedLengths_ThrowsException()
        {
            var items = new DropItem[] { new(1) };
            var weights = new float[] { 1f, 2f };

            Assert.Throws<ArgumentException>(() =>
                WeightedSamplerContainer<DropItem>.Build(items, weights));
        }

        [Test]
        public void Build_NegativeWeight_ThrowsException()
        {
            var items = new DropItem[] { new(1) };
            var weights = new float[] { -1f };

            Assert.Throws<ArgumentException>(() =>
                WeightedSamplerContainer<DropItem>.Build(items, weights));
        }

        [Test]
        public void Build_AllZeroWeights_ThrowsException()
        {
            var items = new DropItem[] { new(1), new(2) };
            var weights = new float[] { 0f, 0f };

            Assert.Throws<ArgumentException>(() =>
                WeightedSamplerContainer<DropItem>.Build(items, weights));
        }

        #endregion

        #region Sample

        [Test]
        public void Sample_SingleItem_AlwaysReturnsSame()
        {
            var items = new DropItem[] { new(42) };
            var weights = new float[] { 1f };
            var sampler = WeightedSamplerContainer<DropItem>.Build(items, weights);

            for (int i = 0; i < 100; i++)
            {
                var result = sampler.Sample();
                Assert.AreEqual(42, result.ItemId);
            }
            sampler.Dispose();
        }

        [Test]
        public void Sample_ZeroWeightItem_NeverSampled()
        {
            var items = new DropItem[] { new(1), new(2) };
            var weights = new float[] { 0f, 1f };
            var sampler = WeightedSamplerContainer<DropItem>.Build(items, weights);

            for (int i = 0; i < 1000; i++)
            {
                var result = sampler.Sample();
                Assert.AreEqual(2, result.ItemId);
            }
            sampler.Dispose();
        }

        [Test]
        public void Sample_EqualWeights_AllItemsAppear()
        {
            var items = new DropItem[] { new(1), new(2), new(3) };
            var weights = new float[] { 1f, 1f, 1f };
            var sampler = WeightedSamplerContainer<DropItem>.Build(items, weights, seed: 67890);

            var counts = new Dictionary<int, int> { { 1, 0 }, { 2, 0 }, { 3, 0 } };
            for (int i = 0; i < 3000; i++)
            {
                var result = sampler.Sample();
                counts[result.ItemId]++;
            }

            // 各アイテムが少なくとも100回は出現するはず
            Assert.Greater(counts[1], 100);
            Assert.Greater(counts[2], 100);
            Assert.Greater(counts[3], 100);
            sampler.Dispose();
        }

        [Test]
        public void Sample_DeterministicWithSameSeed()
        {
            var items = new DropItem[] { new(1), new(2), new(3) };
            var weights = new float[] { 1f, 2f, 3f };

            var sampler1 = WeightedSamplerContainer<DropItem>.Build(items, weights, seed: 42);
            var sampler2 = WeightedSamplerContainer<DropItem>.Build(items, weights, seed: 42);

            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(sampler1.Sample().ItemId, sampler2.Sample().ItemId);
            }
            sampler1.Dispose();
            sampler2.Dispose();
        }

        #endregion

        #region SampleDistinct

        [Test]
        public void SampleDistinct_ReturnsUniqueItems()
        {
            var items = new DropItem[] { new(1), new(2), new(3), new(4), new(5) };
            var weights = new float[] { 1f, 1f, 1f, 1f, 1f };
            var sampler = WeightedSamplerContainer<DropItem>.Build(items, weights, seed: 12345);

            Span<DropItem> results = stackalloc DropItem[3];
            int count = sampler.SampleDistinct(results, 3);

            Assert.AreEqual(3, count);

            var ids = new HashSet<int>();
            for (int i = 0; i < count; i++)
                ids.Add(results[i].ItemId);

            Assert.AreEqual(3, ids.Count); // 全てユニーク
            sampler.Dispose();
        }

        [Test]
        public void SampleDistinct_AllItems_ReturnsAll()
        {
            var items = new DropItem[] { new(1), new(2), new(3) };
            var weights = new float[] { 1f, 1f, 1f };
            var sampler = WeightedSamplerContainer<DropItem>.Build(items, weights);

            Span<DropItem> results = stackalloc DropItem[3];
            int count = sampler.SampleDistinct(results, 3);

            Assert.AreEqual(3, count);
            sampler.Dispose();
        }

        [Test]
        public void SampleDistinct_KExceedsCount_ThrowsException()
        {
            var items = new DropItem[] { new(1), new(2) };
            var weights = new float[] { 1f, 1f };
            var sampler = WeightedSamplerContainer<DropItem>.Build(items, weights);

            var results = new DropItem[5];
            Assert.Throws<ArgumentException>(() => sampler.SampleDistinct(results, 3));
            sampler.Dispose();
        }

        #endregion
    }
}
