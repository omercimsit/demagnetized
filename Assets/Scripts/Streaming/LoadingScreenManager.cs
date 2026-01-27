using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// PROFESSIONAL Loading Screen Manager
/// Pattern: Current Scene → Loading Scene → Target Scene
/// This eliminates ALL stuttering by using an intermediate loading scene.
/// </summary>
public class LoadingScreenManager : Singleton<LoadingScreenManager>
{
    // Static vars to pass between scenes
    private static string targetSceneName;
    private static bool isLoading = false;

    [Header("Loading Screen Visuals")]
    [SerializeField] private Color backgroundColor = Color.black;

    // Dot animation patterns (appended to localized "loading" key)
    private static readonly string[] _dotPatterns = { "", ".", "..", "..." };

    // Runtime
    private float loadProgress = 0f;
    private int textIndex = 0;
    private float textTimer = 0f;
    private bool showUI = false;
    private static Texture2D bgTexture;
    private static Texture2D whiteTexture;
    private GUIStyle titleStyle;
    private GUIStyle percentStyle;

    protected override void OnAwake()
    {
        // Create textures
        if (bgTexture == null)
        {
            bgTexture = new Texture2D(1, 1);
            bgTexture.SetPixel(0, 0, backgroundColor);
            bgTexture.Apply();
        }
        if (whiteTexture == null)
        {
            whiteTexture = new Texture2D(1, 1);
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply();
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();
    }

    protected override void OnDestroy()
    {
        if (bgTexture != null) { Destroy(bgTexture); bgTexture = null; }
        if (whiteTexture != null) { Destroy(whiteTexture); whiteTexture = null; }
        base.OnDestroy();
    }
    
    /// <summary>
    /// PUBLIC: Call this to load a scene smoothly
    /// </summary>
    public static void LoadScene(string sceneName)
    {
        if (isLoading)
        {
            Debug.LogWarning("[LoadingScreenManager] Already loading a scene!");
            return;
        }
        
        targetSceneName = sceneName;
        isLoading = true;
        
        if (Instance != null)
        {
            Instance.StartCoroutine(Instance.LoadSequence());
        }
        else
        {
            Debug.LogError("[LoadingScreenManager] No instance! Create a LoadingScreenManager in your scene.");
            // Fallback direct load
            SceneManager.LoadScene(sceneName);
        }
    }
    
    private IEnumerator LoadSequence()
    {
        Debug.Log($"[LoadingScreenManager] Starting load sequence for: {targetSceneName}");
        
        // 1. Show loading screen
        showUI = true;
        loadProgress = 0f;
        
        // 2. Freeze time to prevent physics/game updates
        float originalTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        
        // 3. Wait one frame for UI to render
        yield return null;
        
        // 4. Garbage collection (unscaled time)
        System.GC.Collect();
        yield return new WaitForSecondsRealtime(0.1f);
        
        // 5. Unload current scene's unused assets
        var unloadOp = Resources.UnloadUnusedAssets();
        while (!unloadOp.isDone)
        {
            yield return null;
        }
        loadProgress = 0.1f;
        
        // 6. Start async load of target scene
        Debug.Log($"[LoadingScreenManager] Loading: {targetSceneName}");
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(targetSceneName);
        
        if (asyncLoad == null)
        {
            Debug.LogError($"[LoadingScreenManager] Failed to load scene: {targetSceneName}");
            Time.timeScale = originalTimeScale;
            showUI = false;
            isLoading = false;
            yield break;
        }
        
        // Don't activate yet
        asyncLoad.allowSceneActivation = false;
        
        // 7. Wait for load to reach 90%
        while (asyncLoad.progress < 0.9f)
        {
            loadProgress = 0.1f + (asyncLoad.progress * 0.8f); // 10% - 82%
            yield return null;
        }
        loadProgress = 0.9f;
        
        Debug.Log("[LoadingScreenManager] Scene loaded to 90%, preparing activation...");
        
        // 8. Small delay to let memory settle
        yield return new WaitForSecondsRealtime(0.2f);
        loadProgress = 0.95f;
        
        // 9. Restore time BEFORE activation
        Time.timeScale = originalTimeScale;
        
        // 10. Activate scene
        asyncLoad.allowSceneActivation = true;
        
        // 11. Wait for complete
        while (!asyncLoad.isDone)
        {
            loadProgress = 0.95f + (asyncLoad.progress * 0.05f);
            yield return null;
        }
        
        loadProgress = 1f;
        Debug.Log("[LoadingScreenManager] Scene fully loaded!");
        
        // 12. Brief delay to show 100%
        yield return new WaitForSecondsRealtime(0.2f);
        
        // 13. Hide loading screen
        showUI = false;
        isLoading = false;
        
        // 14. Final GC
        System.GC.Collect();
    }
    
    private void Update()
    {
        if (showUI)
        {
            // Animate loading text
            textTimer += Time.unscaledDeltaTime;
            if (textTimer > 0.3f)
            {
                textTimer = 0f;
                textIndex = (textIndex + 1) % _dotPatterns.Length;
            }
        }
    }
    
    private void OnGUI()
    {
        if (!showUI) return;
        
        // Black background
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), bgTexture);
        
        // Create styles
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle();
            titleStyle.fontSize = 42;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.normal.textColor = Color.white;
        }
        
        if (percentStyle == null)
        {
            percentStyle = new GUIStyle();
            percentStyle.fontSize = 28;
            percentStyle.alignment = TextAnchor.MiddleCenter;
            percentStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
        }
        
        float centerY = Screen.height / 2f;
        
        // Loading text with animation (localized)
        string text = L.Get("loading") + _dotPatterns[textIndex];
        GUI.Label(new Rect(0, centerY - 80, Screen.width, 60), text, titleStyle);
        
        // Progress bar
        float barWidth = 500;
        float barHeight = 12;
        float barX = (Screen.width - barWidth) / 2f;
        float barY = centerY;
        
        // Background
        GUI.color = new Color(0.15f, 0.15f, 0.15f);
        GUI.DrawTexture(new Rect(barX, barY, barWidth, barHeight), whiteTexture);
        
        // Fill
        GUI.color = new Color(0.2f, 0.7f, 0.3f);
        GUI.DrawTexture(new Rect(barX, barY, barWidth * loadProgress, barHeight), whiteTexture);
        
        // Border
        GUI.color = new Color(0.3f, 0.3f, 0.3f);
        float borderWidth = 2;
        GUI.DrawTexture(new Rect(barX - borderWidth, barY - borderWidth, barWidth + borderWidth * 2, borderWidth), whiteTexture);
        GUI.DrawTexture(new Rect(barX - borderWidth, barY + barHeight, barWidth + borderWidth * 2, borderWidth), whiteTexture);
        GUI.DrawTexture(new Rect(barX - borderWidth, barY, borderWidth, barHeight), whiteTexture);
        GUI.DrawTexture(new Rect(barX + barWidth, barY, borderWidth, barHeight), whiteTexture);
        
        GUI.color = Color.white;
        
        // Percentage
        int percent = Mathf.RoundToInt(loadProgress * 100);
        GUI.Label(new Rect(0, barY + 30, Screen.width, 50), $"{percent}%", percentStyle);
    }
}
