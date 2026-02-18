using UnityEngine;
using System.Collections.Generic;

// generic pool so we stop destroying and re-creating clones/projectiles every frame
// learned this pattern from a Unity tutorial, works pretty well
// TODO: add some kind of peak usage tracking so we can tune initial sizes later
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

    public ObjectPool(T prefab, int initialSize = 10, int maxSize = 0, Transform parent = null, bool autoExpand = true)
    {
        _prefab = prefab;
        _maxSize = maxSize;
        _parent = parent;
        _autoExpand = autoExpand;
        _pool = new Queue<T>(initialSize);

        // pre-warm so there's no hitch on first spawn
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

    // gets an object, creating a new one if needed
    // FIXME: if autoExpand is false and pool is empty we just return null which can crash callers that don't check
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

    // convenience overload with position/rotation
    public T Get(Vector3 position, Quaternion rotation)
    {
        var obj = Get();
        if (obj != null)
        {
            obj.transform.SetPositionAndRotation(position, rotation);
        }
        return obj;
    }

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

    // destroy everything - usually called on scene unload
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

// scene-level manager so we can share pools between systems without passing references around
// TODO: this is kinda slow if you call GetOrCreatePool every frame, cache the result on your side
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

    public ObjectPool<T> GetPool<T>(string poolId) where T : Component
    {
        if (_pools.TryGetValue(poolId, out var pool) && pool is ObjectPool<T> typedPool)
        {
            return typedPool;
        }
        return null;
    }

    public T Spawn<T>(string poolId, Vector3 position, Quaternion rotation) where T : Component
    {
        var pool = GetPool<T>(poolId);
        return pool?.Get(position, rotation);
    }

    public void Despawn<T>(string poolId, T obj) where T : Component
    {
        var pool = GetPool<T>(poolId);
        pool?.ReturnToPool(obj);
    }

    protected override void OnDestroy()
    {
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
