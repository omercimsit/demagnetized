using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ensures critical managers exist and are reused across scene transitions.
/// This replaces ad-hoc manager creation in feature scripts.
/// </summary>
[DefaultExecutionOrder(-10000)]
public class GameBootstrap : MonoBehaviour
{
    private static bool _initialized;

    [Header("Bootstrap")]
    [SerializeField] private bool bootstrapOnAwake = true;

    private void Awake()
    {
        if (bootstrapOnAwake)
            EnsureCoreServices();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RuntimeBootstrap()
    {
        if (_initialized)
            return;

        var go = new GameObject("BootstrapRoot");
        DontDestroyOnLoad(go);
        go.AddComponent<GameBootstrap>();

        // Make sure core services exist before scene behaviours query them.
        EnsureCoreServices();

        _initialized = true;

        // Keep references fresh when scene changes.
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private static void OnActiveSceneChanged(Scene _, Scene __)
    {
        if (ServiceLocator.Instance != null)
            ServiceLocator.Instance.RefreshReferences();
    }

    public static void EnsureCoreServices()
    {
        EnsureSingleton<ServiceLocator>("ServiceLocator");
        EnsureSingleton<GameSettings>("GameSettings");
        EnsureSingleton<GraphicsQualityManager>("GraphicsQualityManager");
        EnsureSingleton<LocalizationManager>("LocalizationManager");
    }

    private static T EnsureSingleton<T>(string objectName) where T : Component
    {
        var existing = Object.FindFirstObjectByType<T>();
        if (existing != null)
            return existing;

        var go = new GameObject(objectName);
        Object.DontDestroyOnLoad(go);
        return go.AddComponent<T>();
    }
}
