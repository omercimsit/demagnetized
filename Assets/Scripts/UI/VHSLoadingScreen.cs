using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

// color aliases so the code doesn't look like a wall of S.Whatever
using S = MenuStyles;

// VHS style loading screen with static, scanlines, animated cassette
// uses MenuStyles for the shared textures/colors
public class VHSLoadingScreen : Singleton<VHSLoadingScreen>
{
    [Header("Settings")]
    [SerializeField] private float minimumLoadTime = 1.5f;
    [SerializeField] private float fadeSpeed = 2f;

    private bool isLoading = false;
    private float loadProgress = 0f;
    private float displayProgress = 0f;
    private float alpha = 0f;
    private string targetScene = "";
    private AsyncOperation loadOperation;

    // VHS effect timers
    private Texture2D solidTex => S.SolidTexture;
    private Texture2D noiseTex => S.NoiseTexture;
    private float noiseTime = 0f;
    private float scanlineOffset = 0f;
    private float glitchTimer = 0f;
    private float flickerTimer = 0f;

    // pull colors from MenuStyles instead of hardcoding
    private Color filmBrown => S.DarkFilmBrown;
    private Color tapeOrange => S.TapeOrange;
    private Color warmWhite => S.WarmWhite;
    private Color dustyGray => S.DustyGray;

    // cached strings to avoid allocs in OnGUI
    private string _cachedTimestamp;
    private string _cachedPercentStr;
    private int _lastCachedSecond = -1;
    private int _lastCachedPercent = -1;

    private void OnDisable()
    {
        StopAllCoroutines();
    }

    private void Update()
    {
        if (!isLoading) return;

        float dt = Time.unscaledDeltaTime;

        alpha = Mathf.MoveTowards(alpha, 1f, fadeSpeed * dt);
        displayProgress = Mathf.MoveTowards(displayProgress, loadProgress, dt);

        noiseTime += dt;
        scanlineOffset += dt * 50f;
        if (Random.value < 0.03f) glitchTimer = 0.05f;
        glitchTimer = Mathf.Max(0, glitchTimer - dt);
        flickerTimer += dt;
    }

    public void LoadScene(string sceneName)
    {
        if (isLoading) return;
        targetScene = sceneName;
        StartCoroutine(LoadSceneAsync());
    }

    public static void Load(string sceneName)
    {
        if (Instance == null)
        {
            var go = new GameObject("VHSLoadingScreen");
            go.AddComponent<VHSLoadingScreen>();
        }
        Instance.LoadScene(sceneName);
    }

    private IEnumerator LoadSceneAsync()
    {
        isLoading = true;
        loadProgress = 0f;
        displayProgress = 0f;
        alpha = 0f;

        // wait for the screen to fade in before starting the load
        while (alpha < 0.95f)
            yield return null;

        float startTime = Time.unscaledTime;

        loadOperation = SceneManager.LoadSceneAsync(targetScene);
        loadOperation.allowSceneActivation = false;

        while (!loadOperation.isDone)
        {
            // Unity caps progress at 0.9 before activation
            loadProgress = Mathf.Clamp01(loadOperation.progress / 0.9f);

            if (loadOperation.progress >= 0.9f &&
                Time.unscaledTime - startTime >= minimumLoadTime &&
                displayProgress >= 0.95f)
            {
                loadOperation.allowSceneActivation = true;
            }

            yield return null;
        }

        // fade back out
        while (alpha > 0.01f)
        {
            alpha = Mathf.MoveTowards(alpha, 0f, fadeSpeed * Time.unscaledDeltaTime);
            yield return null;
        }

        isLoading = false;
    }

    private void OnGUI()
    {
        if (alpha < 0.01f) return;

        S.ResetStylePool();
        float w = Screen.width;
        float h = Screen.height;
        float cx = w / 2f;
        float cy = h / 2f;
        float uiScale = Mathf.Min(w / 1920f, h / 1080f);

        // dark background
        GUI.color = new Color(0.02f, 0.015f, 0.01f, alpha);
        GUI.DrawTexture(new Rect(0, 0, w, h), solidTex);

        // vignette - layered quads, not great but it works
        for (int i = 0; i < 8; i++)
        {
            float inset = i * Mathf.Min(w, h) * 0.06f;
            float vigA = 0.08f * (8 - i) / 8f * alpha;
            GUI.color = new Color(0f, 0f, 0f, vigA);
            GUI.DrawTexture(new Rect(0, 0, w, inset), solidTex);
            GUI.DrawTexture(new Rect(0, h - inset, w, inset), solidTex);
            GUI.DrawTexture(new Rect(0, 0, inset, h), solidTex);
            GUI.DrawTexture(new Rect(w - inset, 0, inset, h), solidTex);
        }

        // VHS static
        if (noiseTex != null)
        {
            float staticIntensity = 0.08f + glitchTimer * 3f;
            GUI.color = new Color(1f, 1f, 1f, staticIntensity * alpha);
            GUI.DrawTextureWithTexCoords(
                new Rect(0, 0, w, h),
                noiseTex,
                new Rect(noiseTime * 3f, noiseTime * 2f, 8f, 8f * (h/w))
            );
        }

        // scanlines
        GUI.color = new Color(0f, 0f, 0f, 0.025f * alpha);
        for (float y = (scanlineOffset % 4); y < h; y += 4)
        {
            GUI.DrawTexture(new Rect(0, y, w, 1.5f), solidTex);
        }

        // glitch bars - random horizontal corruption strips
        if (glitchTimer > 0)
        {
            GUI.color = new Color(tapeOrange.r, tapeOrange.g, tapeOrange.b, 0.15f * alpha);
            float gy = Random.Range(0f, h);
            GUI.DrawTexture(new Rect(0, gy, w, Random.Range(8f, 25f)), solidTex);

            // RGB separation
            GUI.color = new Color(1f, 0.3f, 0.2f, 0.05f * alpha);
            GUI.DrawTexture(new Rect(Random.Range(-5f, 5f), gy + 2, w, 3), solidTex);
            GUI.color = new Color(0.2f, 1f, 0.4f, 0.05f * alpha);
            GUI.DrawTexture(new Rect(Random.Range(-5f, 5f), gy - 2, w, 3), solidTex);
        }

        DrawPremiumCassette(cx, cy - 50 * uiScale, uiScale, alpha);

        // title with double-render glow trick
        float titleY = cy - 140 * uiScale;
        var glowStyle = S.S(GUI.skin.label, Mathf.RoundToInt(42 * uiScale),
            new Color(tapeOrange.r, tapeOrange.g, tapeOrange.b, 0.2f * alpha), TextAnchor.MiddleCenter);
        glowStyle.fontStyle = FontStyle.Bold;
        GUI.Label(new Rect(cx - 250, titleY - 2, 500, 50), "DEMAGNETIZED", glowStyle);
        GUI.Label(new Rect(cx - 250, titleY + 2, 500, 50), "DEMAGNETIZED", glowStyle);

        var titleStyle = S.S(GUI.skin.label, Mathf.RoundToInt(42 * uiScale),
            new Color(warmWhite.r, warmWhite.g, warmWhite.b, alpha), TextAnchor.MiddleCenter);
        titleStyle.fontStyle = FontStyle.Bold;
        GUI.Label(new Rect(cx - 250, titleY, 500, 50), "DEMAGNETIZED", titleStyle);

        // "LOADING..." with animated dots
        float pulse = 0.7f + Mathf.Sin(flickerTimer * 4f) * 0.3f;
        float flicker = (Mathf.Sin(flickerTimer * 15f) > 0.3f) ? 1f : 0.85f;
        var loadStyle = S.S(GUI.skin.label, Mathf.RoundToInt(18 * uiScale),
            new Color(dustyGray.r, dustyGray.g, dustyGray.b, alpha * pulse * flicker), TextAnchor.MiddleCenter);
        loadStyle.fontStyle = FontStyle.Bold;

        string loadingText = "◉ " + L.Get("loading");
        int dots = (int)(flickerTimer * 2f) % 4;
        loadingText += new string('.', dots);
        GUI.Label(new Rect(cx - 100, cy + 60 * uiScale, 200, 30), loadingText, loadStyle);

        // progress bar
        float barW = 320 * uiScale;
        float barH = 8 * uiScale;
        float barX = cx - barW / 2;
        float barY = cy + 95 * uiScale;

        GUI.color = new Color(0.03f, 0.025f, 0.02f, alpha);
        GUI.DrawTexture(new Rect(barX - 2, barY - 2, barW + 4, barH + 4), solidTex);
        GUI.color = new Color(0.08f, 0.06f, 0.05f, alpha);
        GUI.DrawTexture(new Rect(barX, barY, barW, barH), solidTex);

        float fillW = barW * displayProgress;
        if (fillW > 0)
        {
            GUI.color = new Color(tapeOrange.r, tapeOrange.g, tapeOrange.b, alpha);
            GUI.DrawTexture(new Rect(barX, barY, fillW, barH), solidTex);

            if (displayProgress < 1f)
            {
                GUI.color = new Color(warmWhite.r, warmWhite.g, warmWhite.b, 0.5f * alpha * pulse);
                GUI.DrawTexture(new Rect(barX + fillW - 4, barY, 4, barH), solidTex);
            }

            GUI.color = new Color(warmWhite.r, warmWhite.g, warmWhite.b, 0.15f * alpha);
            GUI.DrawTexture(new Rect(barX, barY, fillW, 2), solidTex);
        }

        // percentage label - cached to avoid string allocs every frame
        var pctStyle = S.S(GUI.skin.label, Mathf.RoundToInt(13 * uiScale),
            new Color(tapeOrange.r, tapeOrange.g, tapeOrange.b, alpha), TextAnchor.MiddleCenter);
        pctStyle.fontStyle = FontStyle.Bold;
        int pctInt = Mathf.RoundToInt(displayProgress * 100);
        if (pctInt != _lastCachedPercent) { _lastCachedPercent = pctInt; _cachedPercentStr = pctInt + "%"; }
        GUI.Label(new Rect(cx - 40, barY + barH + 8, 80, 22), _cachedPercentStr, pctStyle);

        // corner brackets
        float bracketSize = 50 * uiScale;
        float bracketThick = 3 * uiScale;
        float margin = 40 * uiScale;

        float bracketAlpha = 0.2f + Mathf.Sin(flickerTimer * 2f) * 0.05f;
        GUI.color = new Color(tapeOrange.r, tapeOrange.g, tapeOrange.b, bracketAlpha * alpha);

        GUI.DrawTexture(new Rect(margin, margin, bracketSize, bracketThick), solidTex);
        GUI.DrawTexture(new Rect(margin, margin, bracketThick, bracketSize), solidTex);

        GUI.DrawTexture(new Rect(w - margin - bracketSize, margin, bracketSize, bracketThick), solidTex);
        GUI.DrawTexture(new Rect(w - margin - bracketThick, margin, bracketThick, bracketSize), solidTex);

        GUI.DrawTexture(new Rect(margin, h - margin - bracketThick, bracketSize, bracketThick), solidTex);
        GUI.DrawTexture(new Rect(margin, h - margin - bracketSize, bracketThick, bracketSize), solidTex);

        GUI.DrawTexture(new Rect(w - margin - bracketSize, h - margin - bracketThick, bracketSize, bracketThick), solidTex);
        GUI.DrawTexture(new Rect(w - margin - bracketThick, h - margin - bracketSize, bracketThick, bracketSize), solidTex);

        // timestamp in the corner like a real VHS
        var timeStyle = S.S(GUI.skin.label, Mathf.RoundToInt(11 * uiScale),
            new Color(tapeOrange.r, tapeOrange.g, tapeOrange.b, 0.4f * alpha));
        int sec = System.DateTime.Now.Second;
        if (sec != _lastCachedSecond) { _lastCachedSecond = sec; _cachedTimestamp = L.Get("vhs_rec") + " \u25C9 " + System.DateTime.Now.ToString("HH:mm:ss"); }
        GUI.Label(new Rect(margin + 10, h - margin - 25, 150, 20), _cachedTimestamp, timeStyle);

        var verStyle = S.S(GUI.skin.label, Mathf.RoundToInt(10 * uiScale),
            new Color(dustyGray.r, dustyGray.g, dustyGray.b, 0.3f * alpha), TextAnchor.MiddleRight);
        GUI.Label(new Rect(w - margin - 110, h - margin - 25, 100, 20), "v1.0.0", verStyle);

        GUI.color = Color.white;
    }

    private void DrawPremiumCassette(float cx, float cy, float scale, float alpha)
    {
        float iconW = 140 * scale;
        float iconH = 90 * scale;
        float x = cx - iconW / 2;
        float y = cy - iconH / 2;
        float thick = 3 * scale;

        // cassette body
        GUI.color = new Color(0.06f, 0.05f, 0.04f, alpha);
        GUI.DrawTexture(new Rect(x, y, iconW, iconH), solidTex);

        // outer frame
        GUI.color = new Color(tapeOrange.r, tapeOrange.g, tapeOrange.b, 0.6f * alpha);
        GUI.DrawTexture(new Rect(x + 5, y, iconW - 10, thick), solidTex);
        GUI.DrawTexture(new Rect(x + 5, y + iconH - thick, iconW - 10, thick), solidTex);
        GUI.DrawTexture(new Rect(x, y + 5, thick, iconH - 10), solidTex);
        GUI.DrawTexture(new Rect(x + iconW - thick, y + 5, thick, iconH - 10), solidTex);

        // corner accents
        GUI.DrawTexture(new Rect(x + 5, y + 5, 8, 2), solidTex);
        GUI.DrawTexture(new Rect(x + 5, y + 5, 2, 8), solidTex);
        GUI.DrawTexture(new Rect(x + iconW - 13, y + 5, 8, 2), solidTex);
        GUI.DrawTexture(new Rect(x + iconW - 7, y + 5, 2, 8), solidTex);

        // tape window
        float winW = 70 * scale;
        float winH = 35 * scale;
        float winX = cx - winW / 2;
        float winY = cy - winH / 2 - 8 * scale;

        GUI.color = new Color(0.02f, 0.02f, 0.02f, alpha);
        GUI.DrawTexture(new Rect(winX, winY, winW, winH), solidTex);

        GUI.color = new Color(dustyGray.r, dustyGray.g, dustyGray.b, 0.3f * alpha);
        GUI.DrawTexture(new Rect(winX, winY, winW, 1), solidTex);
        GUI.DrawTexture(new Rect(winX, winY + winH - 1, winW, 1), solidTex);
        GUI.DrawTexture(new Rect(winX, winY, 1, winH), solidTex);
        GUI.DrawTexture(new Rect(winX + winW - 1, winY, 1, winH), solidTex);

        // animated reels
        float reelR = 12 * scale;
        float reelY = winY + winH / 2;
        float rot = noiseTime * 200f;

        DrawAnimatedReel(winX + 18 * scale, reelY, reelR, rot, alpha, scale);
        DrawAnimatedReel(winX + winW - 18 * scale, reelY, reelR, -rot * 0.9f, alpha, scale);

        // tape between the reels
        GUI.color = new Color(0.12f, 0.08f, 0.06f, alpha);
        GUI.DrawTexture(new Rect(winX + 18 * scale + reelR, reelY - 2, winW - 36 * scale - reelR * 2, 4 * scale), solidTex);

        // label
        float labelY = cy + 18 * scale;
        GUI.color = new Color(warmWhite.r, warmWhite.g, warmWhite.b, 0.08f * alpha);
        GUI.DrawTexture(new Rect(x + 15 * scale, labelY, iconW - 30 * scale, 18 * scale), solidTex);

        var labelStyle = S.S(GUI.skin.label, Mathf.RoundToInt(9 * scale),
            new Color(tapeOrange.r, tapeOrange.g, tapeOrange.b, 0.5f * alpha), TextAnchor.MiddleCenter);
        labelStyle.fontStyle = FontStyle.Bold;
        GUI.Label(new Rect(x + 15 * scale, labelY, iconW - 30 * scale, 18 * scale), L.Get("vhs_play"), labelStyle);

        // decorative screws
        float screwSize = 6 * scale;
        GUI.color = new Color(dustyGray.r, dustyGray.g, dustyGray.b, 0.4f * alpha);
        GUI.DrawTexture(new Rect(x + 12, y + iconH - 18, screwSize, screwSize), solidTex);
        GUI.DrawTexture(new Rect(x + iconW - 18, y + iconH - 18, screwSize, screwSize), solidTex);
    }

    private void DrawAnimatedReel(float cx, float cy, float r, float rotation, float alpha, float scale)
    {
        // outer ring dots
        GUI.color = new Color(dustyGray.r, dustyGray.g, dustyGray.b, 0.5f * alpha);
        for (int i = 0; i < 12; i++)
        {
            float angle = i * 30f * Mathf.Deg2Rad;
            float dx = Mathf.Cos(angle) * r;
            float dy = Mathf.Sin(angle) * r;
            GUI.DrawTexture(new Rect(cx + dx - 1.5f, cy + dy - 1.5f, 3, 3), solidTex);
        }

        // center hub
        float hubR = 4 * scale;
        GUI.color = new Color(0.15f, 0.12f, 0.1f, alpha);
        GUI.DrawTexture(new Rect(cx - hubR, cy - hubR, hubR * 2, hubR * 2), solidTex);

        // rotating spokes
        GUI.color = new Color(dustyGray.r, dustyGray.g, dustyGray.b, 0.6f * alpha);
        for (int i = 0; i < 3; i++)
        {
            float angle = (i * 120f + rotation) * Mathf.Deg2Rad;
            float innerR = 3 * scale;
            float outerR = r - 2;

            float x1 = cx + Mathf.Cos(angle) * innerR;
            float y1 = cy + Mathf.Sin(angle) * innerR;
            float x2 = cx + Mathf.Cos(angle) * outerR;
            float y2 = cy + Mathf.Sin(angle) * outerR;

            // draw spoke as lerped dots - not the most elegant but it works
            int steps = 5;
            for (int s = 0; s < steps; s++)
            {
                float t = s / (float)steps;
                float px = Mathf.Lerp(x1, x2, t);
                float py = Mathf.Lerp(y1, y2, t);
                GUI.DrawTexture(new Rect(px - 1, py - 1, 2, 2), solidTex);
            }
        }
    }
}
