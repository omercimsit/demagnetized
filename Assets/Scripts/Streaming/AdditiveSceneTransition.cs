using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

// Simple scene transition with black screen fade.
// Preloads target scenes in the background so switching is instant.
public class AdditiveSceneTransition : Singleton<AdditiveSceneTransition>
{
    [Header("Preload")]
    [SerializeField] private string[] scenesToPreload = new string[] { "L_Showcase" };

    [Header("Transition")]
    [SerializeField] private float blackScreenDuration = 3f;
    [SerializeField] private float fadeSpeed = 2f;

    private static System.Collections.Generic.Dictionary<string, AsyncOperation> preloadedScenes
        = new System.Collections.Generic.Dictionary<string, AsyncOperation>();
    private bool transitioning = false;
    private float fadeAlpha = 0f;
    private static Texture2D blackTex;

    protected override void OnAwake()
    {
        if (blackTex == null)
        {
            blackTex = new Texture2D(1, 1);
            blackTex.SetPixel(0, 0, Color.black);
            blackTex.Apply();
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();
    }

    protected override void OnDestroy()
    {
        if (blackTex != null) { Destroy(blackTex); blackTex = null; }
        base.OnDestroy();
    }

    private void Start()
    {
        foreach (var scene in scenesToPreload)
        {
            if (!string.IsNullOrEmpty(scene))
                StartCoroutine(PreloadScene(scene));
        }
    }

    private IEnumerator PreloadScene(string sceneName)
    {
        if (preloadedScenes.ContainsKey(sceneName)) yield break;

        // small delay so the current scene can finish initializing first
        yield return new WaitForSeconds(1f);

        Debug.Log($"[Transition] Preloading: {sceneName}");

        var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        if (op == null)
        {
            Debug.LogError($"[Transition] Cannot load: {sceneName}");
            yield break;
        }

        op.allowSceneActivation = false;
        preloadedScenes[sceneName] = op;

        while (op.progress < 0.9f)
            yield return null;

        // scene is ready in memory, just not active yet
        Debug.Log($"[Transition] Ready: {sceneName} ({op.progress * 100:F0}%)");
    }

    public static void TransitionTo(string target)
    {
        if (Instance == null || Instance.transitioning) return;
        Instance.StartCoroutine(Instance.DoTransition(target));
    }

    private IEnumerator DoTransition(string target)
    {
        transitioning = true;
        string current = SceneManager.GetActiveScene().name;

        // fade to black
        while (fadeAlpha < 1f)
        {
            fadeAlpha += Time.unscaledDeltaTime * fadeSpeed;
            yield return null;
        }
        fadeAlpha = 1f;

        // activate preloaded or do a normal load
        AsyncOperation loadOp;
        if (preloadedScenes.TryGetValue(target, out loadOp) && loadOp != null && loadOp.progress >= 0.9f)
        {
            loadOp.allowSceneActivation = true;
            while (!loadOp.isDone) yield return null;
            preloadedScenes.Remove(target);
        }
        else
        {
            loadOp = SceneManager.LoadSceneAsync(target, LoadSceneMode.Additive);
            if (loadOp != null)
                while (!loadOp.isDone) yield return null;
        }

        var newScene = SceneManager.GetSceneByName(target);
        if (newScene.IsValid())
        {
            SceneManager.SetActiveScene(newScene);
            GameEvents.InvokeSceneTransitionComplete(target);
        }

        // don't unload if it's the only scene
        var oldScene = SceneManager.GetSceneByName(current);
        if (oldScene.IsValid() && oldScene.name != target && SceneManager.sceneCount > 1)
            SceneManager.UnloadSceneAsync(oldScene);

        // give shader compilation and asset loading a moment
        yield return null;
        System.GC.Collect();
        yield return new WaitForSecondsRealtime(0.5f);

        // let a few frames render so shaders compile behind the black screen
        for (int i = 0; i < 10; i++)
            yield return null;

        yield return new WaitForSecondsRealtime(blackScreenDuration - 0.5f);

        // fade back in
        while (fadeAlpha > 0f)
        {
            fadeAlpha -= Time.unscaledDeltaTime * fadeSpeed;
            yield return null;
        }
        fadeAlpha = 0f;

        transitioning = false;
    }

    private void OnGUI()
    {
        if (fadeAlpha <= 0f) return;
        GUI.color = new Color(0, 0, 0, fadeAlpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), blackTex);
        GUI.color = Color.white;
    }
}
