using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Generic Object Pool for frequently spawned objects.
/// Reduces garbage collection by reusing objects instead of creating/destroying them.
/// Use for: Clones, Projectiles, Particles, UI elements, etc.
/// </summary>
public class ObjectPool<T> where T : Component
{
    private readonly Queue<T> _pool;
    private readonly T _prefab;
    private readonly Transform _parent;
    private readonly int _maxSize;
    private readonly bool _autoExpand;

    public int ActiveCount { get; private set; }
    public int PooledCount => _pool.Count;
    public int TotalCount => ActiveCount + PooledCount;

    /// <summary>
    /// Creates a new object pool.
    /// </summary>
    /// <param name="prefab">Prefab to instantiate</param>
    /// <param name="initialSize">Initial objects to pre-warm</param>
    /// <param name="maxSize">Maximum pool size (0 = unlimited)</param>
    /// <param name="parent">Parent transform for pooled objects</param>
    /// <param name="autoExpand">Auto-expand pool when empty</param>
    public ObjectPool(T prefab, int initialSize = 10, int maxSize = 0, Transform parent = null, bool autoExpand = true)
    {
        _prefab = prefab;
        _maxSize = maxSize;
        _parent = parent;
        _autoExpand = autoExpand;
        _pool = new Queue<T>(initialSize);

        // Pre-warm the pool
        for (int i = 0; i < initialSize; i++)
        {
            var obj = CreateNewObject();
            ReturnToPool(obj);
        }

        Debug.Log($"[ObjectPool<{typeof(T).Name}>] Initialized with {initialSize} objects");
    }

    private T CreateNewObject()
    {
        var obj = Object.Instantiate(_prefab, _parent);
        obj.gameObject.SetActive(false);
        return obj;
    }

    /// <summary>
    /// Gets an object from the pool. Creates new if pool is empty and autoExpand is true.
    /// </summary>
    public T Get()
    {
        T obj;

        if (_pool.Count > 0)
        {
            obj = _pool.Dequeue();
        }
        else if (_autoExpand && (_maxSize == 0 || TotalCount < _maxSize))
        {
            obj = CreateNewObject();
        }
        else
        {
            Debug.LogWarning($"[ObjectPool<{typeof(T).Name}>] Pool exhausted! MaxSize: {_maxSize}");
            return null;
        }

        obj.gameObject.SetActive(true);
        ActiveCount++;
        return obj;
    }

    /// <summary>
    /// Gets an object from the pool and positions it.
    /// </summary>
    public T Get(Vector3 position, Quaternion rotation)
    {
        var obj = Get();
        if (obj != null)
        {
            obj.transform.SetPositionAndRotation(position, rotation);
        }
        return obj;
    }

    /// <summary>
    /// Returns an object to the pool.
    /// </summary>
    public void ReturnToPool(T obj)
    {
        if (obj == null) return;

        obj.gameObject.SetActive(false);
        
        if (_parent != null)
        {
            obj.transform.SetParent(_parent);
        }

        _pool.Enqueue(obj);
        ActiveCount = Mathf.Max(0, ActiveCount - 1);
    }

    /// <summary>
    /// Clears the pool and destroys all objects.
    /// </summary>
    public void Clear()
    {
        while (_pool.Count > 0)
        {
            var obj = _pool.Dequeue();
            if (obj != null)
            {
                Object.Destroy(obj.gameObject);
            }
        }
        ActiveCount = 0;
    }
}

/// <summary>
/// MonoBehaviour-based pool manager that can be added to a scene.
/// </summary>
public class PoolManager : Singleton<PoolManager>
{
    [Header("Pool Settings")]
    [SerializeField] private bool initializeOnAwake = true;

    private Dictionary<string, object> _pools = new Dictionary<string, object>();

    protected override void OnAwake()
    {
        if (initializeOnAwake)
        {
            Debug.Log("[PoolManager] Initialized on Awake");
        }
    }

    /// <summary>
    /// Creates or gets a pool for the specified prefab.
    /// </summary>
    public ObjectPool<T> GetOrCreatePool<T>(T prefab, string poolId = null, int initialSize = 10) where T : Component
    {
        string id = poolId ?? prefab.name;
        
        if (_pools.TryGetValue(id, out var existingPool) && existingPool is ObjectPool<T> typedPool)
        {
            return typedPool;
        }

        var newPool = new ObjectPool<T>(prefab, initialSize, 0, transform, true);
        _pools[id] = newPool;
        return newPool;
    }

    /// <summary>
    /// Gets a pool by ID.
    /// </summary>
    public ObjectPool<T> GetPool<T>(string poolId) where T : Component
    {
        if (_pools.TryGetValue(poolId, out var pool) && pool is ObjectPool<T> typedPool)
        {
            return typedPool;
        }
        return null;
    }

    /// <summary>
    /// Quick spawn from a registered pool.
    /// </summary>
    public T Spawn<T>(string poolId, Vector3 position, Quaternion rotation) where T : Component
    {
        var pool = GetPool<T>(poolId);
        return pool?.Get(position, rotation);
    }

    /// <summary>
    /// Return object to its pool.
    /// </summary>
    public void Despawn<T>(string poolId, T obj) where T : Component
    {
        var pool = GetPool<T>(poolId);
        pool?.ReturnToPool(obj);
    }

    protected override void OnDestroy()
    {
        // Clear all pools
        foreach (var pool in _pools.Values)
        {
            if (pool is System.IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        _pools.Clear();
        base.OnDestroy();
    }
}
