using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ODC.Runtime;

namespace ODC.Tests
{
    /// <summary>
    /// テスト用ダミークラス
    /// </summary>
    public class TestEntityData
    {
        public string Name;
        public int Value;
    }

    /// <summary>
    /// StateMapContainer用のテスト列挙型
    /// </summary>
    public enum TestState
    {
        Idle,
        Walking,
        Running,
        Attacking,
        Dead
    }

    #region SpatialHashContainer2D Tests

    [TestFixture]
    public class SpatialHashContainer2DTests
    {
        private SpatialHashContainer2D<TestEntityData> _container;
        private GameObject[] _gameObjects;

        [SetUp]
        public void SetUp()
        {
            _container = new SpatialHashContainer2D<TestEntityData>(10f, 64);
            _gameObjects = new GameObject[10];
            for (int i = 0; i < _gameObjects.Length; i++)
            {
                _gameObjects[i] = new GameObject($"Test2D_{i}");
            }
        }

        [TearDown]
        public void TearDown()
        {
            _container.Dispose();
            foreach (var go in _gameObjects)
            {
                if (go != null)
                    GameObject.DestroyImmediate(go);
            }
        }

        [Test]
        public void Add_SingleElement_CountIsOne()
        {
            var data = new TestEntityData { Name = "A", Value = 1 };
            _gameObjects[0].transform.position = new Vector3(5f, 0f, 5f);

            _container.Add(_gameObjects[0], data);

            Assert.AreEqual(1, _container.Count);
        }

        [Test]
        public void Add_MultipleElements_CountMatches()
        {
            for (int i = 0; i < 5; i++)
            {
                _gameObjects[i].transform.position = new Vector3(i * 10f, 0f, i * 10f);
                _container.Add(_gameObjects[i], new TestEntityData { Name = $"E{i}", Value = i });
            }

            Assert.AreEqual(5, _container.Count);
        }

        [Test]
        public void ContainsKey_AfterAdd_ReturnsTrue()
        {
            _gameObjects[0].transform.position = Vector3.zero;
            _container.Add(_gameObjects[0], new TestEntityData { Name = "A" });

            Assert.IsTrue(_container.ContainsKey(_gameObjects[0]));
        }

        [Test]
        public void ContainsKey_NotAdded_ReturnsFalse()
        {
            Assert.IsFalse(_container.ContainsKey(_gameObjects[0]));
        }

        [Test]
        public void TryGetValue_ExistingElement_ReturnsTrueAndData()
        {
            var data = new TestEntityData { Name = "Found", Value = 42 };
            _gameObjects[0].transform.position = Vector3.zero;
            _container.Add(_gameObjects[0], data);

            bool found = _container.TryGetValue(_gameObjects[0], out var result);

            Assert.IsTrue(found);
            Assert.AreEqual("Found", result.Name);
            Assert.AreEqual(42, result.Value);
        }

        [Test]
        public void TryGetValue_NonExistingElement_ReturnsFalse()
        {
            bool found = _container.TryGetValue(_gameObjects[0], out _);

            Assert.IsFalse(found);
        }

        [Test]
        public void Remove_ExistingElement_ReturnsTrueAndDecrementsCount()
        {
            _gameObjects[0].transform.position = Vector3.zero;
            _container.Add(_gameObjects[0], new TestEntityData { Name = "A" });

            bool removed = _container.Remove(_gameObjects[0]);

            Assert.IsTrue(removed);
            Assert.AreEqual(0, _container.Count);
            Assert.IsFalse(_container.ContainsKey(_gameObjects[0]));
        }

        [Test]
        public void Remove_NonExistingElement_ReturnsFalse()
        {
            bool removed = _container.Remove(_gameObjects[0]);

            Assert.IsFalse(removed);
        }

        [Test]
        public void Remove_BackSwap_PreservesOtherElements()
        {
            for (int i = 0; i < 5; i++)
            {
                _gameObjects[i].transform.position = new Vector3(i * 20f, 0f, 0f);
                _container.Add(_gameObjects[i], new TestEntityData { Name = $"E{i}", Value = i });
            }

            // 中央の要素を削除
            _container.Remove(_gameObjects[2]);

            Assert.AreEqual(4, _container.Count);
            Assert.IsFalse(_container.ContainsKey(_gameObjects[2]));

            // 他の要素がまだ存在する
            for (int i = 0; i < 5; i++)
            {
                if (i == 2) continue;
                Assert.IsTrue(_container.ContainsKey(_gameObjects[i]),
                    $"Element {i} should still exist after removing element 2");
                _container.TryGetValue(_gameObjects[i], out var data);
                Assert.AreEqual(i, data.Value);
            }
        }

        [Test]
        public void GetPosition_ReturnsCurrentTransformPosition()
        {
            var pos = new Vector3(15f, 3f, 25f);
            _gameObjects[0].transform.position = pos;
            _container.Add(_gameObjects[0], new TestEntityData());

            Vector3 result = _container.GetPosition(_gameObjects[0]);

            Assert.AreEqual(pos.x, result.x, 0.001f);
            Assert.AreEqual(pos.y, result.y, 0.001f);
            Assert.AreEqual(pos.z, result.z, 0.001f);
        }

        [Test]
        public void QueryNeighbors_FindsElementsInRadius()
        {
            // 原点付近に3つ配置
            _gameObjects[0].transform.position = new Vector3(0f, 0f, 0f);
            _gameObjects[1].transform.position = new Vector3(3f, 0f, 0f);
            _gameObjects[2].transform.position = new Vector3(0f, 0f, 4f);
            // 遠くに1つ配置
            _gameObjects[3].transform.position = new Vector3(100f, 0f, 100f);

            _container.Add(_gameObjects[0], new TestEntityData { Name = "Near0" });
            _container.Add(_gameObjects[1], new TestEntityData { Name = "Near1" });
            _container.Add(_gameObjects[2], new TestEntityData { Name = "Near2" });
            _container.Add(_gameObjects[3], new TestEntityData { Name = "Far" });

            _container.Update();

            var results = new TestEntityData[10];
            int count = _container.QueryNeighbors(Vector3.zero, 5f, results);

            Assert.AreEqual(3, count);
        }

        [Test]
        public void QueryNeighbors_UsesXZPlaneOnly()
        {
            // XZ平面上では近いが、Y軸が異なる
            _gameObjects[0].transform.position = new Vector3(1f, 100f, 1f);
            _container.Add(_gameObjects[0], new TestEntityData { Name = "HighY" });
            _container.Update();

            var results = new TestEntityData[10];
            int count = _container.QueryNeighbors(Vector3.zero, 5f, results);

            // 2D（XZ平面）なのでY軸を無視して見つかるはず
            Assert.AreEqual(1, count);
        }

        [Test]
        public void QueryNeighbors_WithDistances_ReturnsCorrectDistances()
        {
            _gameObjects[0].transform.position = new Vector3(3f, 0f, 4f);
            _container.Add(_gameObjects[0], new TestEntityData { Name = "A" });
            _container.Update();

            var results = new TestEntityData[10];
            var distances = new float[10];
            int count = _container.QueryNeighbors(Vector3.zero, 10f, results, distances);

            Assert.AreEqual(1, count);
            // XZ距離: sqrt(3^2 + 4^2) = 5
            Assert.AreEqual(5f, distances[0], 0.01f);
        }

        [Test]
        public void QueryNeighbors_EmptyContainer_ReturnsZero()
        {
            var results = new TestEntityData[10];
            int count = _container.QueryNeighbors(Vector3.zero, 100f, results);

            Assert.AreEqual(0, count);
        }

        [Test]
        public void Update_RehashesMovedElements()
        {
            _gameObjects[0].transform.position = new Vector3(0f, 0f, 0f);
            _container.Add(_gameObjects[0], new TestEntityData { Name = "Mover" });
            _container.Update();

            // 検索で見つかることを確認
            var results = new TestEntityData[10];
            int count1 = _container.QueryNeighbors(Vector3.zero, 5f, results);
            Assert.AreEqual(1, count1);

            // オブジェクトを遠くに移動
            _gameObjects[0].transform.position = new Vector3(200f, 0f, 200f);
            _container.Update();

            // 元の位置では見つからない
            int count2 = _container.QueryNeighbors(Vector3.zero, 5f, results);
            Assert.AreEqual(0, count2);

            // 新しい位置で見つかる
            int count3 = _container.QueryNeighbors(new Vector3(200f, 0f, 200f), 5f, results);
            Assert.AreEqual(1, count3);
        }

        [Test]
        public void Clear_ResetsContainer()
        {
            for (int i = 0; i < 3; i++)
            {
                _gameObjects[i].transform.position = new Vector3(i, 0, i);
                _container.Add(_gameObjects[i], new TestEntityData { Name = $"E{i}" });
            }

            _container.Clear();

            Assert.AreEqual(0, _container.Count);
            for (int i = 0; i < 3; i++)
            {
                Assert.IsFalse(_container.ContainsKey(_gameObjects[i]));
            }
        }

        [Test]
        public void ActiveDataSpan_ReturnsCorrectLength()
        {
            for (int i = 0; i < 4; i++)
            {
                _gameObjects[i].transform.position = new Vector3(i, 0, 0);
                _container.Add(_gameObjects[i], new TestEntityData { Name = $"E{i}", Value = i });
            }

            var span = _container.ActiveDataSpan;

            Assert.AreEqual(4, span.Length);
        }

        [Test]
        public void Add_ThenRemoveAll_ThenAddAgain_Works()
        {
            for (int i = 0; i < 3; i++)
            {
                _gameObjects[i].transform.position = new Vector3(i, 0, 0);
                _container.Add(_gameObjects[i], new TestEntityData { Name = $"E{i}" });
            }

            for (int i = 0; i < 3; i++)
            {
                _container.Remove(_gameObjects[i]);
            }

            Assert.AreEqual(0, _container.Count);

            // 再追加
            for (int i = 0; i < 3; i++)
            {
                _gameObjects[i].transform.position = new Vector3(i * 5, 0, 0);
                _container.Add(_gameObjects[i], new TestEntityData { Name = $"Re{i}", Value = i + 100 });
            }

            Assert.AreEqual(3, _container.Count);
            _container.TryGetValue(_gameObjects[1], out var data);
            Assert.AreEqual(101, data.Value);
        }
    }

    #endregion

    #region SpatialHashContainer3D Tests

    [TestFixture]
    public class SpatialHashContainer3DTests
    {
        private SpatialHashContainer3D<TestEntityData> _container;
        private GameObject[] _gameObjects;

        [SetUp]
        public void SetUp()
        {
            _container = new SpatialHashContainer3D<TestEntityData>(10f, 64);
            _gameObjects = new GameObject[10];
            for (int i = 0; i < _gameObjects.Length; i++)
            {
                _gameObjects[i] = new GameObject($"Test3D_{i}");
            }
        }

        [TearDown]
        public void TearDown()
        {
            _container.Dispose();
            foreach (var go in _gameObjects)
            {
                if (go != null)
                    GameObject.DestroyImmediate(go);
            }
        }

        [Test]
        public void Add_SingleElement_CountIsOne()
        {
            _gameObjects[0].transform.position = new Vector3(5f, 5f, 5f);
            _container.Add(_gameObjects[0], new TestEntityData { Name = "A" });

            Assert.AreEqual(1, _container.Count);
        }

        [Test]
        public void Add_MultipleElements_CountMatches()
        {
            for (int i = 0; i < 5; i++)
            {
                _gameObjects[i].transform.position = new Vector3(i * 10f, i * 10f, i * 10f);
                _container.Add(_gameObjects[i], new TestEntityData { Name = $"E{i}" });
            }

            Assert.AreEqual(5, _container.Count);
        }

        [Test]
        public void ContainsKey_AfterAdd_ReturnsTrue()
        {
            _gameObjects[0].transform.position = Vector3.zero;
            _container.Add(_gameObjects[0], new TestEntityData());

            Assert.IsTrue(_container.ContainsKey(_gameObjects[0]));
        }

        [Test]
        public void TryGetValue_ExistingElement_ReturnsTrueAndData()
        {
            var data = new TestEntityData { Name = "Found3D", Value = 99 };
            _gameObjects[0].transform.position = Vector3.zero;
            _container.Add(_gameObjects[0], data);

            bool found = _container.TryGetValue(_gameObjects[0], out var result);

            Assert.IsTrue(found);
            Assert.AreEqual("Found3D", result.Name);
            Assert.AreEqual(99, result.Value);
        }

        [Test]
        public void Remove_ExistingElement_Works()
        {
            _gameObjects[0].transform.position = Vector3.zero;
            _container.Add(_gameObjects[0], new TestEntityData { Name = "A" });

            bool removed = _container.Remove(_gameObjects[0]);

            Assert.IsTrue(removed);
            Assert.AreEqual(0, _container.Count);
        }

        [Test]
        public void Remove_BackSwap_PreservesOtherElements()
        {
            for (int i = 0; i < 5; i++)
            {
                _gameObjects[i].transform.position = new Vector3(i * 20f, i * 5f, 0f);
                _container.Add(_gameObjects[i], new TestEntityData { Name = $"E{i}", Value = i });
            }

            _container.Remove(_gameObjects[1]);

            Assert.AreEqual(4, _container.Count);
            Assert.IsFalse(_container.ContainsKey(_gameObjects[1]));

            for (int i = 0; i < 5; i++)
            {
                if (i == 1) continue;
                Assert.IsTrue(_container.ContainsKey(_gameObjects[i]));
                _container.TryGetValue(_gameObjects[i], out var data);
                Assert.AreEqual(i, data.Value);
            }
        }

        [Test]
        public void QueryNeighbors_Uses3DDistance()
        {
            // XZ平面上では近いが、Y軸で離れている
            _gameObjects[0].transform.position = new Vector3(1f, 100f, 1f);
            _container.Add(_gameObjects[0], new TestEntityData { Name = "HighY" });
            _container.Update();

            var results = new TestEntityData[10];
            int count = _container.QueryNeighbors(Vector3.zero, 5f, results);

            // 3Dなので、Y軸の距離も考慮され、見つからないはず
            Assert.AreEqual(0, count);
        }

        [Test]
        public void QueryNeighbors_FindsElementsIn3DRadius()
        {
            _gameObjects[0].transform.position = new Vector3(1f, 1f, 1f);
            _gameObjects[1].transform.position = new Vector3(2f, 2f, 2f);
            _gameObjects[2].transform.position = new Vector3(100f, 100f, 100f);

            _container.Add(_gameObjects[0], new TestEntityData { Name = "Near0" });
            _container.Add(_gameObjects[1], new TestEntityData { Name = "Near1" });
            _container.Add(_gameObjects[2], new TestEntityData { Name = "Far" });
            _container.Update();

            var results = new TestEntityData[10];
            int count = _container.QueryNeighbors(Vector3.zero, 5f, results);

            Assert.AreEqual(2, count);
        }

        [Test]
        public void QueryNeighbors_WithDistances_Returns3DDistances()
        {
            _gameObjects[0].transform.position = new Vector3(1f, 2f, 2f);
            _container.Add(_gameObjects[0], new TestEntityData { Name = "A" });
            _container.Update();

            var results = new TestEntityData[10];
            var distances = new float[10];
            int count = _container.QueryNeighbors(Vector3.zero, 10f, results, distances);

            Assert.AreEqual(1, count);
            // 3D距離: sqrt(1^2 + 2^2 + 2^2) = 3
            Assert.AreEqual(3f, distances[0], 0.01f);
        }

        [Test]
        public void Update_RehashesMovedElements()
        {
            _gameObjects[0].transform.position = Vector3.zero;
            _container.Add(_gameObjects[0], new TestEntityData { Name = "Mover" });
            _container.Update();

            var results = new TestEntityData[10];
            int count1 = _container.QueryNeighbors(Vector3.zero, 5f, results);
            Assert.AreEqual(1, count1);

            _gameObjects[0].transform.position = new Vector3(200f, 200f, 200f);
            _container.Update();

            int count2 = _container.QueryNeighbors(Vector3.zero, 5f, results);
            Assert.AreEqual(0, count2);

            int count3 = _container.QueryNeighbors(new Vector3(200f, 200f, 200f), 5f, results);
            Assert.AreEqual(1, count3);
        }

        [Test]
        public void GetPosition_ReturnsCurrentTransformPosition()
        {
            var pos = new Vector3(15f, 30f, 25f);
            _gameObjects[0].transform.position = pos;
            _container.Add(_gameObjects[0], new TestEntityData());

            Vector3 result = _container.GetPosition(_gameObjects[0]);

            Assert.AreEqual(pos.x, result.x, 0.001f);
            Assert.AreEqual(pos.y, result.y, 0.001f);
            Assert.AreEqual(pos.z, result.z, 0.001f);
        }

        [Test]
        public void Clear_ResetsContainer()
        {
            for (int i = 0; i < 3; i++)
            {
                _gameObjects[i].transform.position = new Vector3(i, i, i);
                _container.Add(_gameObjects[i], new TestEntityData { Name = $"E{i}" });
            }

            _container.Clear();

            Assert.AreEqual(0, _container.Count);
        }

        [Test]
        public void ActiveDataSpan_ReturnsCorrectLength()
        {
            for (int i = 0; i < 4; i++)
            {
                _gameObjects[i].transform.position = new Vector3(i, i, i);
                _container.Add(_gameObjects[i], new TestEntityData { Name = $"E{i}" });
            }

            var span = _container.ActiveDataSpan;

            Assert.AreEqual(4, span.Length);
        }

        [Test]
        public void Add_ThenRemoveAll_ThenAddAgain_Works()
        {
            for (int i = 0; i < 3; i++)
            {
                _gameObjects[i].transform.position = new Vector3(i, i, i);
                _container.Add(_gameObjects[i], new TestEntityData { Name = $"E{i}" });
            }

            for (int i = 0; i < 3; i++)
            {
                _container.Remove(_gameObjects[i]);
            }

            Assert.AreEqual(0, _container.Count);

            for (int i = 0; i < 3; i++)
            {
                _gameObjects[i].transform.position = new Vector3(i * 5, i * 5, i * 5);
                _container.Add(_gameObjects[i], new TestEntityData { Name = $"Re{i}", Value = i + 200 });
            }

            Assert.AreEqual(3, _container.Count);
            _container.TryGetValue(_gameObjects[2], out var data);
            Assert.AreEqual(202, data.Value);
        }
    }

    #endregion

    #region StateMapContainer Tests

    [TestFixture]
    public class StateMapContainerTests
    {
        private StateMapContainer<TestState> _container;
        private GameObject[] _gameObjects;

        [SetUp]
        public void SetUp()
        {
            _container = new StateMapContainer<TestState>(64);
            _gameObjects = new GameObject[10];
            for (int i = 0; i < _gameObjects.Length; i++)
            {
                _gameObjects[i] = new GameObject($"TestState_{i}");
            }
        }

        [TearDown]
        public void TearDown()
        {
            _container.Dispose();
            foreach (var go in _gameObjects)
            {
                if (go != null)
                    GameObject.DestroyImmediate(go);
            }
        }

        [Test]
        public void Add_SingleElement_CountIsOne()
        {
            _container.Add(_gameObjects[0], TestState.Idle);

            Assert.AreEqual(1, _container.Count);
        }

        [Test]
        public void Add_MultipleElements_CountMatches()
        {
            for (int i = 0; i < 5; i++)
            {
                _container.Add(_gameObjects[i], TestState.Idle);
            }

            Assert.AreEqual(5, _container.Count);
        }

        [Test]
        public void ContainsKey_AfterAdd_ReturnsTrue()
        {
            _container.Add(_gameObjects[0], TestState.Idle);

            Assert.IsTrue(_container.ContainsKey(_gameObjects[0]));
        }

        [Test]
        public void ContainsKey_NotAdded_ReturnsFalse()
        {
            Assert.IsFalse(_container.ContainsKey(_gameObjects[0]));
        }

        [Test]
        public void TryGetState_ReturnsCurrentState()
        {
            _container.Add(_gameObjects[0], TestState.Walking);

            bool found = _container.TryGetState(_gameObjects[0], out var state);

            Assert.IsTrue(found);
            Assert.AreEqual(TestState.Walking, state);
        }

        [Test]
        public void TryGetState_NonExisting_ReturnsFalse()
        {
            bool found = _container.TryGetState(_gameObjects[0], out _);

            Assert.IsFalse(found);
        }

        [Test]
        public void GetState_ReturnsCurrentState()
        {
            _container.Add(_gameObjects[0], TestState.Running);

            var state = _container.GetState(_gameObjects[0]);

            Assert.AreEqual(TestState.Running, state);
        }

        [Test]
        public void GetState_NonExisting_Throws()
        {
            Assert.Throws<KeyNotFoundException>(() => _container.GetState(_gameObjects[0]));
        }

        [Test]
        public void SetState_ChangesState()
        {
            _container.Add(_gameObjects[0], TestState.Idle);

            bool result = _container.SetState(_gameObjects[0], TestState.Attacking);

            Assert.IsTrue(result);
            Assert.AreEqual(TestState.Attacking, _container.GetState(_gameObjects[0]));
        }

        [Test]
        public void SetState_TracksPreviousState()
        {
            _container.Add(_gameObjects[0], TestState.Idle);
            _container.SetState(_gameObjects[0], TestState.Walking);

            _container.TryGetState(_gameObjects[0], out var current, out var previous, out _);

            Assert.AreEqual(TestState.Walking, current);
            Assert.AreEqual(TestState.Idle, previous);
        }

        [Test]
        public void SetState_SameState_DoesNotResetElapsed()
        {
            _container.Add(_gameObjects[0], TestState.Idle);
            _container.Update(1.0f);

            _container.SetState(_gameObjects[0], TestState.Idle);
            _container.TryGetState(_gameObjects[0], out _, out _, out float elapsed);

            // 同じステートに遷移しても経過時間はリセットされない
            Assert.AreEqual(1.0f, elapsed, 0.001f);
        }

        [Test]
        public void SetState_DifferentState_ResetsElapsed()
        {
            _container.Add(_gameObjects[0], TestState.Idle);
            _container.Update(2.0f);

            _container.SetState(_gameObjects[0], TestState.Walking);
            _container.TryGetState(_gameObjects[0], out _, out _, out float elapsed);

            Assert.AreEqual(0f, elapsed, 0.001f);
        }

        [Test]
        public void SetState_NonExisting_ReturnsFalse()
        {
            bool result = _container.SetState(_gameObjects[0], TestState.Walking);

            Assert.IsFalse(result);
        }

        [Test]
        public void Update_IncrementsElapsedTime()
        {
            _container.Add(_gameObjects[0], TestState.Idle);

            _container.Update(0.5f);
            _container.Update(0.3f);

            _container.TryGetState(_gameObjects[0], out _, out _, out float elapsed);
            Assert.AreEqual(0.8f, elapsed, 0.001f);
        }

        [Test]
        public void Remove_ExistingElement_ReturnsTrueAndDecrementsCount()
        {
            _container.Add(_gameObjects[0], TestState.Idle);

            bool removed = _container.Remove(_gameObjects[0]);

            Assert.IsTrue(removed);
            Assert.AreEqual(0, _container.Count);
        }

        [Test]
        public void Remove_NonExisting_ReturnsFalse()
        {
            bool removed = _container.Remove(_gameObjects[0]);

            Assert.IsFalse(removed);
        }

        [Test]
        public void Remove_BackSwap_PreservesOtherElements()
        {
            for (int i = 0; i < 5; i++)
            {
                _container.Add(_gameObjects[i], (TestState)(i % 5));
            }

            _container.Remove(_gameObjects[1]);

            Assert.AreEqual(4, _container.Count);
            Assert.IsFalse(_container.ContainsKey(_gameObjects[1]));

            for (int i = 0; i < 5; i++)
            {
                if (i == 1) continue;
                Assert.IsTrue(_container.ContainsKey(_gameObjects[i]),
                    $"Element {i} should still exist");
                Assert.AreEqual((TestState)(i % 5), _container.GetState(_gameObjects[i]));
            }
        }

        [Test]
        public void GetAllInState_ReturnsMatchingObjects()
        {
            _container.Add(_gameObjects[0], TestState.Idle);
            _container.Add(_gameObjects[1], TestState.Walking);
            _container.Add(_gameObjects[2], TestState.Idle);
            _container.Add(_gameObjects[3], TestState.Attacking);
            _container.Add(_gameObjects[4], TestState.Idle);

            var results = new GameObject[10];
            int count = _container.GetAllInState(TestState.Idle, results);

            Assert.AreEqual(3, count);
        }

        [Test]
        public void GetAllInState_NoMatches_ReturnsZero()
        {
            _container.Add(_gameObjects[0], TestState.Idle);
            _container.Add(_gameObjects[1], TestState.Walking);

            var results = new GameObject[10];
            int count = _container.GetAllInState(TestState.Dead, results);

            Assert.AreEqual(0, count);
        }

        [Test]
        public void GetAllInState_AfterStateChange_ReflectsNewState()
        {
            _container.Add(_gameObjects[0], TestState.Idle);
            _container.Add(_gameObjects[1], TestState.Idle);

            _container.SetState(_gameObjects[0], TestState.Walking);

            var idleResults = new GameObject[10];
            int idleCount = _container.GetAllInState(TestState.Idle, idleResults);

            var walkingResults = new GameObject[10];
            int walkingCount = _container.GetAllInState(TestState.Walking, walkingResults);

            Assert.AreEqual(1, idleCount);
            Assert.AreEqual(1, walkingCount);
        }

        [Test]
        public void Clear_ResetsContainer()
        {
            for (int i = 0; i < 3; i++)
            {
                _container.Add(_gameObjects[i], TestState.Idle);
            }

            _container.Clear();

            Assert.AreEqual(0, _container.Count);
            for (int i = 0; i < 3; i++)
            {
                Assert.IsFalse(_container.ContainsKey(_gameObjects[i]));
            }
        }

        [Test]
        public void Add_ThenRemoveAll_ThenAddAgain_Works()
        {
            for (int i = 0; i < 3; i++)
            {
                _container.Add(_gameObjects[i], TestState.Idle);
            }

            for (int i = 0; i < 3; i++)
            {
                _container.Remove(_gameObjects[i]);
            }

            Assert.AreEqual(0, _container.Count);

            for (int i = 0; i < 3; i++)
            {
                _container.Add(_gameObjects[i], TestState.Running);
            }

            Assert.AreEqual(3, _container.Count);
            Assert.AreEqual(TestState.Running, _container.GetState(_gameObjects[0]));
        }

        [Test]
        public void MultipleStateTransitions_TracksCorrectly()
        {
            _container.Add(_gameObjects[0], TestState.Idle);

            _container.SetState(_gameObjects[0], TestState.Walking);
            _container.SetState(_gameObjects[0], TestState.Running);
            _container.SetState(_gameObjects[0], TestState.Attacking);

            _container.TryGetState(_gameObjects[0], out var current, out var previous, out _);

            Assert.AreEqual(TestState.Attacking, current);
            Assert.AreEqual(TestState.Running, previous);
        }
    }

    #endregion
}
