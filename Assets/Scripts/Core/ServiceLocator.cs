using UnityEngine;
using CloneSystem;
using System;
using System.Collections.Generic;

/// <summary>
/// Central service entrypoint used by gameplay and UI systems.
/// New code should resolve dependencies from here instead of ad-hoc scene searches.
/// </summary>
public class ServiceLocator : MonoBehaviour
{
    private static ServiceLocator _instance;

    public static ServiceLocator Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<ServiceLocator>();
                if (_instance == null)
                {
                    var go = new GameObject("ServiceLocator");
                    _instance = go.AddComponent<ServiceLocator>();
                }
            }
            return _instance;
        }
    }

    [Header("Clone System")]
    [SerializeField] private AAACloneSystem aaaCloneSystem;

    [Header("Player")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private CharacterController playerController;
    [SerializeField] private Camera mainCamera;

    [Header("Managers")]
    [SerializeField] private GraphicsQualityManager graphicsQualityManager;
    [SerializeField] private WorkingDLSSManager workingDLSSManager;
    [SerializeField] private LocalizationManager localizationManager;
    [SerializeField] private AudioManager audioManager;

    private bool _aaaCloneSystemSearched;
    private bool _playerControllerSearched;
    private bool _mainCameraSearched;
    private bool _graphicsQualitySearched;
    private bool _dlssSearched;
    private bool _localizationSearched;
    private bool _audioManagerSearched;

    private readonly Dictionary<Type, Component> _serviceCache = new Dictionary<Type, Component>();

    public AAACloneSystem CloneSystem
    {
        get
        {
            if (aaaCloneSystem == null && !_aaaCloneSystemSearched)
            {
                aaaCloneSystem = FindFirstObjectByType<AAACloneSystem>();
                _aaaCloneSystemSearched = true;
            }
            return aaaCloneSystem;
        }
    }

    public Transform PlayerTransform
    {
        get
        {
            if (playerTransform == null && playerController != null)
                playerTransform = playerController.transform;
            return playerTransform;
        }
    }

    public CharacterController PlayerController
    {
        get
        {
            if (playerController == null && !_playerControllerSearched)
            {
                playerController = FindFirstObjectByType<CharacterController>();
                _playerControllerSearched = true;
            }
            return playerController;
        }
    }

    public Camera MainCamera
    {
        get
        {
            if (mainCamera == null && !_mainCameraSearched)
            {
                mainCamera = Camera.main;
                if (mainCamera == null)
                    mainCamera = FindFirstObjectByType<Camera>();
                _mainCameraSearched = true;
            }
            return mainCamera;
        }
    }

    public GraphicsQualityManager GraphicsQuality
    {
        get
        {
            if (graphicsQualityManager == null && !_graphicsQualitySearched)
            {
                graphicsQualityManager = FindFirstObjectByType<GraphicsQualityManager>();
                _graphicsQualitySearched = true;
            }
            return graphicsQualityManager;
        }
    }

    public WorkingDLSSManager DLSS
    {
        get
        {
            if (workingDLSSManager == null && !_dlssSearched)
            {
                workingDLSSManager = FindFirstObjectByType<WorkingDLSSManager>();
                _dlssSearched = true;
            }
            return workingDLSSManager;
        }
    }

    public LocalizationManager Localization
    {
        get
        {
            if (localizationManager == null && !_localizationSearched)
            {
                localizationManager = FindFirstObjectByType<LocalizationManager>();
                _localizationSearched = true;
            }
            return localizationManager;
        }
    }

    public AudioManager Audio
    {
        get
        {
            if (audioManager == null && !_audioManagerSearched)
            {
                audioManager = FindFirstObjectByType<AudioManager>();
                _audioManagerSearched = true;
            }
            return audioManager;
        }
    }

    public T Resolve<T>() where T : Component
    {
        if (_serviceCache.TryGetValue(typeof(T), out var cached) && cached != null)
            return cached as T;

        var found = FindFirstObjectByType<T>();
        if (found != null)
            _serviceCache[typeof(T)] = found;
        return found;
    }

    public bool TryResolve<T>(out T result) where T : Component
    {
        result = Resolve<T>();
        return result != null;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        if (transform.parent != null)
            transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        // Ensure foundational services before first lookup pass.
        GameBootstrap.EnsureCoreServices();
        AutoFindReferences();
    }

    private void AutoFindReferences()
    {
        if (aaaCloneSystem == null)
            aaaCloneSystem = FindFirstObjectByType<AAACloneSystem>();

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
                mainCamera = FindFirstObjectByType<Camera>();
        }

        if (playerController == null)
            playerController = FindFirstObjectByType<CharacterController>();
        if (playerTransform == null && playerController != null)
            playerTransform = playerController.transform;

        if (graphicsQualityManager == null)
            graphicsQualityManager = FindFirstObjectByType<GraphicsQualityManager>();
        if (workingDLSSManager == null)
            workingDLSSManager = FindFirstObjectByType<WorkingDLSSManager>();
        if (localizationManager == null)
            localizationManager = FindFirstObjectByType<LocalizationManager>();
        if (audioManager == null)
            audioManager = FindFirstObjectByType<AudioManager>();

        LogMissingReferences();
    }

    private void LogMissingReferences()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (aaaCloneSystem == null) Debug.Log("[ServiceLocator] AAACloneSystem not found in scene.");
        if (mainCamera == null) Debug.LogWarning("[ServiceLocator] MainCamera not found!");
#endif
    }

    public void RefreshReferences()
    {
        _aaaCloneSystemSearched = false;
        _playerControllerSearched = false;
        _mainCameraSearched = false;
        _graphicsQualitySearched = false;
        _dlssSearched = false;
        _localizationSearched = false;
        _audioManagerSearched = false;
        _serviceCache.Clear();

        AutoFindReferences();
        Debug.Log("[ServiceLocator] References refreshed");
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }
}
