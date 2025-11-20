using UnityEngine;

/// <summary>
/// Generic singleton base for persistent MonoBehaviour managers.
/// Handles Instance assignment, duplicate destruction, parent detach, and DontDestroyOnLoad.
///
/// Usage:
///   public class MyManager : Singleton&lt;MyManager&gt;
///   {
///       protected override void OnAwake() { /* your init code */ }
///   }
/// </summary>
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

    /// <summary>Override this instead of Awake for initialization logic.</summary>
    protected virtual void OnAwake() { }

    protected virtual void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
