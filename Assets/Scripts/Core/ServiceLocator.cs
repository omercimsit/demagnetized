using UnityEngine;
using CloneSystem;
using System;
using System.Collections.Generic;

// global access point for all the main systems, instead of doing FindObjectOfType everywhere
// TODO: might want to split this into multiple locators if the project gets bigger
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

    // these flags stop us from doing FindFirstObjectByType every frame, which is slow
    private bool _aaaCloneSystemSearched;
    private bool _playerControllerSearched;
    private bool _mainCameraSearched;
    private bool _graphicsQualitySearched;
    private bool _dlssSearched;
    private bool _localizationSearched;
    private bool _audioManagerSearched;

    // FIXME: this cache doesn't get invalidated when objects are destroyed mid-scene
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
                // Camera.main can return null if the camera isn't tagged properly
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

    // generic fallback for anything not explicitly wired up above
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

        // make sure core stuff exists before we start looking for things
        GameBootstrap.EnsureCoreServices();
        AutoFindReferences();
    }

    // not sure if doing all this in Awake is ideal but it seems to work fine
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

    // call this after scene loads if references went stale
    // TODO: hook this up to SceneManager.sceneLoaded automatically
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
