using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    /// <summary>
    /// Unity.Vector3のシミュレーション
    /// </summary>
    public struct Vector3
    {
        public float x, y, z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);

        public float magnitude => MathF.Sqrt(x * x + y * y + z * z);

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);

        public override string ToString() => $"({x:F1}, {y:F1}, {z:F1})";
    }

    /// <summary>
    /// Unity.GameObjectのシミュレーション
    /// </summary>
    public class GameObject
    {
        private static int _nextId = 1;
        private readonly int _instanceId;
        private readonly List<MonoBehaviour> _components = new();

        public string name { get; set; }
        public Transform transform { get; }

        public GameObject(string name = "GameObject")
        {
            this.name = name;
            _instanceId = _nextId++;
            transform = new Transform(this);
        }

        public T AddComponent<T>() where T : MonoBehaviour, new()
        {
            var component = new T();
            component.gameObject = this;
            _components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : MonoBehaviour
        {
            foreach ( var component in _components )
            {
                if ( component is T result )
                    return result;
            }
            return null;
        }

        public override int GetHashCode() => _instanceId;
        public override string ToString() => $"GameObject({name})";
    }

    /// <summary>
    /// Unity.Transformのシミュレーション
    /// </summary>
    public class Transform
    {
        public GameObject gameObject { get; }
        public Vector3 position { get; set; }
        public Vector3 rotation { get; set; }
        public Vector3 localScale { get; set; } = Vector3.one;

        public Transform(GameObject gameObject)
        {
            this.gameObject = gameObject;
        }
    }

    /// <summary>
    /// Unity.MonoBehaviourのシミュレーション
    /// </summary>
    public abstract class MonoBehaviour
    {
        public GameObject gameObject { get; internal set; } = null!;
        public Transform transform => gameObject.transform;

        protected virtual void Awake() { }
        protected virtual void Start() { }
        protected virtual void Update() { }
        protected virtual void OnDestroy() { }
    }

    /// <summary>
    /// Unity.Mathfのシミュレーション
    /// </summary>
    public static class Mathf
    {
        public static float PI => MathF.PI;
        public static float Deg2Rad => PI / 180f;
        public static float Rad2Deg => 180f / PI;

        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Min(float a, float b) => MathF.Min(a, b);
        public static float Abs(float value) => MathF.Abs(value);
        public static float Sqrt(float value) => MathF.Sqrt(value);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Abs(int value) => Math.Abs(value);

        public static float Clamp(float value, float min, float max) => Max(min, Min(max, value));
        public static int Clamp(int value, int min, int max) => Max(min, Min(max, value));

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp(t, 0f, 1f);
    }
}

namespace Unity.Collections
{
    /// <summary>
    /// Unity.Collections.Allocatorのシミュレーション
    /// </summary>
    public enum Allocator
    {
        Temp,
        TempJob,
        Persistent
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    /// <summary>
    /// Unity.Collections.UnsafeListのシミュレーション
    /// </summary>
    public unsafe struct UnsafeList<T> : IDisposable where T : unmanaged
    {
        private T* _ptr;
        private int _length;
        private int _capacity;

        public int Length
        {
            get => _length;
            set => _length = value;
        }

        public int Capacity => _capacity;

        public UnsafeList(T* ptr, int capacity)
        {
            _ptr = ptr;
            _length = 0;
            _capacity = capacity;
        }

        public UnsafeList(int capacity, Allocator allocator)
        {
            _capacity = capacity;
            _length = 0;
            _ptr = (T*)UnsafeUtility.Malloc(capacity * sizeof(T), sizeof(T), allocator);
        }

        public T this[int index]
        {
            get => _ptr[index];
            set => _ptr[index] = value;
        }

        public ref T ElementAt(int index) => ref _ptr[index];

        public void AddNoResize(T item)
        {
            _ptr[_length] = item;
            _length++;
        }

        public void RemoveAtSwapBack(int index)
        {
            _ptr[index] = _ptr[_length - 1];
            _length--;
        }

        public void Clear() => _length = 0;

        public void Dispose()
        {
            if ( _ptr != null )
            {
                UnsafeUtility.Free(_ptr, Allocator.Persistent);
                _ptr = null;
            }
        }
    }

    /// <summary>
    /// Unity.Collections.LowLevel.Unsafe.UnsafeUtilityのシミュレーション
    /// </summary>
    public static unsafe class UnsafeUtility
    {
        public static void* Malloc(int size, int alignment, Allocator allocator)
        {
            return (void*)System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
        }

        public static void Free(void* memory, Allocator allocator)
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)memory);
        }

        public static void MemClear(void* destination, int size)
        {
            byte* dest = (byte*)destination;
            for ( int i = 0; i < size; i++ )
            {
                dest[i] = 0;
            }
        }
    }
}