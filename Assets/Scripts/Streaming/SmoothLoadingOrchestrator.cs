using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Smooth Loading Orchestrator - Coordinates all loading operations.
/// Combines scene preloading, shader warmup, GC, and asset loading
/// to provide a seamless loading experience.
/// </summary>
public class SmoothLoadingOrchestrator : Singleton<SmoothLoadingOrchestrator>
{
    #region Settings
    
    [Header("Loading Settings")]
    [SerializeField] private float minimumLoadingDuration = 1f;
    [SerializeField] private float fadeInDuration = 0.3f;
    [SerializeField] private float fadeOutDuration = 0.5f;
    
    [Header("Warm-up Settings")]
    [SerializeField] private bool warmupShadersOnLoad = true;
    [SerializeField] private bool collectGCOnLoad = true;
    [SerializeField] private int warmupFrames = 30;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;
    
    #endregion
    
    #region State
    
    private bool _isLoading = false;
    private float _loadProgress = 0f;
    private string _currentLoadingScene = "";
    private string _loadingStatus = "";
    private float _overlayAlpha = 0f;
    
    public bool IsLoading => _isLoading;
    public float LoadProgress => _loadProgress;
    public string LoadingStatus => _loadingStatus;
    
    // Texture
    private Texture2D _solidTex;
    
    #endregion
    
    #region Lifecycle
    
    protected override void OnAwake()
    {
        _solidTex = new Texture2D(1, 1);
        _solidTex.SetPixel(0, 0, Color.white);
        _solidTex.Apply();
    }

    private void OnDisable()
    {
        StopAllCoroutines();
    }

    protected override void OnDestroy()
    {
        if (_solidTex != null) Destroy(_solidTex);
        base.OnDestroy();
    }

    #endregion
    
    #region Public API
    
    /// <summary>
    /// Load scene with all optimizations
    /// </summary>
    public void LoadScene(string sceneName)
    {
        if (_isLoading) return;
        StartCoroutine(SmoothLoadSequence(sceneName));
    }
    
    /// <summary>
    /// Load scene with progress callback
    /// </summary>
    public void LoadScene(string sceneName, System.Action<float, string> onProgress, System.Action onComplete = null)
    {
        if (_isLoading) return;
        StartCoroutine(SmoothLoadSequence(sceneName, onProgress, onComplete));
    }
    
    /// <summary>
    /// Preload next scene in background
    /// </summary>
    public void PreloadNextScene(string sceneName, System.Action onReady = null)
    {
        if (ScenePreloader.Instance != null)
        {
            ScenePreloader.Instance.QueuePreload(sceneName);
            
            if (onReady != null)
            {
                StartCoroutine(WaitForPreload(sceneName, onReady));
            }
        }
        else
        {
            Log("ScenePreloader not available, cannot preload");
        }
    }
    
    /// <summary>
    /// Perform post-load warmup for current scene
    /// </summary>
    public void WarmupCurrentScene(System.Action onComplete = null)
    {
        StartCoroutine(WarmupSequence(onComplete));
    }
    
    #endregion
    
    #region Loading Sequence
    
    private IEnumerator SmoothLoadSequence(string sceneName, System.Action<float, string> onProgress = null, System.Action onComplete = null)
    {
        _isLoading = true;
        _loadProgress = 0f;
        _currentLoadingScene = sceneName;
        
        float loadStartTime = Time.realtimeSinceStartup;
        
        // Fire event
        GameEvents.InvokeSceneTransitionStart(sceneName);
        
        // ===== PHASE 1: PREPARATION (5%) =====
        _loadingStatus = "Preparing...";
        _loadProgress = 0.05f;
        ReportProgress(onProgress);
        Log($"Starting smooth load: {sceneName}");
        
        // Fade in overlay
        yield return StartCoroutine(FadeOverlay(true, fadeInDuration));
        
        // ===== PHASE 2: GC COLLECTION (10%) =====
        if (collectGCOnLoad)
        {
            _loadingStatus = "Cleaning memory...";
            _loadProgress = 0.10f;
            ReportProgress(onProgress);
            
            if (GCManager.Instance != null)
            {
                GCManager.Instance.CollectDuringLoad();
            }
            else
            {
                System.GC.Collect();
            }
            yield return null;
        }
        
        // ===== PHASE 3: SCENE LOADING (10-80%) =====
        _loadingStatus = "Loading scene...";
        ReportProgress(onProgress);
        
        AsyncOperation loadOp = null;
        
        // Check if scene is preloaded
        if (ScenePreloader.Instance != null && ScenePreloader.Instance.IsPreloaded(sceneName))
        {
            Log("Using preloaded scene");
            _loadProgress = 0.75f;
            ReportProgress(onProgress);
            
            ScenePreloader.Instance.ActivatePreloadedScene(sceneName);
            
            // Wait for scene activation
            yield return new WaitForSecondsRealtime(0.1f);
        }
        else
        {
            // Load normally
            loadOp = SceneManager.LoadSceneAsync(sceneName);
            loadOp.allowSceneActivation = false;
            
            while (loadOp.progress < 0.9f)
            {
                _loadProgress = 0.1f + (loadOp.progress * 0.7f);
                _loadingStatus = $"Loading scene... {(loadOp.progress * 100):F0}%";
                ReportProgress(onProgress);
                GameEvents.InvokeSceneLoadProgress(_loadProgress);
                yield return null;
            }
            
            _loadProgress = 0.80f;
            _loadingStatus = "Activating scene...";
            ReportProgress(onProgress);
            
            // Activate scene
            loadOp.allowSceneActivation = true;
            
            while (!loadOp.isDone)
            {
                yield return null;
            }
        }
        
        // ===== PHASE 4: SHADER WARMUP (80-90%) =====
        if (warmupShadersOnLoad)
        {
            _loadingStatus = "Warming up shaders...";
            _loadProgress = 0.85f;
            ReportProgress(onProgress);
            
            if (ShaderWarmup.Instance != null)
            {
                bool warmupComplete = false;
                ShaderWarmup.Instance.WarmupAll(
                    progress => { _loadProgress = 0.8f + (progress * 0.1f); },
                    () => warmupComplete = true
                );
                
                while (!warmupComplete)
                {
                    ReportProgress(onProgress);
                    yield return null;
                }
            }
        }
        
        // ===== PHASE 5: FRAME WARMUP (90-95%) =====
        _loadingStatus = "Warming up...";
        _loadProgress = 0.90f;
        ReportProgress(onProgress);
        
        // Let the scene render for a few frames to warm up systems
        for (int i = 0; i < warmupFrames; i++)
        {
            _loadProgress = 0.90f + (0.05f * i / warmupFrames);
            yield return null;
        }
        
        // ===== PHASE 6: FINAL GC (95-100%) =====
        _loadingStatus = "Finalizing...";
        _loadProgress = 0.95f;
        ReportProgress(onProgress);
        
        System.GC.Collect();
        yield return null;
        
        // Ensure minimum loading time for visual consistency
        float elapsed = Time.realtimeSinceStartup - loadStartTime;
        if (elapsed < minimumLoadingDuration)
        {
            yield return new WaitForSecondsRealtime(minimumLoadingDuration - elapsed);
        }
        
        // ===== COMPLETE =====
        _loadProgress = 1f;
        _loadingStatus = "Complete!";
        ReportProgress(onProgress);
        
        // Fade out overlay
        yield return StartCoroutine(FadeOverlay(false, fadeOutDuration));
        
        _isLoading = false;
        _currentLoadingScene = "";
        
        Log($"Load complete: {sceneName} ({Time.realtimeSinceStartup - loadStartTime:F2}s)");
        
        GameEvents.InvokeSceneTransitionComplete(sceneName);
        onComplete?.Invoke();
    }
    
    private IEnumerator WarmupSequence(System.Action onComplete)
    {
        Log("Starting warmup sequence...");
        
        // Shader warmup
        if (ShaderWarmup.Instance != null)
        {
            bool done = false;
            ShaderWarmup.Instance.WarmupAll(null, () => done = true);
            while (!done) yield return null;
        }
        
        // Frame warmup
        for (int i = 0; i < warmupFrames; i++)
        {
            yield return null;
        }
        
        // GC
        System.GC.Collect();
        yield return null;
        
        Log("Warmup complete");
        onComplete?.Invoke();
    }
    
    private IEnumerator WaitForPreload(string sceneName, System.Action onReady)
    {
        while (ScenePreloader.Instance != null && !ScenePreloader.Instance.IsPreloaded(sceneName))
        {
            yield return null;
        }
        onReady?.Invoke();
    }
    
    private IEnumerator FadeOverlay(bool fadeIn, float duration)
    {
        float startAlpha = fadeIn ? 0f : 1f;
        float endAlpha = fadeIn ? 1f : 0f;
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            _overlayAlpha = Mathf.Lerp(startAlpha, endAlpha, elapsed / duration);
            yield return null;
        }
        
        _overlayAlpha = endAlpha;
    }
    
    private void ReportProgress(System.Action<float, string> onProgress)
    {
        onProgress?.Invoke(_loadProgress, _loadingStatus);
    }
    
    #endregion
    
    #region GUI
    
    private void OnGUI()
    {
        if (_overlayAlpha <= 0.01f) return;
        
        // Simple loading overlay
        GUI.color = new Color(0f, 0f, 0f, _overlayAlpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _solidTex);
        GUI.color = Color.white;
    }
    
    #endregion
    
    #region Helpers
    
    private void Log(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[SmoothLoader] {message}");
        }
    }
    
    #endregion
}
