using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

// Loading screen manager - shows a progress bar while loading the next scene.
// Pattern: current scene -> load async -> activate when ready
public class LoadingScreenManager : Singleton<LoadingScreenManager>
{
    private static string targetSceneName;
    private static bool isLoading = false;

    [Header("Loading Screen Visuals")]
    [SerializeField] private Color backgroundColor = Color.black;

    // dots cycle: "" -> "." -> ".." -> "..."
    private static readonly string[] _dotPatterns = { "", ".", "..", "..." };

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
            SceneManager.LoadScene(sceneName); // fallback
        }
    }

    private IEnumerator LoadSequence()
    {
        Debug.Log($"[LoadingScreenManager] Starting load sequence for: {targetSceneName}");

        showUI = true;
        loadProgress = 0f;

        // freeze physics/game updates during load
        float originalTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        yield return null;

        // clean up before loading
        System.GC.Collect();
        yield return new WaitForSecondsRealtime(0.1f);

        var unloadOp = Resources.UnloadUnusedAssets();
        while (!unloadOp.isDone)
            yield return null;
        loadProgress = 0.1f;

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

        asyncLoad.allowSceneActivation = false;

        // wait for 90% (Unity never reports 100% while allowSceneActivation is false)
        while (asyncLoad.progress < 0.9f)
        {
            loadProgress = 0.1f + (asyncLoad.progress * 0.8f);
            yield return null;
        }
        loadProgress = 0.9f;

        Debug.Log("[LoadingScreenManager] Scene loaded to 90%, preparing activation...");

        yield return new WaitForSecondsRealtime(0.2f);
        loadProgress = 0.95f;

        // restore time before activating so Start() methods run with correct timescale
        Time.timeScale = originalTimeScale;

        asyncLoad.allowSceneActivation = true;

        while (!asyncLoad.isDone)
        {
            loadProgress = 0.95f + (asyncLoad.progress * 0.05f);
            yield return null;
        }

        loadProgress = 1f;
        Debug.Log("[LoadingScreenManager] Scene fully loaded!");

        // brief pause so player sees 100%
        yield return new WaitForSecondsRealtime(0.2f);

        showUI = false;
        isLoading = false;

        System.GC.Collect();
    }

    private void Update()
    {
        if (showUI)
        {
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

        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), bgTexture);

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

        string text = L.Get("loading") + _dotPatterns[textIndex];
        GUI.Label(new Rect(0, centerY - 80, Screen.width, 60), text, titleStyle);

        float barWidth = 500;
        float barHeight = 12;
        float barX = (Screen.width - barWidth) / 2f;
        float barY = centerY;

        // bar background
        GUI.color = new Color(0.15f, 0.15f, 0.15f);
        GUI.DrawTexture(new Rect(barX, barY, barWidth, barHeight), whiteTexture);

        // fill
        GUI.color = new Color(0.2f, 0.7f, 0.3f);
        GUI.DrawTexture(new Rect(barX, barY, barWidth * loadProgress, barHeight), whiteTexture);

        // border lines
        GUI.color = new Color(0.3f, 0.3f, 0.3f);
        float borderWidth = 2;
        GUI.DrawTexture(new Rect(barX - borderWidth, barY - borderWidth, barWidth + borderWidth * 2, borderWidth), whiteTexture);
        GUI.DrawTexture(new Rect(barX - borderWidth, barY + barHeight, barWidth + borderWidth * 2, borderWidth), whiteTexture);
        GUI.DrawTexture(new Rect(barX - borderWidth, barY, borderWidth, barHeight), whiteTexture);
        GUI.DrawTexture(new Rect(barX + barWidth, barY, borderWidth, barHeight), whiteTexture);

        GUI.color = Color.white;

        int percent = Mathf.RoundToInt(loadProgress * 100);
        GUI.Label(new Rect(0, barY + 30, Screen.width, 50), $"{percent}%", percentStyle);
    }
}
