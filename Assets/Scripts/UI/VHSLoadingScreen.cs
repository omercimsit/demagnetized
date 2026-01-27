using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

// Color aliases from MenuStyles for cleaner code
using S = MenuStyles;

/// <summary>
/// DEMAGNETIZED - VHS Style Loading Screen
/// Vintage loading animation with static effects
/// Now uses MenuStyles for shared styling
/// </summary>
public class VHSLoadingScreen : Singleton<VHSLoadingScreen>
{
    [Header("Settings")]
    [SerializeField] private float minimumLoadTime = 1.5f;
    [SerializeField] private float fadeSpeed = 2f;

    // State
    private bool isLoading = false;
    private float loadProgress = 0f;
    private float displayProgress = 0f;
    private float alpha = 0f;
    private string targetScene = "";
    private AsyncOperation loadOperation;

    // VHS Effects (shared from MenuStyles)
    private Texture2D solidTex => S.SolidTexture;
    private Texture2D noiseTex => S.NoiseTexture;
    private float noiseTime = 0f;
    private float scanlineOffset = 0f;
    private float glitchTimer = 0f;
    private float flickerTimer = 0f;

    // Colors from MenuStyles
    private Color filmBrown => S.DarkFilmBrown;
    private Color tapeOrange => S.TapeOrange;
    private Color warmWhite => S.WarmWhite;
    private Color dustyGray => S.DustyGray;

    // Cached strings to avoid per-frame allocation in OnGUI
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
        
        // Fade in
        alpha = Mathf.MoveTowards(alpha, 1f, fadeSpeed * dt);
        
        // Smooth progress
        displayProgress = Mathf.MoveTowards(displayProgress, loadProgress, dt);
        
        // VHS effects
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
            // Create instance if not exists
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
        
        // Wait for fade in
        while (alpha < 0.95f)
            yield return null;
        
        float startTime = Time.unscaledTime;
        
        // Start loading
        loadOperation = SceneManager.LoadSceneAsync(targetScene);
        loadOperation.allowSceneActivation = false;
        
        while (!loadOperation.isDone)
        {
            // Progress goes from 0 to 0.9 before activation
            loadProgress = Mathf.Clamp01(loadOperation.progress / 0.9f);
            
            // Check if loading is complete and minimum time passed
            if (loadOperation.progress >= 0.9f && 
                Time.unscaledTime - startTime >= minimumLoadTime &&
                displayProgress >= 0.95f)
            {
                loadOperation.allowSceneActivation = true;
            }
            
            yield return null;
        }
        
        // Fade out
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
        
        // === DARK CINEMATIC BACKGROUND ===
        GUI.color = new Color(0.02f, 0.015f, 0.01f, alpha);
        GUI.DrawTexture(new Rect(0, 0, w, h), solidTex);
        
        // === RADIAL VIGNETTE ===
        for (int i = 0; i < 8; i++)
        {
            float inset = i * Mathf.Min(w, h) * 0.06f;
            float vigA = 0.08f * (8 - i) / 8f * alpha;
            GUI.color = new Color(0f, 0f, 0f, vigA);
            // Top
            GUI.DrawTexture(new Rect(0, 0, w, inset), solidTex);
            // Bottom
            GUI.DrawTexture(new Rect(0, h - inset, w, inset), solidTex);
            // Left
            GUI.DrawTexture(new Rect(0, 0, inset, h), solidTex);
            // Right
            GUI.DrawTexture(new Rect(w - inset, 0, inset, h), solidTex);
        }
        
        // === VHS STATIC NOISE ===
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
        
        // === SCANLINES ===
        GUI.color = new Color(0f, 0f, 0f, 0.025f * alpha);
        for (float y = (scanlineOffset % 4); y < h; y += 4)
        {
            GUI.DrawTexture(new Rect(0, y, w, 1.5f), solidTex);
        }
        
        // === GLITCH BARS ===
        if (glitchTimer > 0)
        {
            GUI.color = new Color(tapeOrange.r, tapeOrange.g, tapeOrange.b, 0.15f * alpha);
            float gy = Random.Range(0f, h);
            GUI.DrawTexture(new Rect(0, gy, w, Random.Range(8f, 25f)), solidTex);
            
            // RGB separation glitch
            GUI.color = new Color(1f, 0.3f, 0.2f, 0.05f * alpha);
            GUI.DrawTexture(new Rect(Random.Range(-5f, 5f), gy + 2, w, 3), solidTex);
            GUI.color = new Color(0.2f, 1f, 0.4f, 0.05f * alpha);
            GUI.DrawTexture(new Rect(Random.Range(-5f, 5f), gy - 2, w, 3), solidTex);
        }
        
        // === ANIMATED VHS CASSETTE ===
        DrawPremiumCassette(cx, cy - 50 * uiScale, uiScale, alpha);
        
        // === GAME TITLE with glow ===
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
        
        // === "LOADING" TEXT with pulse ===
        float pulse = 0.7f + Mathf.Sin(flickerTimer * 4f) * 0.3f;
        float flicker = (Mathf.Sin(flickerTimer * 15f) > 0.3f) ? 1f : 0.85f;
        var loadStyle = S.S(GUI.skin.label, Mathf.RoundToInt(18 * uiScale),
            new Color(dustyGray.r, dustyGray.g, dustyGray.b, alpha * pulse * flicker), TextAnchor.MiddleCenter);
        loadStyle.fontStyle = FontStyle.Bold;

        string loadingText = "◉ " + L.Get("loading");
        int dots = (int)(flickerTimer * 2f) % 4;
        loadingText += new string('.', dots);
        GUI.Label(new Rect(cx - 100, cy + 60 * uiScale, 200, 30), loadingText, loadStyle);
        
        // === PREMIUM PROGRESS BAR ===
        float barW = 320 * uiScale;
        float barH = 8 * uiScale;
        float barX = cx - barW / 2;
        float barY = cy + 95 * uiScale;
        
        // Bar background with inner shadow
        GUI.color = new Color(0.03f, 0.025f, 0.02f, alpha);
        GUI.DrawTexture(new Rect(barX - 2, barY - 2, barW + 4, barH + 4), solidTex);
        GUI.color = new Color(0.08f, 0.06f, 0.05f, alpha);
        GUI.DrawTexture(new Rect(barX, barY, barW, barH), solidTex);
        
        // Animated fill with gradient effect
        float fillW = barW * displayProgress;
        if (fillW > 0)
        {
            // Main fill
            GUI.color = new Color(tapeOrange.r, tapeOrange.g, tapeOrange.b, alpha);
            GUI.DrawTexture(new Rect(barX, barY, fillW, barH), solidTex);
            
            // Glow on leading edge
            if (displayProgress < 1f)
            {
                GUI.color = new Color(warmWhite.r, warmWhite.g, warmWhite.b, 0.5f * alpha * pulse);
                GUI.DrawTexture(new Rect(barX + fillW - 4, barY, 4, barH), solidTex);
            }
            
            // Top highlight
            GUI.color = new Color(warmWhite.r, warmWhite.g, warmWhite.b, 0.15f * alpha);
            GUI.DrawTexture(new Rect(barX, barY, fillW, 2), solidTex);
        }
        
        // Percentage with style
        var pctStyle = S.S(GUI.skin.label, Mathf.RoundToInt(13 * uiScale),
            new Color(tapeOrange.r, tapeOrange.g, tapeOrange.b, alpha), TextAnchor.MiddleCenter);
        pctStyle.fontStyle = FontStyle.Bold;
        int pctInt = Mathf.RoundToInt(displayProgress * 100);
        if (pctInt != _lastCachedPercent) { _lastCachedPercent = pctInt; _cachedPercentStr = pctInt + "%"; }
        GUI.Label(new Rect(cx - 40, barY + barH + 8, 80, 22), _cachedPercentStr, pctStyle);
        
        // === CORNER BRACKETS - Premium style ===
        float bracketSize = 50 * uiScale;
        float bracketThick = 3 * uiScale;
        float margin = 40 * uiScale;
        
        // Animated subtle pulse
        float bracketAlpha = 0.2f + Mathf.Sin(flickerTimer * 2f) * 0.05f;
        GUI.color = new Color(tapeOrange.r, tapeOrange.g, tapeOrange.b, bracketAlpha * alpha);
        
        // Top-left
        GUI.DrawTexture(new Rect(margin, margin, bracketSize, bracketThick), solidTex);
        GUI.DrawTexture(new Rect(margin, margin, bracketThick, bracketSize), solidTex);
        
        // Top-right
        GUI.DrawTexture(new Rect(w - margin - bracketSize, margin, bracketSize, bracketThick), solidTex);
        GUI.DrawTexture(new Rect(w - margin - bracketThick, margin, bracketThick, bracketSize), solidTex);
        
        // Bottom-left
        GUI.DrawTexture(new Rect(margin, h - margin - bracketThick, bracketSize, bracketThick), solidTex);
        GUI.DrawTexture(new Rect(margin, h - margin - bracketSize, bracketThick, bracketSize), solidTex);
        
        // Bottom-right
        GUI.DrawTexture(new Rect(w - margin - bracketSize, h - margin - bracketThick, bracketSize, bracketThick), solidTex);
        GUI.DrawTexture(new Rect(w - margin - bracketThick, h - margin - bracketSize, bracketThick, bracketSize), solidTex);
        
        // === VHS TIMESTAMP ===
        var timeStyle = S.S(GUI.skin.label, Mathf.RoundToInt(11 * uiScale),
            new Color(tapeOrange.r, tapeOrange.g, tapeOrange.b, 0.4f * alpha));
        int sec = System.DateTime.Now.Second;
        if (sec != _lastCachedSecond) { _lastCachedSecond = sec; _cachedTimestamp = L.Get("vhs_rec") + " \u25C9 " + System.DateTime.Now.ToString("HH:mm:ss"); }
        GUI.Label(new Rect(margin + 10, h - margin - 25, 150, 20), _cachedTimestamp, timeStyle);

        // Version info
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
        
        // Cassette body - dark background
        GUI.color = new Color(0.06f, 0.05f, 0.04f, alpha);
        GUI.DrawTexture(new Rect(x, y, iconW, iconH), solidTex);
        
        // Outer frame with rounded corners effect
        GUI.color = new Color(tapeOrange.r, tapeOrange.g, tapeOrange.b, 0.6f * alpha);
        GUI.DrawTexture(new Rect(x + 5, y, iconW - 10, thick), solidTex); // Top
        GUI.DrawTexture(new Rect(x + 5, y + iconH - thick, iconW - 10, thick), solidTex); // Bottom
        GUI.DrawTexture(new Rect(x, y + 5, thick, iconH - 10), solidTex); // Left
        GUI.DrawTexture(new Rect(x + iconW - thick, y + 5, thick, iconH - 10), solidTex); // Right
        
        // Corner accents
        GUI.DrawTexture(new Rect(x + 5, y + 5, 8, 2), solidTex);
        GUI.DrawTexture(new Rect(x + 5, y + 5, 2, 8), solidTex);
        GUI.DrawTexture(new Rect(x + iconW - 13, y + 5, 8, 2), solidTex);
        GUI.DrawTexture(new Rect(x + iconW - 7, y + 5, 2, 8), solidTex);
        
        // Tape window
        float winW = 70 * scale;
        float winH = 35 * scale;
        float winX = cx - winW / 2;
        float winY = cy - winH / 2 - 8 * scale;
        
        // Window background
        GUI.color = new Color(0.02f, 0.02f, 0.02f, alpha);
        GUI.DrawTexture(new Rect(winX, winY, winW, winH), solidTex);
        
        // Window frame
        GUI.color = new Color(dustyGray.r, dustyGray.g, dustyGray.b, 0.3f * alpha);
        GUI.DrawTexture(new Rect(winX, winY, winW, 1), solidTex);
        GUI.DrawTexture(new Rect(winX, winY + winH - 1, winW, 1), solidTex);
        GUI.DrawTexture(new Rect(winX, winY, 1, winH), solidTex);
        GUI.DrawTexture(new Rect(winX + winW - 1, winY, 1, winH), solidTex);
        
        // Animated reels
        float reelR = 12 * scale;
        float reelY = winY + winH / 2;
        float rot = noiseTime * 200f;
        
        DrawAnimatedReel(winX + 18 * scale, reelY, reelR, rot, alpha, scale);
        DrawAnimatedReel(winX + winW - 18 * scale, reelY, reelR, -rot * 0.9f, alpha, scale);
        
        // Tape between reels
        GUI.color = new Color(0.12f, 0.08f, 0.06f, alpha);
        GUI.DrawTexture(new Rect(winX + 18 * scale + reelR, reelY - 2, winW - 36 * scale - reelR * 2, 4 * scale), solidTex);
        
        // Label area
        float labelY = cy + 18 * scale;
        GUI.color = new Color(warmWhite.r, warmWhite.g, warmWhite.b, 0.08f * alpha);
        GUI.DrawTexture(new Rect(x + 15 * scale, labelY, iconW - 30 * scale, 18 * scale), solidTex);
        
        // Label text
        var labelStyle = S.S(GUI.skin.label, Mathf.RoundToInt(9 * scale),
            new Color(tapeOrange.r, tapeOrange.g, tapeOrange.b, 0.5f * alpha), TextAnchor.MiddleCenter);
        labelStyle.fontStyle = FontStyle.Bold;
        GUI.Label(new Rect(x + 15 * scale, labelY, iconW - 30 * scale, 18 * scale), L.Get("vhs_play"), labelStyle);
        
        // Decorative screws
        float screwSize = 6 * scale;
        GUI.color = new Color(dustyGray.r, dustyGray.g, dustyGray.b, 0.4f * alpha);
        GUI.DrawTexture(new Rect(x + 12, y + iconH - 18, screwSize, screwSize), solidTex);
        GUI.DrawTexture(new Rect(x + iconW - 18, y + iconH - 18, screwSize, screwSize), solidTex);
    }
    
    private void DrawAnimatedReel(float cx, float cy, float r, float rotation, float alpha, float scale)
    {
        // Outer ring
        GUI.color = new Color(dustyGray.r, dustyGray.g, dustyGray.b, 0.5f * alpha);
        for (int i = 0; i < 12; i++)
        {
            float angle = i * 30f * Mathf.Deg2Rad;
            float dx = Mathf.Cos(angle) * r;
            float dy = Mathf.Sin(angle) * r;
            GUI.DrawTexture(new Rect(cx + dx - 1.5f, cy + dy - 1.5f, 3, 3), solidTex);
        }
        
        // Center hub
        float hubR = 4 * scale;
        GUI.color = new Color(0.15f, 0.12f, 0.1f, alpha);
        GUI.DrawTexture(new Rect(cx - hubR, cy - hubR, hubR * 2, hubR * 2), solidTex);
        
        // Spokes (animated)
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
            
            // Simple spoke (as small rectangles)
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
