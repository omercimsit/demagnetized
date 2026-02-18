using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// basic tests for the object pool - wanted to make sure the core stuff doesn't break
// when I change things later
[TestFixture]
public class ObjectPoolTests
{
    private GameObject _prefabGo;
    private Transform _prefab;

    [SetUp]
    public void SetUp()
    {
        _prefabGo = new GameObject("PoolTestPrefab");
        _prefab = _prefabGo.transform;
    }

    [TearDown]
    public void TearDown()
    {
        if (_prefabGo != null)
            Object.DestroyImmediate(_prefabGo);
    }

    [Test]
    public void Pool_PrewarmsCorrectAmount()
    {
        var pool = new ObjectPool<Transform>(_prefab, initialSize: 5);

        Assert.AreEqual(5, pool.PooledCount);
        Assert.AreEqual(0, pool.ActiveCount);
    }

    [Test]
    public void Get_ReturnsActiveObject()
    {
        var pool = new ObjectPool<Transform>(_prefab, initialSize: 3);

        var obj = pool.Get();

        Assert.IsNotNull(obj);
        Assert.IsTrue(obj.gameObject.activeSelf);
        Assert.AreEqual(1, pool.ActiveCount);
        Assert.AreEqual(2, pool.PooledCount);

        Object.DestroyImmediate(obj.gameObject);
    }

    [Test]
    public void ReturnToPool_DeactivatesObject()
    {
        var pool = new ObjectPool<Transform>(_prefab, initialSize: 1);

        var obj = pool.Get();
        pool.ReturnToPool(obj);

        Assert.IsFalse(obj.gameObject.activeSelf);
        Assert.AreEqual(0, pool.ActiveCount);
        Assert.AreEqual(1, pool.PooledCount);

        Object.DestroyImmediate(obj.gameObject);
    }

    [Test]
    public void Get_WithPositionRotation_SetsTransform()
    {
        var pool = new ObjectPool<Transform>(_prefab, initialSize: 1);
        var pos = new Vector3(1f, 2f, 3f);
        var rot = Quaternion.Euler(0f, 90f, 0f);

        var obj = pool.Get(pos, rot);

        Assert.AreEqual(pos, obj.position);
        // quaternion comparison needs a tolerance
        Assert.IsTrue(Quaternion.Angle(rot, obj.rotation) < 0.01f);

        Object.DestroyImmediate(obj.gameObject);
    }

    [Test]
    public void Pool_ExpandsWhenEmpty()
    {
        var pool = new ObjectPool<Transform>(_prefab, initialSize: 1);

        var first = pool.Get();
        var second = pool.Get(); // pool was empty, should auto-expand

        Assert.IsNotNull(second);
        Assert.AreEqual(2, pool.ActiveCount);

        Object.DestroyImmediate(first.gameObject);
        Object.DestroyImmediate(second.gameObject);
    }

    [Test]
    public void Pool_RespectsMaxSize()
    {
        // maxSize=2, autoExpand=true but capped
        var pool = new ObjectPool<Transform>(_prefab, initialSize: 2, maxSize: 2);

        var first = pool.Get();
        var second = pool.Get();
        var third = pool.Get(); // should be null, we're at max

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.IsNull(third);

        Object.DestroyImmediate(first.gameObject);
        Object.DestroyImmediate(second.gameObject);
    }

    [Test]
    public void Clear_DestroysAllPooledObjects()
    {
        var pool = new ObjectPool<Transform>(_prefab, initialSize: 3);

        pool.Clear();

        Assert.AreEqual(0, pool.PooledCount);
        Assert.AreEqual(0, pool.ActiveCount);
    }

    [Test]
    public void ReturnToPool_NullDoesNotThrow()
    {
        var pool = new ObjectPool<Transform>(_prefab, initialSize: 1);

        // shouldn't crash
        Assert.DoesNotThrow(() => pool.ReturnToPool(null));
    }
}
