using System;
using NUnit.Framework;
using ODC.Runtime;

namespace ODC.Tests
{
    /// <summary>
    /// FactionRelationContainer のユニットテスト
    /// </summary>
    [TestFixture]
    public class FactionRelationContainerTests
    {
        private FactionRelationContainer _container;
        private int _player, _enemy, _neutral, _ally;

        [SetUp]
        public void SetUp()
        {
            _container = new FactionRelationContainer();
            _player = _container.RegisterFaction();
            _enemy = _container.RegisterFaction();
            _neutral = _container.RegisterFaction();
            _ally = _container.RegisterFaction();
        }

        [TearDown]
        public void TearDown()
        {
            _container.Dispose();
        }

        #region RegisterFaction

        [Test]
        public void RegisterFaction_AssignsSequentialIds()
        {
            Assert.AreEqual(0, _player);
            Assert.AreEqual(1, _enemy);
            Assert.AreEqual(2, _neutral);
            Assert.AreEqual(3, _ally);
            Assert.AreEqual(4, _container.FactionCount);
        }

        [Test]
        public void RegisterFaction_ExceedsMax_ThrowsException()
        {
            var container = new FactionRelationContainer();
            for (int i = 0; i < FactionRelationContainer.MaxFactions; i++)
                container.RegisterFaction();

            Assert.Throws<InvalidOperationException>(() => container.RegisterFaction());
            container.Dispose();
        }

        #endregion

        #region Get / Set

        [Test]
        public void Set_Symmetric_BothDirectionsMatch()
        {
            _container.Set(_player, _enemy, FactionRelationContainer.Relation.Hostile);

            Assert.AreEqual(FactionRelationContainer.Relation.Hostile, _container.Get(_player, _enemy));
            Assert.AreEqual(FactionRelationContainer.Relation.Hostile, _container.Get(_enemy, _player));
        }

        [Test]
        public void Get_SelfRelation_AlwaysAllied()
        {
            Assert.AreEqual(FactionRelationContainer.Relation.Allied, _container.Get(_player, _player));
        }

        [Test]
        public void Get_DefaultRelation_IsNeutral()
        {
            Assert.AreEqual(FactionRelationContainer.Relation.Neutral, _container.Get(_player, _enemy));
        }

        [Test]
        public void SetOneWay_Asymmetric()
        {
            _container.SetOneWay(_player, _enemy, FactionRelationContainer.Relation.Hostile);

            Assert.AreEqual(FactionRelationContainer.Relation.Hostile, _container.Get(_player, _enemy));
            Assert.AreEqual(FactionRelationContainer.Relation.Neutral, _container.Get(_enemy, _player));
        }

        #endregion

        #region SetTemporary / Tick

        [Test]
        public void SetTemporary_OverridesRelation()
        {
            _container.Set(_player, _enemy, FactionRelationContainer.Relation.Hostile);
            _container.SetTemporary(_player, _enemy, FactionRelationContainer.Relation.Allied, 5f);

            Assert.AreEqual(FactionRelationContainer.Relation.Allied, _container.Get(_player, _enemy));
        }

        [Test]
        public void Tick_RestoresAfterExpiry()
        {
            _container.Set(_player, _enemy, FactionRelationContainer.Relation.Hostile);
            _container.SetTemporary(_player, _enemy, FactionRelationContainer.Relation.Allied, 2f);

            // まだ期限内
            _container.Tick(1f);
            Assert.AreEqual(FactionRelationContainer.Relation.Allied, _container.Get(_player, _enemy));

            // 期限切れ
            _container.Tick(1.5f);
            Assert.AreEqual(FactionRelationContainer.Relation.Hostile, _container.Get(_player, _enemy));
        }

        #endregion

        #region GetHostile / GetAllied

        [Test]
        public void GetHostile_ReturnsHostileFactions()
        {
            _container.Set(_player, _enemy, FactionRelationContainer.Relation.Hostile);
            _container.Set(_player, _ally, FactionRelationContainer.Relation.Allied);

            Span<int> results = stackalloc int[8];
            int count = _container.GetHostile(_player, results);

            Assert.AreEqual(1, count);
            Assert.AreEqual(_enemy, results[0]);
        }

        [Test]
        public void GetAllied_ReturnsAlliedFactions()
        {
            _container.Set(_player, _ally, FactionRelationContainer.Relation.Allied);
            _container.Set(_player, _enemy, FactionRelationContainer.Relation.Hostile);

            Span<int> results = stackalloc int[8];
            int count = _container.GetAllied(_player, results);

            Assert.AreEqual(1, count);
            Assert.AreEqual(_ally, results[0]);
        }

        #endregion

        #region Clear

        [Test]
        public void Clear_ResetsAll()
        {
            _container.Set(_player, _enemy, FactionRelationContainer.Relation.Hostile);

            _container.Clear();

            Assert.AreEqual(0, _container.FactionCount);
        }

        #endregion

        #region Validation

        [Test]
        public void Get_InvalidFaction_ThrowsException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _container.Get(99, _player));
        }

        #endregion

        #region SetTemporary Bidirectional Restore

        [Test]
        public void SetTemporary_RestoresBothDirections()
        {
            // SetOneWayで非対称関係を作成
            _container.SetOneWay(_player, _enemy, FactionRelationContainer.Relation.Hostile);
            _container.SetOneWay(_enemy, _player, FactionRelationContainer.Relation.Allied);

            // 一時的に変更
            _container.SetTemporary(_player, _enemy, FactionRelationContainer.Relation.Neutral, 1f);

            Assert.AreEqual(FactionRelationContainer.Relation.Neutral, _container.Get(_player, _enemy));
            Assert.AreEqual(FactionRelationContainer.Relation.Neutral, _container.Get(_enemy, _player));

            // タイムアウト → 元の非対称関係に戻る
            _container.Tick(2f);

            Assert.AreEqual(FactionRelationContainer.Relation.Hostile, _container.Get(_player, _enemy));
            Assert.AreEqual(FactionRelationContainer.Relation.Allied, _container.Get(_enemy, _player));
        }

        #endregion
    }
}
