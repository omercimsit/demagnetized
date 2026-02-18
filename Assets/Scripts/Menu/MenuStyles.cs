using UnityEngine;

// shared menu styles and utilities for MainMenu, PauseMenu, and any other UI
// VHS tape / analog / industrial design theme
// single source of truth for palette, fonts, and drawing helpers
public static class MenuStyles
{
    // canonical UI palette - match these in any new UI, don't add new colors without good reason
    public static readonly Color Amber     = new Color(0.784f, 0.686f, 0.471f, 1f);
    public static readonly Color TextMain  = new Color(0.863f, 0.839f, 0.784f, 0.92f);
    public static readonly Color TextMid   = new Color(0.824f, 0.804f, 0.765f, 0.55f);
    public static readonly Color TextDim   = new Color(0.784f, 0.765f, 0.725f, 0.25f);
    public static readonly Color TextFaint = new Color(0.784f, 0.765f, 0.725f, 0.12f);
    public static readonly Color Danger    = new Color(0.765f, 0.333f, 0.294f, 1f);
    public static readonly Color BgDeep    = new Color(0.039f, 0.035f, 0.031f, 1f);

    // legacy aliases - kept so existing code doesn't break
    public static readonly Color TapeOrange = Amber;
    public static readonly Color RustRed = Danger;
    public static readonly Color MechanicGreen = new Color(0.3f, 0.9f, 0.55f);
    public static readonly Color CrtCyan = new Color(0.4f, 0.85f, 1f);
    public static readonly Color WarmWhite = new Color(0.98f, 0.98f, 0.95f);
    public static readonly Color DustyGray = new Color(0.5f, 0.5f, 0.55f);
    public static readonly Color MetalGray = new Color(0.35f, 0.35f, 0.4f);
    public static readonly Color DeepBlack = new Color(0.02f, 0.02f, 0.03f);
    public static readonly Color FilmBrown = new Color(0.06f, 0.06f, 0.08f);
    public static readonly Color DarkFilmBrown = new Color(0.04f, 0.04f, 0.05f);

    // clone personality colors - used by clone UI overlays
    public static readonly Color CloneHollow = new Color(0.5f, 0.5f, 0.5f);
    public static readonly Color CloneFearful = new Color(0f, 1f, 1f);
    public static readonly Color CloneBrave = new Color(1f, 0.2f, 0.2f);

    // VHS UI accents
    public static readonly Color VCRBlue = new Color(0.1f, 0.15f, 0.25f);
    public static readonly Color VCRTracking = new Color(1f, 1f, 1f, 0.05f);
    public static readonly Color PhosphorGreen = new Color(0.3f, 0.9f, 0.5f);
    public static readonly Color AmberOSD = new Color(1f, 0.8f, 0.4f);

    public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    // shared fonts, locale-aware, reloaded when language changes
    public static Font FontTitle   { get; private set; }  // BebasNeue-Regular
    public static Font FontBold    { get; private set; }  // IBMPlexMono-Bold
    public static Font FontRegular { get; private set; }  // IBMPlexMono-Regular
    public static Font FontLight   { get; private set; }  // IBMPlexMono-Light
    private static bool _fontsLoaded;
    private static bool _localeSubscribed;
    private static FontGroup _currentFontGroup = FontGroup.Latin;
    private static readonly string[] PreferredSystemFonts = { "Segoe UI", "Roboto", "Helvetica Neue" };

    private enum FontGroup { Latin, CJK_JP, CJK_KR, CJK_SC, CJK_TC, Arabic }

    private static FontGroup DetectFontGroup()
    {
        string code = Loc.CurrentLocaleCode;
        switch (code)
        {
            case "ja":      return FontGroup.CJK_JP;
            case "ko":      return FontGroup.CJK_KR;
            case "zh-Hans": return FontGroup.CJK_SC;
            case "zh-Hant": return FontGroup.CJK_TC;
            case "ar":      return FontGroup.Arabic;
            default:        return FontGroup.Latin;
        }
    }

    // load shared fonts for the current locale - safe to call multiple times
    public static void EnsureFonts()
    {
        if (!_localeSubscribed)
        {
            _localeSubscribed = true;
            Loc.OnLocaleChanged += OnLocaleChanged;
        }

        var group = DetectFontGroup();
        if (_fontsLoaded && group == _currentFontGroup) return;
        _currentFontGroup = group;
        _fontsLoaded = true;

        switch (group)
        {
            case FontGroup.CJK_JP: LoadCJKFonts("NotoSansCJKjp"); break;
            case FontGroup.CJK_KR: LoadCJKFonts("NotoSansCJKkr"); break;
            case FontGroup.CJK_SC: LoadCJKFonts("NotoSansCJKsc"); break;
            case FontGroup.CJK_TC: LoadCJKFonts("NotoSansCJKtc"); break;
            case FontGroup.Arabic:  LoadArabicFonts();             break;
            default:                LoadLatinFonts();              break;
        }

        // fallback chain - make sure nothing ends up null
        if (FontTitle == null) FontTitle = FontBold;
        if (FontLight == null) FontLight = FontRegular;
        if (FontBold == null)
        {
            string[] sysFonts = Font.GetOSInstalledFontNames();
            string best = "Arial";
            foreach (var s in sysFonts)
            {
                foreach (var pref in PreferredSystemFonts)
                    if (s.Contains(pref)) { best = s; goto found; }
            }
            found:
            FontBold = Font.CreateDynamicFontFromOSFont(best, 24);
            if (FontRegular == null) FontRegular = FontBold;
            if (FontTitle == null) FontTitle = FontBold;
            if (FontLight == null) FontLight = FontRegular;
        }
    }

    private static void LoadLatinFonts()
    {
        FontTitle   = Resources.Load<Font>("Fonts/BebasNeue-Regular");
        FontBold    = Resources.Load<Font>("Fonts/IBMPlexMono-Bold");
        FontRegular = Resources.Load<Font>("Fonts/IBMPlexMono-Regular");
        FontLight   = Resources.Load<Font>("Fonts/IBMPlexMono-Light");
    }

    private static void LoadCJKFonts(string familyPrefix)
    {
        // CJK body fonts use dynamic glyph loading since the atlas would be massive otherwise
        Font cjkRegular = Resources.Load<Font>($"Fonts/CJK/{familyPrefix}-Regular");
        if (cjkRegular != null)
        {
            FontRegular = cjkRegular;
            FontBold    = Resources.Load<Font>($"Fonts/CJK/{familyPrefix}-Bold") ?? cjkRegular;
            FontLight   = cjkRegular;
            FontTitle   = Resources.Load<Font>("Fonts/BebasNeue-Regular"); // game title stays latin
        }
        else
        {
            LoadLatinFonts();
        }
    }

    private static void LoadArabicFonts()
    {
        Font arabicRegular = Resources.Load<Font>("Fonts/Arabic/NotoSansArabic-Regular");
        if (arabicRegular != null)
        {
            FontRegular = arabicRegular;
            FontBold    = Resources.Load<Font>("Fonts/Arabic/NotoSansArabic-Bold") ?? arabicRegular;
            FontLight   = arabicRegular;
            FontTitle   = Resources.Load<Font>("Fonts/BebasNeue-Regular");
        }
        else
        {
            LoadLatinFonts();
        }
    }

    private static void OnLocaleChanged()
    {
        _fontsLoaded = false;
        _stylesValid = false;
        // next EnsureStyles() call will reload fonts for the new locale
    }

    // shared base styles, cached and recreated on screen resize
    public static GUIStyle StyleTitle { get; private set; }
    public static GUIStyle StyleBody  { get; private set; }
    public static GUIStyle StyleBold  { get; private set; }
    public static GUIStyle StyleLight { get; private set; }
    public static GUIStyle StyleHud   { get; private set; }
    private static bool _stylesValid;
    private static int _cachedW, _cachedH;

    // call once per OnGUI frame before using S() or Style* properties
    public static void EnsureStyles()
    {
        EnsureFonts();
        ResetStylePool();
        if (_stylesValid && _cachedW == Screen.width && _cachedH == Screen.height) return;
        _cachedW = Screen.width;
        _cachedH = Screen.height;

        StyleTitle = new GUIStyle { font = FontTitle, alignment = TextAnchor.MiddleCenter };
        StyleTitle.normal.textColor = Color.white;

        StyleBody = new GUIStyle { font = FontRegular, alignment = TextAnchor.MiddleLeft };
        StyleBody.normal.textColor = TextMain;

        StyleBold = new GUIStyle { font = FontBold, alignment = TextAnchor.MiddleLeft };
        StyleBold.normal.textColor = TextMain;

        StyleLight = new GUIStyle { font = FontLight, alignment = TextAnchor.MiddleLeft };
        StyleLight.normal.textColor = TextMid;

        StyleHud = new GUIStyle { font = FontRegular, alignment = TextAnchor.MiddleLeft };
        StyleHud.normal.textColor = TextDim;

        _stylesValid = true;
    }

    // zero-GC style pool - eliminates ~50+ heap allocations per frame from new GUIStyle() calls
    // pool resets each OnGUI pass, never store references across frames
    private static readonly GUIStyle[] _stylePool = new GUIStyle[80];
    private static int _poolIdx;

    public static void ResetStylePool() { _poolIdx = 0; }

    // returns a pooled GUIStyle with the given size, color, and alignment
    // callers can modify fontStyle, wordWrap, clipping before drawing
    public static GUIStyle S(GUIStyle baseStyle, int size, Color col, TextAnchor align = TextAnchor.MiddleLeft)
    {
        int i = _poolIdx % _stylePool.Length;
        _poolIdx++;

        var s = _stylePool[i];
        if (s == null) { s = new GUIStyle(); _stylePool[i] = s; }

        s.font = baseStyle.font;
        s.fontSize = size;
        s.normal.textColor = col;
        s.alignment = align;
        s.fontStyle = FontStyle.Normal;
        s.wordWrap = false;
        s.clipping = TextClipping.Clip;
        s.richText = false;
        return s;
    }

    // 1x1 white texture, cached after first use
    private static Texture2D _solidTex;
    public static Texture2D SolidTexture
    {
        get
        {
            if (_solidTex == null)
            {
                _solidTex = new Texture2D(1, 1);
                _solidTex.SetPixel(0, 0, Color.white);
                _solidTex.Apply();
            }
            return _solidTex;
        }
    }

    private static Texture2D _noiseTex;
    public static Texture2D NoiseTexture
    {
        get
        {
            if (_noiseTex == null)
            {
                // 128x128 noise texture shared with VHS effects
                _noiseTex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
                Color32[] pixels = new Color32[128 * 128];
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte n = (byte)Random.Range(0, 52);
                    pixels[i] = new Color32(n, n, n, n);
                }
                _noiseTex.SetPixels32(pixels);
                _noiseTex.Apply();
            }
            return _noiseTex;
        }
    }

    private static Texture2D _gradientTex;
    // left-to-right gradient (opaque left, transparent right) for panel overlays
    // 256x1 texture with quintic ease, cached after first generation
    public static Texture2D GradientTexture
    {
        get
        {
            if (_gradientTex == null)
            {
                int width = 256;
                _gradientTex = new Texture2D(width, 1, TextureFormat.ARGB32, false);
                _gradientTex.wrapMode = TextureWrapMode.Clamp;
                _gradientTex.filterMode = FilterMode.Bilinear;
                var pixels = new Color32[width];
                for (int x = 0; x < width; x++)
                {
                    float t = (float)x / (width - 1);
                    float a;
                    if (t <= 0.28f) a = 0.96f;
                    else
                    {
                        float fadeT = (t - 0.28f) / 0.72f;
                        float ease = 1f - Mathf.Pow(1f - fadeT, 5f);
                        a = (1f - ease) * 0.96f;
                    }
                    byte ab = (byte)(a * 255);
                    pixels[x] = new Color32(255, 255, 255, ab);
                }
                _gradientTex.SetPixels32(pixels);
                _gradientTex.Apply();
            }
            return _gradientTex;
        }
    }

    // draw a mechanical rivet/bolt decoration
    public static void DrawRivet(float x, float y, float alpha, Texture2D tex = null)
    {
        tex = tex ?? SolidTexture;
        GUI.color = new Color(MetalGray.r, MetalGray.g, MetalGray.b, 0.6f * alpha);
        GUI.DrawTexture(new Rect(x - 4, y - 4, 8, 8), tex);
        GUI.color = new Color(DustyGray.r, DustyGray.g, DustyGray.b, 0.4f * alpha);
        GUI.DrawTexture(new Rect(x - 2, y - 2, 4, 4), tex);
    }

    // draw a VHS tape reel with rotation
    public static void DrawTapeReel(float cx, float cy, float r, float rotation, float alpha, Texture2D tex = null)
    {
        tex = tex ?? SolidTexture;

        GUI.color = new Color(MetalGray.r, MetalGray.g, MetalGray.b, 0.3f * alpha);
        for (int i = 0; i < 24; i++)
        {
            float angle = i * (Mathf.PI * 2 / 24) + rotation * Mathf.Deg2Rad;
            float px = cx + Mathf.Cos(angle) * r;
            float py = cy + Mathf.Sin(angle) * r;
            GUI.DrawTexture(new Rect(px - 2, py - 2, 4, 4), tex);
        }

        GUI.color = new Color(DeepBlack.r, DeepBlack.g, DeepBlack.b, 0.5f * alpha);
        GUI.DrawTexture(new Rect(cx - r * 0.3f, cy - r * 0.3f, r * 0.6f, r * 0.6f), tex);

        GUI.color = new Color(TapeOrange.r, TapeOrange.g, TapeOrange.b, 0.25f * alpha);
        for (int i = 0; i < 3; i++)
        {
            float angle = i * (Mathf.PI * 2 / 3) + rotation * Mathf.Deg2Rad;
            float px1 = cx + Mathf.Cos(angle) * r * 0.2f;
            float py1 = cy + Mathf.Sin(angle) * r * 0.2f;
            float px2 = cx + Mathf.Cos(angle) * r * 0.85f;
            float py2 = cy + Mathf.Sin(angle) * r * 0.85f;
            DrawLine(px1, py1, px2, py2, 2, tex);
        }
    }

    public static void DrawLine(float x1, float y1, float x2, float y2, float thickness, Texture2D tex = null)
    {
        tex = tex ?? SolidTexture;
        Vector2 dir = new Vector2(x2 - x1, y2 - y1);
        float len = dir.magnitude;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        Matrix4x4 matrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, new Vector2(x1, y1));
        GUI.DrawTexture(new Rect(x1, y1 - thickness/2, len, thickness), tex);
        GUI.matrix = matrix;
    }

    public static void DrawScanlines(float width, float height, float offset, float alpha, Texture2D tex = null)
    {
        tex = tex ?? SolidTexture;
        GUI.color = new Color(0f, 0f, 0f, 0.04f * alpha);
        for (float y = (offset % 4); y < height; y += 4)
        {
            GUI.DrawTexture(new Rect(0, y, width, 1), tex);
        }
    }

    // corner brackets in film frame style
    public static void DrawCornerBrackets(float margin, float size, float thickness, float alpha, Texture2D tex = null)
    {
        tex = tex ?? SolidTexture;
        float w = Screen.width;
        float h = Screen.height;

        GUI.color = new Color(TapeOrange.r, TapeOrange.g, TapeOrange.b, 0.35f * alpha);

        GUI.DrawTexture(new Rect(margin, margin, size, thickness), tex);
        GUI.DrawTexture(new Rect(margin, margin, thickness, size), tex);

        GUI.DrawTexture(new Rect(w - margin - size, margin, size, thickness), tex);
        GUI.DrawTexture(new Rect(w - margin - thickness, margin, thickness, size), tex);

        GUI.DrawTexture(new Rect(margin, h - margin - thickness, size, thickness), tex);
        GUI.DrawTexture(new Rect(margin, h - margin - size, thickness, size), tex);

        GUI.DrawTexture(new Rect(w - margin - size, h - margin - thickness, size, thickness), tex);
        GUI.DrawTexture(new Rect(w - margin - thickness, h - margin - size, thickness, size), tex);
    }

    public static void DrawFilmGrain(Rect area, float time, float alpha, Texture2D noiseTex = null)
    {
        noiseTex = noiseTex ?? NoiseTexture;
        if (noiseTex == null) return;

        GUI.color = new Color(1f, 1f, 1f, 0.08f * alpha);
        float noiseScale = 3f;
        GUI.DrawTextureWithTexCoords(
            area,
            noiseTex,
            new Rect(time * 0.5f, time * 0.3f, noiseScale, noiseScale * (area.height / area.width))
        );
    }

    public static void DrawGlitchLine(float width, float alpha, Texture2D tex = null)
    {
        tex = tex ?? SolidTexture;
        float glitchY = Random.Range(0f, Screen.height);

        GUI.color = new Color(TapeOrange.r, TapeOrange.g, TapeOrange.b, 0.1f * alpha);
        GUI.DrawTexture(new Rect(0, glitchY, width, Random.Range(5f, 20f)), tex);

        GUI.color = new Color(CrtCyan.r, CrtCyan.g, CrtCyan.b, 0.05f * alpha);
        GUI.DrawTexture(new Rect(Random.Range(-10f, 0f), glitchY + 10, width + 10, Random.Range(2f, 8f)), tex);
    }

    public static void DrawTrackingLines(float w, float h, float offset, float alpha)
    {
        alpha *= 0.3f;

        for (int i = 0; i < 3; i++)
        {
            float lineY = ((offset + i * 70) % (h + 50)) - 25;
            float lineHeight = 2 + Random.Range(0f, 3f);
            float lineAlpha = alpha * (0.5f + Random.Range(0f, 0.5f));

            GUI.color = new Color(VCRTracking.r, VCRTracking.g, VCRTracking.b, lineAlpha);
            GUI.DrawTexture(new Rect(0, lineY, w, lineHeight), SolidTexture);

            GUI.color = new Color(CrtCyan.r, CrtCyan.g, CrtCyan.b, lineAlpha * 0.3f);
            GUI.DrawTexture(new Rect(0, lineY - 2, w, 1), SolidTexture);

            GUI.color = new Color(TapeOrange.r, TapeOrange.g, TapeOrange.b, lineAlpha * 0.2f);
            GUI.DrawTexture(new Rect(0, lineY + lineHeight + 1, w, 1), SolidTexture);
        }

        // occasional horizontal jitter
        if (Random.value < 0.1f)
        {
            float jitterY = Random.Range(0f, h);
            GUI.color = new Color(1f, 1f, 1f, 0.05f * alpha);
            GUI.DrawTexture(new Rect(Random.Range(-5f, 5f), jitterY, w, 1), SolidTexture);
        }
    }

    // small L-shaped corner accent for panels
    public static void DrawCornerAccent(float x, float y, float size, float alpha)
    {
        GUI.color = new Color(TapeOrange.r, TapeOrange.g, TapeOrange.b, 0.4f * alpha);
        GUI.DrawTexture(new Rect(x, y, size, 2), SolidTexture);
        GUI.DrawTexture(new Rect(x, y, 2, size), SolidTexture);
    }

    // dark vignette around screen edges
    // bands = quality (8 = fast, 15 = smooth), edgeFraction = how deep into the screen
    public static void DrawVignette(float w, float h, float strength = 1f, int bands = 8, float edgeFraction = 0.22f)
    {
        float edgeW = w * edgeFraction;
        float edgeH = h * (edgeFraction * 0.82f);
        float sw = edgeW / bands;
        float sh = edgeH / bands;

        for (int i = 0; i < bands; i++)
        {
            float a = (1f - (float)i / bands) * 0.25f * strength;
            GUI.color = new Color(0f, 0f, 0f, a);
            GUI.DrawTexture(new Rect(i * sw, 0, sw + 1, h), SolidTexture);
            GUI.DrawTexture(new Rect(w - (i + 1) * sw, 0, sw + 1, h), SolidTexture);
            GUI.DrawTexture(new Rect(0, i * sh, w, sh + 1), SolidTexture);
            GUI.DrawTexture(new Rect(0, h - (i + 1) * sh, w, sh + 1), SolidTexture);
        }
        GUI.color = Color.white;
    }

    // smooth step (ease in-out)
    public static float SmoothStep(float from, float to, float t)
    {
        t = Mathf.Clamp01(t);
        t = t * t * (3f - 2f * t);
        return Mathf.Lerp(from, to, t);
    }

    // ease out (decelerate)
    public static float EaseOut(float from, float to, float t)
    {
        t = Mathf.Clamp01(t);
        t = 1f - (1f - t) * (1f - t);
        return Mathf.Lerp(from, to, t);
    }

    // ease in (accelerate)
    public static float EaseIn(float from, float to, float t)
    {
        t = Mathf.Clamp01(t);
        t = t * t;
        return Mathf.Lerp(from, to, t);
    }

    // call on application quit to clean up cached textures
    public static void Cleanup()
    {
        if (_solidTex != null)
        {
            Object.Destroy(_solidTex);
            _solidTex = null;
        }
        if (_noiseTex != null)
        {
            Object.Destroy(_noiseTex);
            _noiseTex = null;
        }
        if (_gradientTex != null)
        {
            Object.Destroy(_gradientTex);
            _gradientTex = null;
        }
        _stylesValid = false;
        _fontsLoaded = false;
        if (_localeSubscribed)
        {
            Loc.OnLocaleChanged -= OnLocaleChanged;
            _localeSubscribed = false;
        }
    }
}
