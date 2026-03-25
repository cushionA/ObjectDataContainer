using System.Collections.Generic;
using NUnit.Framework;
using ODC.Runtime;
using UnityEngine;

namespace ODC.Tests
{
    [TestFixture]
    public class ComponentCacheTests
    {
        private List<GameObject> _createdObjects;
        private ComponentCache<BoxCollider> _cache;

        [SetUp]
        public void SetUp()
        {
            _createdObjects = new List<GameObject>();
            _cache = new ComponentCache<BoxCollider>(16);
        }

        [TearDown]
        public void TearDown()
        {
            _cache.Dispose();
            foreach (var go in _createdObjects)
            {
                if (go != null)
                    GameObject.DestroyImmediate(go);
            }
            _createdObjects.Clear();
        }

        private GameObject CreateGameObjectWithCollider(string name = "TestObj")
        {
            var go = new GameObject(name);
            go.AddComponent<BoxCollider>();
            _createdObjects.Add(go);
            return go;
        }

        private GameObject CreateGameObjectWithoutCollider(string name = "NoCollider")
        {
            var go = new GameObject(name);
            _createdObjects.Add(go);
            return go;
        }

        // ================================================================
        // Constructor
        // ================================================================

        [Test]
        public void Constructor_InitializesWithZeroCount()
        {
            Assert.AreEqual(0, _cache.Count);
        }

        // ================================================================
        // GetOrCache
        // ================================================================

        [Test]
        public void GetOrCache_WithNull_ReturnsNull()
        {
            var result = _cache.GetOrCache(null);
            Assert.IsNull(result);
        }

        [Test]
        public void GetOrCache_WithValidComponent_ReturnsCachedComponent()
        {
            var go = CreateGameObjectWithCollider();
            var expected = go.GetComponent<BoxCollider>();

            var result = _cache.GetOrCache(go);

            Assert.AreSame(expected, result);
            Assert.AreEqual(1, _cache.Count);
        }

        [Test]
        public void GetOrCache_CalledTwice_ReturnsSameComponent()
        {
            var go = CreateGameObjectWithCollider();

            var first = _cache.GetOrCache(go);
            var second = _cache.GetOrCache(go);

            Assert.AreSame(first, second);
            Assert.AreEqual(1, _cache.Count);
        }

        [Test]
        public void GetOrCache_WithoutComponent_ReturnsNull_AndDoesNotCache()
        {
            var go = CreateGameObjectWithoutCollider();

            var result = _cache.GetOrCache(go);

            Assert.IsNull(result);
            Assert.AreEqual(0, _cache.Count);
        }

        [Test]
        public void GetOrCache_WhenAtCapacity_ReturnsComponentButDoesNotAdd()
        {
            var smallCache = new ComponentCache<BoxCollider>(2);

            var go1 = CreateGameObjectWithCollider("Obj1");
            var go2 = CreateGameObjectWithCollider("Obj2");
            var go3 = CreateGameObjectWithCollider("Obj3");

            smallCache.GetOrCache(go1);
            smallCache.GetOrCache(go2);

            // Cache is full, should still return the component but not cache it
            var result = smallCache.GetOrCache(go3);
            Assert.IsNotNull(result);
            Assert.AreEqual(2, smallCache.Count);
            Assert.IsFalse(smallCache.ContainsKey(go3));

            smallCache.Dispose();
        }

        // ================================================================
        // GetCached
        // ================================================================

        [Test]
        public void GetCached_WhenNotCached_ReturnsNull()
        {
            var go = CreateGameObjectWithCollider();

            var result = _cache.GetCached(go);

            Assert.IsNull(result);
            Assert.AreEqual(0, _cache.Count);
        }

        [Test]
        public void GetCached_WhenCached_ReturnsComponent()
        {
            var go = CreateGameObjectWithCollider();
            _cache.GetOrCache(go);

            var result = _cache.GetCached(go);

            Assert.IsNotNull(result);
            Assert.AreSame(go.GetComponent<BoxCollider>(), result);
        }

        [Test]
        public void GetCached_WithNull_ReturnsNull()
        {
            var result = _cache.GetCached(null);
            Assert.IsNull(result);
        }

        // ================================================================
        // Add
        // ================================================================

        [Test]
        public void Add_ManuallyAddsToCache()
        {
            var go = CreateGameObjectWithCollider();
            var collider = go.GetComponent<BoxCollider>();

            _cache.Add(go, collider);

            Assert.AreEqual(1, _cache.Count);
            Assert.IsTrue(_cache.ContainsKey(go));
            Assert.AreSame(collider, _cache.GetCached(go));
        }

        [Test]
        public void Add_MultipleObjects_AllRetrievable()
        {
            var go1 = CreateGameObjectWithCollider("Obj1");
            var go2 = CreateGameObjectWithCollider("Obj2");
            var go3 = CreateGameObjectWithCollider("Obj3");

            _cache.Add(go1, go1.GetComponent<BoxCollider>());
            _cache.Add(go2, go2.GetComponent<BoxCollider>());
            _cache.Add(go3, go3.GetComponent<BoxCollider>());

            Assert.AreEqual(3, _cache.Count);
            Assert.IsTrue(_cache.ContainsKey(go1));
            Assert.IsTrue(_cache.ContainsKey(go2));
            Assert.IsTrue(_cache.ContainsKey(go3));
        }

        // ================================================================
        // Remove
        // ================================================================

        [Test]
        public void Remove_ExistingObject_ReturnsTrueAndDecrementsCount()
        {
            var go = CreateGameObjectWithCollider();
            _cache.GetOrCache(go);

            bool removed = _cache.Remove(go);

            Assert.IsTrue(removed);
            Assert.AreEqual(0, _cache.Count);
            Assert.IsFalse(_cache.ContainsKey(go));
        }

        [Test]
        public void Remove_NonExistingObject_ReturnsFalse()
        {
            var go = CreateGameObjectWithCollider();

            bool removed = _cache.Remove(go);

            Assert.IsFalse(removed);
        }

        [Test]
        public void Remove_BackSwap_PreservesOtherEntries()
        {
            var go1 = CreateGameObjectWithCollider("Obj1");
            var go2 = CreateGameObjectWithCollider("Obj2");
            var go3 = CreateGameObjectWithCollider("Obj3");

            _cache.GetOrCache(go1);
            _cache.GetOrCache(go2);
            _cache.GetOrCache(go3);

            // Remove the first one - triggers back-swap with last element
            _cache.Remove(go1);

            Assert.AreEqual(2, _cache.Count);
            Assert.IsFalse(_cache.ContainsKey(go1));
            Assert.IsTrue(_cache.ContainsKey(go2));
            Assert.IsTrue(_cache.ContainsKey(go3));

            // Verify lookups still work after swap
            Assert.AreSame(go2.GetComponent<BoxCollider>(), _cache.GetCached(go2));
            Assert.AreSame(go3.GetComponent<BoxCollider>(), _cache.GetCached(go3));
        }

        [Test]
        public void Remove_MiddleElement_BackSwap_PreservesIntegrity()
        {
            var objects = new GameObject[5];
            for (int i = 0; i < 5; i++)
            {
                objects[i] = CreateGameObjectWithCollider($"Obj{i}");
                _cache.GetOrCache(objects[i]);
            }

            // Remove middle element
            _cache.Remove(objects[2]);

            Assert.AreEqual(4, _cache.Count);
            Assert.IsFalse(_cache.ContainsKey(objects[2]));

            // All other elements should still be accessible
            for (int i = 0; i < 5; i++)
            {
                if (i == 2) continue;
                Assert.IsTrue(_cache.ContainsKey(objects[i]),
                    $"Object {i} should still be in cache");
                Assert.AreSame(objects[i].GetComponent<BoxCollider>(),
                    _cache.GetCached(objects[i]),
                    $"Object {i} component should match");
            }
        }

        [Test]
        public void Remove_ThenAdd_WorksCorrectly()
        {
            var go1 = CreateGameObjectWithCollider("Obj1");
            var go2 = CreateGameObjectWithCollider("Obj2");

            _cache.GetOrCache(go1);
            _cache.Remove(go1);
            _cache.GetOrCache(go2);

            Assert.AreEqual(1, _cache.Count);
            Assert.IsFalse(_cache.ContainsKey(go1));
            Assert.IsTrue(_cache.ContainsKey(go2));
        }

        // ================================================================
        // ContainsKey
        // ================================================================

        [Test]
        public void ContainsKey_WhenNotAdded_ReturnsFalse()
        {
            var go = CreateGameObjectWithCollider();
            Assert.IsFalse(_cache.ContainsKey(go));
        }

        [Test]
        public void ContainsKey_WhenAdded_ReturnsTrue()
        {
            var go = CreateGameObjectWithCollider();
            _cache.GetOrCache(go);
            Assert.IsTrue(_cache.ContainsKey(go));
        }

        [Test]
        public void ContainsKey_WithNull_ReturnsFalse()
        {
            Assert.IsFalse(_cache.ContainsKey(null));
        }

        // ================================================================
        // CleanupDestroyed
        // ================================================================

        [Test]
        public void CleanupDestroyed_NoDestroyedObjects_ReturnsZero()
        {
            var go = CreateGameObjectWithCollider();
            _cache.GetOrCache(go);

            int removed = _cache.CleanupDestroyed();

            Assert.AreEqual(0, removed);
            Assert.AreEqual(1, _cache.Count);
        }

        [Test]
        public void CleanupDestroyed_RemovesDestroyedObjects()
        {
            var go1 = CreateGameObjectWithCollider("Obj1");
            var go2 = CreateGameObjectWithCollider("Obj2");
            var go3 = CreateGameObjectWithCollider("Obj3");

            _cache.GetOrCache(go1);
            _cache.GetOrCache(go2);
            _cache.GetOrCache(go3);

            // Destroy one object
            GameObject.DestroyImmediate(go2);
            _createdObjects.Remove(go2);

            int removed = _cache.CleanupDestroyed();

            Assert.AreEqual(1, removed);
            Assert.AreEqual(2, _cache.Count);
            Assert.IsTrue(_cache.ContainsKey(go1));
            Assert.IsFalse(_cache.ContainsKey(go2));
            Assert.IsTrue(_cache.ContainsKey(go3));
        }

        [Test]
        public void CleanupDestroyed_MultipleDestroyed_RemovesAll()
        {
            var objects = new GameObject[5];
            for (int i = 0; i < 5; i++)
            {
                objects[i] = CreateGameObjectWithCollider($"Obj{i}");
                _cache.GetOrCache(objects[i]);
            }

            // Destroy objects at indices 1 and 3
            GameObject.DestroyImmediate(objects[1]);
            GameObject.DestroyImmediate(objects[3]);
            _createdObjects.Remove(objects[1]);
            _createdObjects.Remove(objects[3]);

            int removed = _cache.CleanupDestroyed();

            Assert.AreEqual(2, removed);
            Assert.AreEqual(3, _cache.Count);
            Assert.IsTrue(_cache.ContainsKey(objects[0]));
            Assert.IsTrue(_cache.ContainsKey(objects[2]));
            Assert.IsTrue(_cache.ContainsKey(objects[4]));
        }

        [Test]
        public void CleanupDestroyed_AfterCleanup_RemainingEntriesStillAccessible()
        {
            var go1 = CreateGameObjectWithCollider("Obj1");
            var go2 = CreateGameObjectWithCollider("Obj2");
            var go3 = CreateGameObjectWithCollider("Obj3");

            _cache.GetOrCache(go1);
            _cache.GetOrCache(go2);
            _cache.GetOrCache(go3);

            GameObject.DestroyImmediate(go2);
            _createdObjects.Remove(go2);

            _cache.CleanupDestroyed();

            // Remaining entries should still work
            Assert.AreSame(go1.GetComponent<BoxCollider>(), _cache.GetCached(go1));
            Assert.AreSame(go3.GetComponent<BoxCollider>(), _cache.GetCached(go3));
        }

        // ================================================================
        // Clear
        // ================================================================

        [Test]
        public void Clear_RemovesAllEntries()
        {
            var go1 = CreateGameObjectWithCollider("Obj1");
            var go2 = CreateGameObjectWithCollider("Obj2");

            _cache.GetOrCache(go1);
            _cache.GetOrCache(go2);

            _cache.Clear();

            Assert.AreEqual(0, _cache.Count);
            Assert.IsFalse(_cache.ContainsKey(go1));
            Assert.IsFalse(_cache.ContainsKey(go2));
        }

        [Test]
        public void Clear_ThenAddAgain_WorksCorrectly()
        {
            var go1 = CreateGameObjectWithCollider("Obj1");

            _cache.GetOrCache(go1);
            _cache.Clear();

            _cache.GetOrCache(go1);

            Assert.AreEqual(1, _cache.Count);
            Assert.IsTrue(_cache.ContainsKey(go1));
        }

        // ================================================================
        // Dispose
        // ================================================================

        [Test]
        public void Dispose_ClearsAllData()
        {
            var go = CreateGameObjectWithCollider();
            _cache.GetOrCache(go);

            _cache.Dispose();
            // Re-create for TearDown safety
            _cache = new ComponentCache<BoxCollider>(16);

            Assert.AreEqual(0, _cache.Count);
        }

        // ================================================================
        // Hash collision / stress tests
        // ================================================================

        [Test]
        public void ManyObjects_AllRetrievableAfterAdding()
        {
            var largeCache = new ComponentCache<BoxCollider>(64);
            var objects = new GameObject[50];

            for (int i = 0; i < 50; i++)
            {
                objects[i] = CreateGameObjectWithCollider($"Obj{i}");
                largeCache.GetOrCache(objects[i]);
            }

            Assert.AreEqual(50, largeCache.Count);

            for (int i = 0; i < 50; i++)
            {
                Assert.IsTrue(largeCache.ContainsKey(objects[i]),
                    $"Object {i} should be in cache");
                Assert.AreSame(objects[i].GetComponent<BoxCollider>(),
                    largeCache.GetCached(objects[i]),
                    $"Object {i} component should match");
            }

            largeCache.Dispose();
        }

        [Test]
        public void ManyObjects_RemoveHalf_RemainingStillAccessible()
        {
            var largeCache = new ComponentCache<BoxCollider>(64);
            var objects = new GameObject[20];

            for (int i = 0; i < 20; i++)
            {
                objects[i] = CreateGameObjectWithCollider($"Obj{i}");
                largeCache.GetOrCache(objects[i]);
            }

            // Remove even-indexed objects
            for (int i = 0; i < 20; i += 2)
            {
                largeCache.Remove(objects[i]);
            }

            Assert.AreEqual(10, largeCache.Count);

            // Odd-indexed objects should still be accessible
            for (int i = 1; i < 20; i += 2)
            {
                Assert.IsTrue(largeCache.ContainsKey(objects[i]),
                    $"Object {i} should still be in cache");
                Assert.AreSame(objects[i].GetComponent<BoxCollider>(),
                    largeCache.GetCached(objects[i]),
                    $"Object {i} component should match");
            }

            // Even-indexed objects should be gone
            for (int i = 0; i < 20; i += 2)
            {
                Assert.IsFalse(largeCache.ContainsKey(objects[i]),
                    $"Object {i} should not be in cache");
            }

            largeCache.Dispose();
        }

        [Test]
        public void AddRemoveAdd_CycleIntegrity()
        {
            var go1 = CreateGameObjectWithCollider("Obj1");
            var go2 = CreateGameObjectWithCollider("Obj2");
            var go3 = CreateGameObjectWithCollider("Obj3");

            // Add all
            _cache.GetOrCache(go1);
            _cache.GetOrCache(go2);
            _cache.GetOrCache(go3);
            Assert.AreEqual(3, _cache.Count);

            // Remove all
            _cache.Remove(go1);
            _cache.Remove(go2);
            _cache.Remove(go3);
            Assert.AreEqual(0, _cache.Count);

            // Re-add all
            _cache.GetOrCache(go1);
            _cache.GetOrCache(go2);
            _cache.GetOrCache(go3);
            Assert.AreEqual(3, _cache.Count);

            // Verify all accessible
            Assert.AreSame(go1.GetComponent<BoxCollider>(), _cache.GetCached(go1));
            Assert.AreSame(go2.GetComponent<BoxCollider>(), _cache.GetCached(go2));
            Assert.AreSame(go3.GetComponent<BoxCollider>(), _cache.GetCached(go3));
        }
    }
}
