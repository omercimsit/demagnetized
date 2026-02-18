using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

// Coordinates scene loading, shader warmup, GC, and frame warmup
// so the player never sees a stutter when entering a new area.
public class SmoothLoadingOrchestrator : Singleton<SmoothLoadingOrchestrator>
{
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

    private bool _isLoading = false;
    private float _loadProgress = 0f;
    private string _currentLoadingScene = "";
    private string _loadingStatus = "";
    private float _overlayAlpha = 0f;

    public bool IsLoading => _isLoading;
    public float LoadProgress => _loadProgress;
    public string LoadingStatus => _loadingStatus;

    private Texture2D _solidTex;

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

    public void LoadScene(string sceneName)
    {
        if (_isLoading) return;
        StartCoroutine(SmoothLoadSequence(sceneName));
    }

    public void LoadScene(string sceneName, System.Action<float, string> onProgress, System.Action onComplete = null)
    {
        if (_isLoading) return;
        StartCoroutine(SmoothLoadSequence(sceneName, onProgress, onComplete));
    }

    public void PreloadNextScene(string sceneName, System.Action onReady = null)
    {
        if (ScenePreloader.Instance != null)
        {
            ScenePreloader.Instance.QueuePreload(sceneName);

            if (onReady != null)
                StartCoroutine(WaitForPreload(sceneName, onReady));
        }
        else
        {
            Log("ScenePreloader not available, cannot preload");
        }
    }

    // run warmup for a scene that's already loaded (e.g. after respawn)
    public void WarmupCurrentScene(System.Action onComplete = null)
    {
        StartCoroutine(WarmupSequence(onComplete));
    }

    private IEnumerator SmoothLoadSequence(string sceneName, System.Action<float, string> onProgress = null, System.Action onComplete = null)
    {
        _isLoading = true;
        _loadProgress = 0f;
        _currentLoadingScene = sceneName;

        float loadStartTime = Time.realtimeSinceStartup;

        GameEvents.InvokeSceneTransitionStart(sceneName);

        // phase 1: prep
        _loadingStatus = "Preparing...";
        _loadProgress = 0.05f;
        ReportProgress(onProgress);
        Log($"Starting smooth load: {sceneName}");

        yield return StartCoroutine(FadeOverlay(true, fadeInDuration));

        // phase 2: GC
        if (collectGCOnLoad)
        {
            _loadingStatus = "Cleaning memory...";
            _loadProgress = 0.10f;
            ReportProgress(onProgress);

            if (GCManager.Instance != null)
                GCManager.Instance.CollectDuringLoad();
            else
                System.GC.Collect();

            yield return null;
        }

        // phase 3: scene loading (10-80%)
        _loadingStatus = "Loading scene...";
        ReportProgress(onProgress);

        AsyncOperation loadOp = null;

        if (ScenePreloader.Instance != null && ScenePreloader.Instance.IsPreloaded(sceneName))
        {
            Log("Using preloaded scene");
            _loadProgress = 0.75f;
            ReportProgress(onProgress);

            ScenePreloader.Instance.ActivatePreloadedScene(sceneName);
            yield return new WaitForSecondsRealtime(0.1f);
        }
        else
        {
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

            loadOp.allowSceneActivation = true;

            while (!loadOp.isDone)
                yield return null;
        }

        // phase 4: shader warmup (80-90%)
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

        // phase 5: let the scene actually render for a bit (90-95%)
        _loadingStatus = "Warming up...";
        _loadProgress = 0.90f;
        ReportProgress(onProgress);

        // TODO: might want to increase warmupFrames for heavier scenes
        for (int i = 0; i < warmupFrames; i++)
        {
            _loadProgress = 0.90f + (0.05f * i / warmupFrames);
            yield return null;
        }

        // phase 6: final cleanup
        _loadingStatus = "Finalizing...";
        _loadProgress = 0.95f;
        ReportProgress(onProgress);

        System.GC.Collect();
        yield return null;

        // enforce minimum time so the loading screen doesn't flash
        float elapsed = Time.realtimeSinceStartup - loadStartTime;
        if (elapsed < minimumLoadingDuration)
            yield return new WaitForSecondsRealtime(minimumLoadingDuration - elapsed);

        _loadProgress = 1f;
        _loadingStatus = "Complete!";
        ReportProgress(onProgress);

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

        if (ShaderWarmup.Instance != null)
        {
            bool done = false;
            ShaderWarmup.Instance.WarmupAll(null, () => done = true);
            while (!done) yield return null;
        }

        for (int i = 0; i < warmupFrames; i++)
            yield return null;

        System.GC.Collect();
        yield return null;

        Log("Warmup complete");
        onComplete?.Invoke();
    }

    private IEnumerator WaitForPreload(string sceneName, System.Action onReady)
    {
        while (ScenePreloader.Instance != null && !ScenePreloader.Instance.IsPreloaded(sceneName))
            yield return null;
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

    private void OnGUI()
    {
        if (_overlayAlpha <= 0.01f) return;

        GUI.color = new Color(0f, 0f, 0f, _overlayAlpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _solidTex);
        GUI.color = Color.white;
    }

    private void Log(string message)
    {
        if (showDebugLogs)
            Debug.Log($"[SmoothLoader] {message}");
    }
}
