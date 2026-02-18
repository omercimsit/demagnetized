using UnityEngine;

// generic singleton base - inherit from this for any persistent manager
// usage: public class MyManager : Singleton<MyManager>
public abstract class Singleton<T> : MonoBehaviour where T : Singleton<T>
{
    public static T Instance { get; private set; }

    protected virtual void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = (T)this;

        // DontDestroyOnLoad only works on root objects
        if (transform.parent != null)
            transform.SetParent(null);

        DontDestroyOnLoad(gameObject);
        OnAwake();
    }

    // override this instead of Awake
    protected virtual void OnAwake() { }

    protected virtual void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
