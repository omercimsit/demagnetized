using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// DEMAGNETIZED - Cinematic Main Menu (HTML v4 port)
/// VHS tape aesthetic with live background, split-panel settings, DLSS integration.
/// </summary>
[DefaultExecutionOrder(-100)]
public class DemagnetizedMainMenu : MonoBehaviour
{
    #region Singleton
    private static DemagnetizedMainMenu _instance;
    public static DemagnetizedMainMenu Instance => _instance;
    #endregion

    #region Enums
    private enum MenuPanel { Splash, TitleCard, MainMenu, Settings, Credits, NewGameConfirm, QuitConfirm }
    private enum SettingsTab { Audio, Video, Controls, Accessibility }
    #endregion

    #region Color Palette (aliases into MenuStyles canonical palette)
    private static Color CAmber  => MenuStyles.Amber;
    private static Color CText   => MenuStyles.TextMain;
    private static Color CText60 => MenuStyles.TextMid;
    private static Color CText30 => MenuStyles.TextDim;
    private static Color CText15 => MenuStyles.TextFaint;
    private static Color CDanger => MenuStyles.Danger;
    private static Color CBg     => MenuStyles.BgDeep;
    #endregion

    #region Static Data (allocated once, not per-frame)
    // Localized arrays — rebuilt when language changes (cached per-frame via OnGUI)
    private string[] _catNames, _catDescs, _qualityNames;
    private string[] _subSizes;
    private string[] _cbModes;
    private string[] _fpsNames;
    private void RefreshLocalizedArrays()
    {
        _catNames = new[] { L.Get("tab_audio"), L.Get("tab_video"), L.Get("tab_controls"), L.Get("tab_accessibility") };
        _catDescs = new[] { L.Get("audio"), L.Get("display"), L.Get("tab_controls"), L.Get("tab_accessibility") };
        _qualityNames = new[] { L.Get("quality_low"), L.Get("quality_medium"), L.Get("quality_high") };
        _subSizes = new[] { L.Get("size_small"), L.Get("size_medium"), L.Get("size_large") };
        _cbModes = new[] { L.Get("cb_none"), L.Get("cb_deuteranopia"), L.Get("cb_protanopia"), L.Get("cb_tritanopia") };
        _fpsNames = new[] { "30", "60", "120", L.Get("off") };
    }
    private static readonly string[] DlssNames = { "TAA", "DLAA", "QUAL", "BAL", "PERF", "ULTRA" };
    private static readonly int[] FpsValues = { 30, 60, 120, -1 };
    private static readonly string[][] Credits = {
        new[] { "credits_created_by", "Omer C." },
        new[] { "credits_game_design", "Omer C." },
        new[] { "credits_programming", "Omer C." },
        new[] { "credits_narrative", "Omer C." },
        new[] { "credits_env_art", "Omer C." },
        new[] { "credits_char_anim", "KINEMATION by Kinemation Studio" },
        new[] { "credits_audio_design", "Omer C." },
        new[] { "credits_special_thanks", "credits_thanks_names" },
        new[] { "", "credits_you_playing" },
    };
    #endregion

    #region Serialized Fields
    [Header("Scene")]
    [SerializeField] private string gameSceneName = "AbandonedFactory";
    [SerializeField] private string backgroundSceneName = "HDRP_TheCarnival";

    [Header("Background Optimization")]
    [Tooltip("Baked cubemap from Camera7. If assigned, the full scene is NOT loaded — huge perf win.")]
    [SerializeField] private Cubemap bakedBackgroundCubemap;
    [Tooltip("Camera7's Euler rotation at bake time. Sets the initial view direction for the cubemap sky.")]
    [SerializeField] private Vector3 bakedCameraRotation;

    [Header("Video")]
    [SerializeField] private VideoClip splashVideo;

    [Header("Fonts")]
    [SerializeField] private Font fontTitle;
    [SerializeField] private Font fontBold;
    [SerializeField] private Font fontRegular;
    [SerializeField] private Font fontLight;

    [Header("Audio")]
    [SerializeField] private AudioClip ambientTapeHiss;
    [SerializeField] private AudioClip sfxHover;
    [SerializeField] private AudioClip sfxSelect;
    [SerializeField] private AudioClip sfxBack;

    [Header("Parallax")]
    [SerializeField] private float parallaxAmount = 1.5f;
    [SerializeField] private float parallaxSmooth = 3f;
    #endregion

    #region Panel State
    private MenuPanel _panel = MenuPanel.Splash;
    private MenuPanel _targetPanel = MenuPanel.Splash;
    private float _panelAlpha = 1f;
    private float _panelSlide = 0f;
    private float _transTimer = 0f;
    private bool _transActive = false;
    private float _blackout = 0f;
    private const float TRANS_DUR = 0.45f;
    private const float SENSITIVITY_MIN = 0.1f;
    private const float SENSITIVITY_MAX = 2f;
    private const float FOV_MIN = 60f;
    private const float FOV_MAX = 120f;
    private const float BAR_HEIGHT_PCT = 0.065f;
    #endregion

    #region Cinematic Bars
    private float _barH = 0f;
    private float _barTarget = 0f;
    #endregion

    #region Splash
    private VideoPlayer _vp;
    private RenderTexture _vpRT;
    private bool _videoEnded = false;
    private bool _splashPhase2 = false; // after video, showing bg
    #endregion

    #region Title Card
    private float _tcTimer = 0f;
    #endregion

    #region Main Menu
    private int _menuSel = 0;
    private bool _hasSave = false;
    private float _menuTimer = 0f;
    private bool _staggerDone = false;

    private struct MItem
    {
        public string text, subtitle;
        public bool locked;
        public Action action;
    }
    private List<MItem> _items = new List<MItem>();
    #endregion

    #region Settings
    private SettingsTab _sTab = SettingsTab.Audio;
    private int _sTabIdx = 0;
    private float _sTabFade = 0f;
    // Audio
    private float _sMaster, _sMusic, _sSfx, _sAmbient;
    // Video
    private Resolution[] _resArr;
    private int _resIdx;
    private bool _sFullscreen, _sVsync;
    private int _sQuality;
    private int _sDlssMode;
    private float _sDlssSharp;
    private bool _dlssOk;
    private bool _sBloom, _sSsao, _sVolFog, _sFilmGrain, _sVignette, _sMotionBlur;
    private int _sFpsLimit; // 0=30,1=60,2=120,3=Off
    // Controls
    private float _sSens;
    private bool _sInvertY;
    private float _sFov;
    // Accessibility
    private int _sLang;
    private int _sSubSize;
    private int _sColorblind;
    // Widget state
    private bool _dragging = false;
    private int _dragID = -1;
    private int _sliderCounter = 0;
    private Vector2 _settingsScroll = Vector2.zero;
    #endregion

    #region Credits
    private float _credScroll = 0f;
    private bool _credHover = false;
    #endregion

    #region Confirm
    private int _confirmBtn = 0; // 0=cancel, 1=action
    #endregion

    #region VHS Effects
    private float _t = 0f; // global time
    private float _scanY = 0f;
    private float _vigBreath = 0f;
    private float _glitchCD = 0f;

    // Cached strings (avoid per-frame GC in OnGUI)
    private int _lastRecTs = -1;
    private string _cachedRecStr;
    private float _glitchDur = 0f;
    private bool _glitchOn = false;
    private float _idleTime = 0f;
    private float _recDropCD = 0f;
    private bool _recShow = true;

    private struct Ptcl { public float x, y, spd, sz, a; }
    private Ptcl[] _ptcls = new Ptcl[22];
    #endregion

    #region Audio
    private AudioSource _ambSrc;
    private AudioSource _sfxSrc;
    private bool _hissStarted = false;
    #endregion

    #region Background
    private bool _bgLoading = false;
    private bool _bgLoaded = false;
    private Quaternion _camRot0;
    private Camera _cachedCam;
    private MainMenuScenePreloader _preloader;
    private GameObject _menuSkyGO;
    #endregion

    #region Styles (local aliases into MenuStyles shared cache)
    private GUIStyle _stTitle => MenuStyles.StyleTitle;
    private GUIStyle _stBody  => MenuStyles.StyleBody;
    private GUIStyle _stBold  => MenuStyles.StyleBold;
    private GUIStyle _stLight => MenuStyles.StyleLight;
    private GUIStyle _stHud   => MenuStyles.StyleHud;
    #endregion

    // ───────────────────────────── LIFECYCLE ─────────────────────────────

    #region Lifecycle

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;

        var cam = Camera.main;
        if (cam != null)
        {
            // Use Skybox so HDRP volumes from the bg scene render properly
            cam.clearFlags = CameraClearFlags.Skybox;

            // Ensure we have an AudioListener
            if (!cam.TryGetComponent<AudioListener>(out _))
                cam.gameObject.AddComponent<AudioListener>();
        }
    }

    private void Start()
    {
        GameBootstrap.EnsureCoreServices();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        LoadFonts();
        SetupAudio();
        SetupVideo();
        LoadSettings();
        InitParticles();
        RefreshLocalizedArrays();
        LocalizationManager.OnLanguageChanged += _ => RefreshLocalizedArrays();
        Loc.OnLocaleChanged += () => { _localeNames = Loc.GetLocaleNames(); RefreshLocalizedArrays(); };
        _hasSave = PlayerPrefs.HasKey("SaveExists");
        BuildMenu();

        _preloader = new MainMenuScenePreloader(this, gameSceneName);
        // Preload starts after background scene loads (see LoadBgScene) to avoid I/O contention

        _cachedCam = Camera.main;
        if (_cachedCam != null)
            _camRot0 = _cachedCam.transform.rotation;

        MenuInput.Reset();
        _panelAlpha = 1f;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        _t += dt;

        UpdateTransition(dt);
        UpdateVHS(dt);
        UpdateParticles(dt);

        _barH = Mathf.MoveTowards(_barH, _barTarget, dt * Screen.height * 0.12f);

        if (_bgLoaded && _cachedCam != null)
            UpdateParallax(dt);

        if (_panel == MenuPanel.MainMenu)
            _menuTimer += dt;

        if (_panel == MenuPanel.Settings)
            _sTabFade += dt;

        // Auto-transition: after intro video ends and bg is loaded, go to TitleCard
        if (_panel == MenuPanel.Splash && _splashPhase2 && _bgLoaded && !_transActive)
            GoTo(MenuPanel.TitleCard);

        if (!_transActive)
            HandleInput(dt);

        if (_dragging && !Input.GetMouseButton(0))
        {
            _dragging = false;
            _dragID = -1;
        }
    }

    private void OnGUI()
    {
        EnsureStyles();
        _sliderCounter = 0;

        DrawBG();

        float a = _panelAlpha;
        float sl = _panelSlide;

        switch (_panel)
        {
            case MenuPanel.Splash: DrawSplash(a); break;
            case MenuPanel.TitleCard: DrawTitleCard(a, sl); break;
            case MenuPanel.MainMenu: DrawMainMenu(a, sl); break;
            case MenuPanel.Settings: DrawSettingsPanel(a, sl); break;
            case MenuPanel.Credits: DrawCreditsPanel(a, sl); break;
            case MenuPanel.NewGameConfirm: DrawConfirm(a, sl, true); break;
            case MenuPanel.QuitConfirm: DrawConfirm(a, sl, false); break;
        }

        if (_barH > 0.5f) DrawBars();

        if (_panel != MenuPanel.Splash || _splashPhase2)
            DrawVHSFX();

        if (_panel >= MenuPanel.MainMenu)
            DrawHUD(a);

        // Blackout overlay
        if (_blackout > 0.01f)
        {
            GUI.color = new Color(0, 0, 0, _blackout);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), MenuStyles.SolidTexture);
            GUI.color = Color.white;
        }
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        if (_vpRT != null) { _vpRT.Release(); Destroy(_vpRT); }
        if (_vp != null) _vp.loopPointReached -= OnVideoEnd;
        if (_menuSkyGO != null)
        {
            var vol = _menuSkyGO.GetComponent<Volume>();
            if (vol != null && vol.profile != null) Destroy(vol.profile);
            Destroy(_menuSkyGO);
        }
        MenuStyles.Cleanup();
    }

    #endregion

    // ───────────────────────────── FONTS & STYLES ─────────────────────────────

    #region Fonts & Styles

    private void LoadFonts()
    {
        MenuStyles.EnsureFonts();
        // Use serialized overrides if set in Inspector, otherwise shared fonts
        if (fontTitle == null) fontTitle = MenuStyles.FontTitle;
        if (fontBold == null) fontBold = MenuStyles.FontBold;
        if (fontRegular == null) fontRegular = MenuStyles.FontRegular;
        if (fontLight == null) fontLight = MenuStyles.FontLight;
    }

    private void EnsureStyles()
    {
        MenuStyles.EnsureStyles();
    }

    // Lightweight per-frame style derivative (delegates to MenuStyles.S)
    private GUIStyle S(GUIStyle baseStyle, int size, Color col, TextAnchor align = TextAnchor.MiddleLeft)
        => MenuStyles.S(baseStyle, size, col, align);

    #endregion

    // ───────────────────────────── TRANSITIONS ─────────────────────────────

    #region Transitions

    private void GoTo(MenuPanel p)
    {
        if (_transActive || p == _panel) return;
        _targetPanel = p;
        _transActive = true;
        _transTimer = 0f;
    }

    private void UpdateTransition(float dt)
    {
        if (!_transActive)
        {
            _panelAlpha = Mathf.MoveTowards(_panelAlpha, 1f, dt * 3f);
            _panelSlide = Mathf.MoveTowards(_panelSlide, 0f, dt * 80f);
            return;
        }

        _transTimer += dt;
        float half = TRANS_DUR * 0.5f;

        if (_transTimer < half)
        {
            float t = _transTimer / half;
            _blackout = t;
            _panelAlpha = 1f - t;
        }
        else if (_transTimer < TRANS_DUR)
        {
            if (_panel != _targetPanel)
            {
                _panel = _targetPanel;
                _panelSlide = 20f;
                _panelAlpha = 0f;
                OnEnter(_panel);
            }
            float t = (_transTimer - half) / half;
            _blackout = 1f - t;
            _panelAlpha = t;
            _panelSlide = Mathf.Lerp(20f, 0f, t);
        }
        else
        {
            _transActive = false;
            _blackout = 0f;
            _panelAlpha = 1f;
            _panelSlide = 0f;
        }
    }

    private void OnEnter(MenuPanel p)
    {
        switch (p)
        {
            case MenuPanel.TitleCard:
                _tcTimer = 0f;
                _barTarget = Screen.height * BAR_HEIGHT_PCT;
                StartHiss();
                break;
            case MenuPanel.MainMenu:
                _menuTimer = 0f;
                _staggerDone = false;
                _menuSel = 0;
                BuildMenu();
                break;
            case MenuPanel.Settings:
                _sTab = SettingsTab.Audio;
                _sTabIdx = 0;
                _sTabFade = 0f;
                _settingsScroll = Vector2.zero;
                LoadSettings();
                InitResolutions();
                CheckDLSS();
                break;
            case MenuPanel.Credits:
                _credScroll = 0f;
                break;
            case MenuPanel.NewGameConfirm:
            case MenuPanel.QuitConfirm:
                _confirmBtn = 0;
                break;
        }
    }

    #endregion

    // ───────────────────────────── INPUT ─────────────────────────────

    #region Input

    private void HandleInput(float dt)
    {
        bool any = Input.anyKeyDown || Input.GetMouseButtonDown(0);
        if (any) _idleTime = 0f;
        else _idleTime += dt;

        switch (_panel)
        {
            case MenuPanel.Splash:
                // Video skip: press any key to skip intro video
                if (any && !_videoEnded && !_splashPhase2)
                {
                    if (_vp != null) _vp.Stop();
                    _videoEnded = true;
                    _splashPhase2 = true;
                    if (!_bgLoaded && !_bgLoading) StartCoroutine(LoadBgScene());
                }
                // Auto-transition to TitleCard handled in Update()
                break;

            case MenuPanel.TitleCard:
                _tcTimer += dt;
                if (_tcTimer > 1.5f && any)
                {
                    PlaySFX(sfxSelect);
                    GoTo(MenuPanel.MainMenu);
                }
                break;

            case MenuPanel.MainMenu:
                if (!_staggerDone) return;
                if (MenuInput.Down)
                {
                    int o = _menuSel;
                    do { _menuSel = (_menuSel + 1) % _items.Count; }
                    while (_items[_menuSel].locked && _menuSel != o);
                    if (o != _menuSel) PlaySFX(sfxHover);
                }
                if (MenuInput.Up)
                {
                    int o = _menuSel;
                    do { _menuSel = (_menuSel - 1 + _items.Count) % _items.Count; }
                    while (_items[_menuSel].locked && _menuSel != o);
                    if (o != _menuSel) PlaySFX(sfxHover);
                }
                if (MenuInput.Confirm)
                {
                    var it = _items[_menuSel];
                    if (!it.locked && it.action != null)
                    {
                        PlaySFX(sfxSelect);
                        it.action();
                    }
                }
                break;

            case MenuPanel.Settings:
                if (MenuInput.Cancel)
                {
                    PlaySFX(sfxBack);
                    SaveSettings();
                    GoTo(MenuPanel.MainMenu);
                }
                // Up/Down navigates categories (per HTML spec)
                if (MenuInput.Up && _sTabIdx > 0)
                {
                    _sTabIdx--;
                    _sTab = (SettingsTab)_sTabIdx;
                    _sTabFade = 0f;
                    _settingsScroll = Vector2.zero;
                    PlaySFX(sfxHover);
                }
                if (MenuInput.Down && _sTabIdx < 3)
                {
                    _sTabIdx++;
                    _sTab = (SettingsTab)_sTabIdx;
                    _sTabFade = 0f;
                    _settingsScroll = Vector2.zero;
                    PlaySFX(sfxHover);
                }
                break;

            case MenuPanel.Credits:
                if (MenuInput.Cancel)
                {
                    PlaySFX(sfxBack);
                    GoTo(MenuPanel.MainMenu);
                }
                break;

            case MenuPanel.NewGameConfirm:
            case MenuPanel.QuitConfirm:
                if (MenuInput.Left || MenuInput.Right)
                {
                    _confirmBtn = 1 - _confirmBtn;
                    PlaySFX(sfxHover);
                }
                if (MenuInput.Cancel)
                {
                    PlaySFX(sfxBack);
                    GoTo(MenuPanel.MainMenu);
                }
                if (MenuInput.Confirm)
                {
                    if (_confirmBtn == 0) { PlaySFX(sfxBack); GoTo(MenuPanel.MainMenu); }
                    else
                    {
                        PlaySFX(sfxSelect);
                        if (_panel == MenuPanel.NewGameConfirm) DoNewGame();
                        else DoQuit();
                    }
                }
                break;
        }
    }

    #endregion

    // ───────────────────────────── DRAW: BACKGROUND ─────────────────────────────

    #region Background Drawing

    private void DrawBG()
    {
        bool showLiveBg = _bgLoaded && (_panel != MenuPanel.Splash || _splashPhase2);

        // Only draw opaque background when live bg scene isn't rendering behind us
        if (!showLiveBg)
        {
            GUI.color = CBg;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), MenuStyles.SolidTexture);
            GUI.color = Color.white;
        }

        // Splash video
        if (_panel == MenuPanel.Splash && !_videoEnded && _vpRT != null && _vp != null && _vp.isPlaying)
        {
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _vpRT, ScaleMode.ScaleAndCrop);
        }

        // Dim overlay over live bg (lets camera render show through)
        if (showLiveBg)
        {
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), MenuStyles.SolidTexture);
            GUI.color = Color.white;
            DrawEdgeFalloff();
        }
    }

    private void DrawEdgeFalloff()
    {
        float w = Screen.width, h = Screen.height;
        int n = 10;
        float ew = w * 0.2f, eh = h * 0.15f;

        for (int i = 0; i < n; i++)
        {
            float a = (1f - (float)i / n) * 0.3f;
            float sw = ew / n;
            GUI.color = new Color(0, 0, 0, a);
            GUI.DrawTexture(new Rect(i * sw, 0, sw + 1, h), MenuStyles.SolidTexture);
            GUI.DrawTexture(new Rect(w - (i + 1) * sw, 0, sw + 1, h), MenuStyles.SolidTexture);
        }
        for (int i = 0; i < n; i++)
        {
            float a = (1f - (float)i / n) * 0.25f;
            float sh = eh / n;
            GUI.color = new Color(0, 0, 0, a);
            GUI.DrawTexture(new Rect(0, i * sh, w, sh + 1), MenuStyles.SolidTexture);
            GUI.DrawTexture(new Rect(0, h - (i + 1) * sh, w, sh + 1), MenuStyles.SolidTexture);
        }
        GUI.color = Color.white;
    }

    #endregion

    // ───────────────────────────── DRAW: SPLASH ─────────────────────────────

    #region Splash

    private void DrawSplash(float a)
    {
        if ((_splashPhase2 || _videoEnded) && !_bgLoaded)
        {
            float h = Screen.height, w = Screen.width;
            float dotA = Mathf.Sin(_t * 3f) * 0.3f + 0.5f;
            var stLoad = S(_stLight, Sz(0.014f), ColA(CText30, a * dotA), TextAnchor.MiddleCenter);
            GUI.Label(new Rect(0, h * 0.52f, w, Sz(0.03f)), L.Get("loading") + "...", stLoad);
        }
    }

    #endregion

    // ───────────────────────────── DRAW: TITLE CARD ─────────────────────────────

    #region Title Card

    private void DrawTitleCard(float a, float sl)
    {
        float w = Screen.width, h = Screen.height;
        float titleA = Mathf.Clamp01(_tcTimer / 1.5f) * a;
        float chromOff = Mathf.Lerp(8f, 0f, Mathf.Clamp01(_tcTimer / 1.2f));

        int titleSz = Sz(0.09f);
        float cy = h * 0.38f + sl; // vertically centered

        // Chromatic aberration simulation
        if (chromOff > 0.5f)
        {
            var stR = S(_stTitle, titleSz, new Color(0.8f, 0.2f, 0.2f, titleA * 0.5f), TextAnchor.MiddleCenter);
            GUI.Label(new Rect(-chromOff, cy, w, h * 0.12f), "DEMAGNETIZED", stR);
            var stC = S(_stTitle, titleSz, new Color(0.2f, 0.7f, 0.8f, titleA * 0.5f), TextAnchor.MiddleCenter);
            GUI.Label(new Rect(chromOff, cy, w, h * 0.12f), "DEMAGNETIZED", stC);
        }

        // Main title
        var stM = S(_stTitle, titleSz, ColA(Color.white, titleA), TextAnchor.MiddleCenter);
        GUI.Label(new Rect(0, cy, w, h * 0.12f), "DEMAGNETIZED", stM);

        // "CLICK TO START" pulsing
        if (_tcTimer > 1.5f)
        {
            float pulse = Mathf.Sin(_t * 2.5f) * 0.3f + 0.7f;
            var stP = S(_stBody, Sz(0.018f), ColA(CText60, a * pulse), TextAnchor.MiddleCenter);
            GUI.Label(new Rect(0, cy + h * 0.14f, w, Sz(0.04f)), L.Get("clicktostart"), stP);
        }
    }

    #endregion

    // ───────────────────────────── DRAW: MAIN MENU ─────────────────────────────

    #region Main Menu

    private void DrawMainMenu(float a, float sl)
    {
        float w = Screen.width, h = Screen.height;
        float lx = w * 0.065f; // 6.5vw left margin
        float ty = h * 0.42f + sl; // bottom-left positioning (HTML: 55%, balanced for screen fit)

        // ── Title ──
        var stT = S(_stTitle, Sz(0.065f), ColA(Color.white, a), TextAnchor.MiddleLeft);
        GUI.Label(new Rect(lx, ty, w * 0.6f, h * 0.08f), "DEMAGNETIZED", stT);

        // Tagline
        var stTag = S(_stLight, Sz(0.014f), ColA(CText60, a * 0.7f));
        stTag.fontStyle = FontStyle.Italic;
        GUI.Label(new Rect(lx, ty + h * 0.065f, w * 0.5f, h * 0.025f), L.Get("tagline"), stTag);

        // Separator line (animated width, 30px target per HTML spec)
        float sepW = Mathf.Lerp(0, 30f, Mathf.Clamp01(_menuTimer / 0.8f));
        GUI.color = ColA(CAmber, a);
        GUI.DrawTexture(new Rect(lx, ty + h * 0.095f, sepW, 1), MenuStyles.SolidTexture);
        GUI.color = Color.white;

        // ── Menu Items ──
        float iy = ty + h * 0.115f;
        float itemH = h * 0.042f;

        for (int i = 0; i < _items.Count; i++)
        {
            // Stagger-in
            float stagger = Mathf.Clamp01((_menuTimer - 0.3f - i * 0.06f) / 0.25f);
            if (stagger <= 0f) continue;
            if (i == _items.Count - 1 && stagger >= 1f) _staggerDone = true;

            var it = _items[i];
            float ia = a * stagger;
            float ix = lx + 30f; // space for marker

            bool sel = (i == _menuSel);
            Rect r = new Rect(lx, iy, w * 0.35f, itemH);
            bool hover = !it.locked && r.Contains(Event.current.mousePosition);

            // Mouse hover → select
            if (hover && !sel && Event.current.type == EventType.Repaint && _staggerDone)
            {
                _menuSel = i;
                PlaySFX(sfxHover);
                sel = true;
            }

            // Mouse click
            if (hover && Event.current.type == EventType.MouseDown && !it.locked && _staggerDone)
            {
                PlaySFX(sfxSelect);
                it.action?.Invoke();
                Event.current.Use();
            }

            // Marker line
            if (sel && !it.locked)
            {
                float mw = 24f;
                GUI.color = ColA(CAmber, ia);
                GUI.DrawTexture(new Rect(lx, iy + itemH * 0.35f, mw, 2), MenuStyles.SolidTexture);
                GUI.color = Color.white;
                ix = lx + mw + 12f;
            }
            else if (hover && !it.locked)
            {
                float mw = 18f;
                GUI.color = ColA(CAmber, ia * 0.5f);
                GUI.DrawTexture(new Rect(lx, iy + itemH * 0.35f, mw, 2), MenuStyles.SolidTexture);
                GUI.color = Color.white;
                ix = lx + mw + 12f;
            }

            // Text
            Color tc = it.locked ? ColA(CText30, ia * 0.3f)
                     : sel ? ColA(CAmber, ia)
                     : hover ? ColA(CText, ia)
                     : ColA(CText60, ia);
            var stI = S(_stBold, Sz(0.020f), tc);
            GUI.Label(new Rect(ix, iy, w * 0.3f, itemH), it.text, stI);

            // Subtitle
            if (!string.IsNullOrEmpty(it.subtitle))
            {
                Color sc = it.locked ? ColA(CDanger, ia * 0.4f) : ColA(CText30, ia);
                var stS = S(_stLight, Sz(0.013f), sc);
                GUI.Label(new Rect(ix + w * 0.14f, iy, w * 0.2f, itemH), it.subtitle, stS);
            }

            iy += itemH;
        }
    }

    private void BuildMenu()
    {
        _items.Clear();

        if (_hasSave)
        {
            float pt = PlayerPrefs.GetFloat("PlayTime", 872f);
            int hrs = Mathf.FloorToInt(pt / 3600f);
            int min = Mathf.FloorToInt((pt % 3600f) / 60f);
            int sec = Mathf.FloorToInt(pt % 60f);
            _items.Add(new MItem
            {
                text = L.Get("menu_continue"),
                subtitle = $"{L.Get("menu_side_a")} \u2014 {hrs:00}:{min:00}:{sec:00}",
                action = DoContinue
            });
        }

        _items.Add(new MItem { text = L.Get("menu_new_recording"), action = () => GoTo(MenuPanel.NewGameConfirm) });
        _items.Add(new MItem { text = L.Get("menu_chapters"), subtitle = L.Get("menu_locked"), locked = true });
        _items.Add(new MItem { text = L.Get("menu_calibration"), action = () => GoTo(MenuPanel.Settings) });
        _items.Add(new MItem { text = L.Get("liner_notes"), action = () => GoTo(MenuPanel.Credits) });
        _items.Add(new MItem { text = L.Get("eject"), action = () => GoTo(MenuPanel.QuitConfirm) });

        _menuSel = 0;
        while (_menuSel < _items.Count && _items[_menuSel].locked) _menuSel++;
    }

    #endregion

    // ───────────────────────────── DRAW: SETTINGS ─────────────────────────────

    #region Settings Panel

    private void DrawSettingsPanel(float a, float sl)
    {
        float w = Screen.width, h = Screen.height;

        // Semi-transparent overlay
        GUI.color = new Color(0, 0, 0, 0.6f * a);
        GUI.DrawTexture(new Rect(0, 0, w, h), MenuStyles.SolidTexture);
        GUI.color = Color.white;

        float leftW = w * 0.38f;
        float rightX = leftW;
        float rightW = w * 0.62f;
        float pad = w * 0.04f;
        float barY = _barH;
        float contentH = h - _barH * 2f;

        // ── LEFT PANEL ──
        DrawSettingsLeft(a, sl, pad, barY, leftW, contentH);

        // Vertical separator
        GUI.color = ColA(CText15, a);
        GUI.DrawTexture(new Rect(rightX, barY + contentH * 0.1f, 1, contentH * 0.8f), MenuStyles.SolidTexture);
        GUI.color = Color.white;

        // ── RIGHT PANEL ──
        DrawSettingsRight(a, sl, rightX + pad, barY, rightW - pad * 2f, contentH);
    }

    private void DrawSettingsLeft(float a, float sl, float pad, float topY, float leftW, float contentH)
    {
        float h = Screen.height;
        float x = pad;
        float y = topY + contentH * 0.08f + sl;

        // Back button
        var backR = new Rect(x, y, 120, Sz(0.035f));
        bool bHov = backR.Contains(Event.current.mousePosition);
        var stBack = S(_stBold, Sz(0.016f), ColA(bHov ? CAmber : CText60, a));
        GUI.Label(backR, "\u2190 " + L.Get("back"), stBack);
        if (bHov && Event.current.type == EventType.MouseDown)
        {
            PlaySFX(sfxBack);
            SaveSettings();
            GoTo(MenuPanel.MainMenu);
            Event.current.Use();
        }

        // Category title
        y += Sz(0.08f);
        var catNames = _catNames;
        var catDescs = _catDescs;

        var stCat = S(_stTitle, Sz(0.06f), ColA(Color.white, a));
        GUI.Label(new Rect(x, y, leftW - pad * 2, Sz(0.08f)), catNames[_sTabIdx], stCat);

        y += Sz(0.07f);
        var stDesc = S(_stLight, Sz(0.014f), ColA(CText60, a * 0.7f));
        stDesc.fontStyle = FontStyle.Italic;
        GUI.Label(new Rect(x, y, leftW - pad * 2, Sz(0.025f)), catDescs[_sTabIdx], stDesc);

        // Category list
        y += Sz(0.08f);
        float catItemH = Sz(0.045f);
        for (int i = 0; i < 4; i++)
        {
            bool sel = (i == _sTabIdx);
            Rect cr = new Rect(x, y, leftW - pad * 2, catItemH);
            bool cHov = cr.Contains(Event.current.mousePosition);

            // Marker
            if (sel)
            {
                GUI.color = ColA(CAmber, a);
                GUI.DrawTexture(new Rect(x, y + catItemH * 0.35f, 20, 2), MenuStyles.SolidTexture);
                GUI.color = Color.white;
            }

            Color tc = sel ? ColA(CAmber, a) : cHov ? ColA(CText, a) : ColA(CText60, a);
            var stC = S(_stBold, Sz(0.018f), tc);
            GUI.Label(new Rect(x + 30, y, leftW - pad * 2, catItemH), catNames[i], stC);

            if (cHov && Event.current.type == EventType.MouseDown)
            {
                _sTabIdx = i;
                _sTab = (SettingsTab)i;
                _sTabFade = 0f;
                _settingsScroll = Vector2.zero;
                PlaySFX(sfxHover);
                Event.current.Use();
            }

            y += catItemH;
        }
    }

    private void DrawSettingsRight(float a, float sl, float x, float topY, float w, float contentH)
    {
        float fadeA = Mathf.Clamp01(_sTabFade / 0.3f) * a;
        float y = topY + contentH * 0.1f + sl;
        float viewH = contentH * 0.8f;

        Rect viewRect = new Rect(x, y, w, viewH);
        float totalH = EstimateTabHeight();
        Rect contentRect = new Rect(0, 0, w - 20, Mathf.Max(totalH, viewH));

        // Custom scrollbar style (invisible)
        GUIStyle noScroll = GUIStyle.none;

        _settingsScroll = GUI.BeginScrollView(viewRect, _settingsScroll, contentRect, false, false, noScroll, noScroll);

        float cy = 0f;
        switch (_sTab)
        {
            case SettingsTab.Audio: cy = DrawAudioTab(fadeA, 0, 0, w - 20); break;
            case SettingsTab.Video: cy = DrawVideoTab(fadeA, 0, 0, w - 20); break;
            case SettingsTab.Controls: cy = DrawControlsTab(fadeA, 0, 0, w - 20); break;
            case SettingsTab.Accessibility: cy = DrawAccessibilityTab(fadeA, 0, 0, w - 20); break;
        }

        GUI.EndScrollView();

        // Mouse wheel for scroll
        if (viewRect.Contains(Event.current.mousePosition))
        {
            if (Event.current.type == EventType.ScrollWheel)
            {
                _settingsScroll.y += Event.current.delta.y * 30f;
                _settingsScroll.y = Mathf.Clamp(_settingsScroll.y, 0, Mathf.Max(0, totalH - viewH));
                Event.current.Use();
            }
        }
    }

    private float EstimateTabHeight()
    {
        float row = Sz(0.055f);
        switch (_sTab)
        {
            case SettingsTab.Audio: return row * 5;
            case SettingsTab.Video: return row * (_dlssOk ? 15 : 14);
            case SettingsTab.Controls: return row * 4;
            case SettingsTab.Accessibility: return row * 4;
        }
        return row * 6;
    }

    // ── AUDIO TAB ──
    private float DrawAudioTab(float a, float x, float y, float w)
    {
        y = DrawSliderRow(L.Get("master_volume"), ref _sMaster, x, y, w, a,
            v => { var gs = GameSettings.Instance; if (gs != null) gs.MasterVolume = v; AudioListener.volume = v; });
        y = DrawSliderRow(L.Get("music"), ref _sMusic, x, y, w, a,
            v => { var gs = GameSettings.Instance; if (gs != null) gs.MusicVolume = v; });
        y = DrawSliderRow(L.Get("sound_effects"), ref _sSfx, x, y, w, a,
            v => { var gs = GameSettings.Instance; if (gs != null) gs.SFXVolume = v; });
        y = DrawSliderRow(L.Get("ambience"), ref _sAmbient, x, y, w, a,
            v => { var gs = GameSettings.Instance; if (gs != null) gs.AmbientVolume = v; });
        return y;
    }

    // ── VIDEO TAB ──
    private float DrawVideoTab(float a, float x, float y, float w)
    {
        // Resolution
        string resLabel = _resArr != null && _resIdx < _resArr.Length
            ? $"{_resArr[_resIdx].width}x{_resArr[_resIdx].height}"
            : "N/A";
        y = DrawOptionNav(L.Get("resolution"), resLabel, x, y, w, a,
            () => { if (_resArr != null && _resArr.Length > 0) { _resIdx = (_resIdx - 1 + _resArr.Length) % _resArr.Length; ApplyResolution(); } },
            () => { if (_resArr != null && _resArr.Length > 0) { _resIdx = (_resIdx + 1) % _resArr.Length; ApplyResolution(); } });

        y = DrawToggleRow(L.Get("fullscreen"), ref _sFullscreen, x, y, w, a,
            v => { Screen.fullScreen = v; var gs = GameSettings.Instance; if (gs != null) gs.IsFullscreen = v; });

        y = DrawToggleRow(L.Get("vsync"), ref _sVsync, x, y, w, a,
            v => { QualitySettings.vSyncCount = v ? 1 : 0; var gs = GameSettings.Instance; if (gs != null) gs.VSync = v; });

        // Quality
        var qNames = _qualityNames;
        y = DrawSelectRow(L.Get("quality"), qNames, ref _sQuality, x, y, w, a,
            v => {
                var gfx = ServiceLocator.Instance != null ? ServiceLocator.Instance.GraphicsQuality : GraphicsQualityManager.Instance;
                if (gfx != null) gfx.SetQuality(v);
                var gs = GameSettings.Instance; if (gs != null) gs.QualityLevel = v;
            });

        // DLSS / Anti-Aliasing
        if (_dlssOk)
        {
            var dlssNames = DlssNames;
            y = DrawSelectRow(L.Get("dlss_mode"), dlssNames, ref _sDlssMode, x, y, w, a,
                v => {
                    var dlss = ServiceLocator.Instance != null ? ServiceLocator.Instance.DLSS : WorkingDLSSManager.Instance;
                    if (dlss != null) dlss.SetMode((WorkingDLSSManager.DLSSMode)v);
                    var gs = GameSettings.Instance; if (gs != null) gs.DLSSMode = v;
                });

            if (_sDlssMode > 0)
            {
                y = DrawSliderRow(L.Get("sharpness"), ref _sDlssSharp, x, y, w, a,
                    v => { var gs = GameSettings.Instance; if (gs != null) gs.DLSSSharpness = v; });
            }
        }
        else
        {
            // DLSS not supported — show TAA as the active anti-aliasing
            var stSec2 = S(_stLight, Sz(0.013f), ColA(CText60, a));
            GUI.Label(new Rect(x, y, w, Sz(0.025f)), L.Get("aa_taa_active"), stSec2);
            y += Sz(0.035f);
        }

        // Effects toggles
        y += Sz(0.015f); // spacer
        var stSec = S(_stLight, Sz(0.013f), ColA(CText30, a));
        GUI.Label(new Rect(x, y, w, Sz(0.025f)), L.Get("postprocessing"), stSec);
        y += Sz(0.03f);

        y = DrawToggleRow(L.Get("bloom"), ref _sBloom, x, y, w, a,
            v => { var gs = GameSettings.Instance; if (gs != null) { gs.Bloom = v; gs.ApplyEffects(); } });
        y = DrawToggleRow(L.Get("ssao"), ref _sSsao, x, y, w, a,
            v => { var gs = GameSettings.Instance; if (gs != null) { gs.SSAO = v; gs.ApplyEffects(); } });
        y = DrawToggleRow(L.Get("volumetricfog"), ref _sVolFog, x, y, w, a,
            v => { var gs = GameSettings.Instance; if (gs != null) { gs.VolumetricFog = v; gs.ApplyEffects(); } });
        y = DrawToggleRow(L.Get("filmgrain"), ref _sFilmGrain, x, y, w, a,
            v => { var gs = GameSettings.Instance; if (gs != null) { gs.FilmGrain = v; gs.ApplyEffects(); } });
        y = DrawToggleRow(L.Get("vignette"), ref _sVignette, x, y, w, a,
            v => { var gs = GameSettings.Instance; if (gs != null) { gs.Vignette = v; gs.ApplyEffects(); } });
        y = DrawToggleRow(L.Get("motionblur"), ref _sMotionBlur, x, y, w, a,
            v => { var gs = GameSettings.Instance; if (gs != null) { gs.MotionBlur = v; gs.ApplyEffects(); } });

        // FPS Limit
        if (_fpsNames == null) RefreshLocalizedArrays();
        var fpsNames = _fpsNames;
        y = DrawSelectRow(L.Get("fps_limit"), fpsNames, ref _sFpsLimit, x, y, w, a,
            v => { Application.targetFrameRate = FpsValues[v]; });

        return y;
    }

    // ── CONTROLS TAB ──
    private float DrawControlsTab(float a, float x, float y, float w)
    {
        y = DrawSliderRow(L.Get("mouse_sensitivity"), ref _sSens, x, y, w, a,
            v => { var gs = GameSettings.Instance; if (gs != null) gs.MouseSensitivity = Mathf.Lerp(SENSITIVITY_MIN, SENSITIVITY_MAX, v); }, 0.1f, 2f);

        y = DrawToggleRow(L.Get("invert_y"), ref _sInvertY, x, y, w, a,
            v => { var gs = GameSettings.Instance; if (gs != null) gs.InvertY = v; });

        y = DrawSliderRow(L.Get("fov"), ref _sFov, x, y, w, a,
            v => { var gs = GameSettings.Instance; if (gs != null) { gs.FieldOfView = Mathf.Lerp(FOV_MIN, FOV_MAX, v); gs.ApplyCamera(); } },
            60f, 120f, "0");

        return y;
    }

    // ── ACCESSIBILITY TAB ──
    private string[] _localeNames;

    private float DrawAccessibilityTab(float a, float x, float y, float w)
    {
        if (_localeNames == null || _localeNames.Length == 0) _localeNames = Loc.GetLocaleNames();
        int count = _localeNames.Length;
        if (count == 0) { _localeNames = new[] { "English" }; count = 1; }
        if (_sLang >= count) _sLang = 0;

        // Label
        var stL = S(_stBold, Sz(0.014f), ColA(CText, a));
        GUI.Label(new Rect(x, y, w, Sz(0.025f)), L.Get("language"), stL);
        y += Sz(0.030f);

        // Grid: 4 columns, premium card style
        int cols = 4;
        float gap = Sz(0.004f);
        float btnW = (w - gap * (cols - 1)) / cols;
        float btnH = Sz(0.028f);
        float rowGap = Sz(0.003f);

        for (int i = 0; i < count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            float bx = x + col * (btnW + gap);
            float by = y + row * (btnH + rowGap);
            Rect br = new Rect(bx, by, btnW, btnH);
            bool sel = (i == _sLang);
            bool hov = br.Contains(Event.current.mousePosition);

            // Selected: amber fill + top accent
            if (sel)
            {
                GUI.color = ColA(CAmber, a * 0.12f);
                GUI.DrawTexture(br, MenuStyles.SolidTexture);
                GUI.color = ColA(CAmber, a * 0.9f);
                GUI.DrawTexture(new Rect(bx, by, btnW, 2), MenuStyles.SolidTexture);
            }
            else if (hov)
            {
                GUI.color = ColA(CText, a * 0.05f);
                GUI.DrawTexture(br, MenuStyles.SolidTexture);
            }

            // Subtle bottom border only (clean look)
            GUI.color = sel ? ColA(CAmber, a * 0.3f) : ColA(CText, a * (hov ? 0.12f : 0.06f));
            GUI.DrawTexture(new Rect(bx, by + btnH - 1, btnW, 1), MenuStyles.SolidTexture);
            GUI.color = Color.white;

            // Text
            Color tc = sel ? ColA(CAmber, a) : hov ? ColA(CText, a * 0.9f) : ColA(CText, a * 0.5f);
            var stB = S(sel ? _stBold : _stBody, Sz(0.0105f), tc, TextAnchor.MiddleCenter);
            GUI.Label(br, _localeNames[i], stB);

            if (hov && Event.current.type == EventType.MouseDown)
            {
                _sLang = i;
                Loc.SetLocale(i);
                PlaySFX(sfxSelect);
                Event.current.Use();
            }
        }

        int totalRows = (count + cols - 1) / cols;
        y += totalRows * (btnH + rowGap) + Sz(0.012f);

        if (_subSizes == null) RefreshLocalizedArrays();
        y = DrawSelectRow(L.Get("subtitle_size"), _subSizes, ref _sSubSize, x, y, w, a, null);

        if (_cbModes == null) RefreshLocalizedArrays();
        y = DrawSelectRow(L.Get("colorblind_mode"), _cbModes, ref _sColorblind, x, y, w, a, null);

        return y;
    }

    #endregion

    // ───────────────────────────── DRAW: CREDITS ─────────────────────────────

    #region Credits

    private void DrawCreditsPanel(float a, float sl)
    {
        float w = Screen.width, h = Screen.height;

        // Overlay
        GUI.color = new Color(0, 0, 0, 0.7f * a);
        GUI.DrawTexture(new Rect(0, 0, w, h), MenuStyles.SolidTexture);
        GUI.color = Color.white;

        float cx = w * 0.5f;
        float topY = _barH + h * 0.08f + sl;

        // Title
        var stT = S(_stTitle, Sz(0.05f), ColA(Color.white, a), TextAnchor.MiddleCenter);
        GUI.Label(new Rect(0, topY, w, Sz(0.07f)), L.Get("liner_notes"), stT);

        var stSub = S(_stLight, Sz(0.014f), ColA(CText60, a * 0.7f), TextAnchor.MiddleCenter);
        stSub.fontStyle = FontStyle.Italic;
        GUI.Label(new Rect(0, topY + Sz(0.065f), w, Sz(0.025f)), L.Get("liner_notes_subtitle"), stSub);

        // Credits content (auto-scroll)
        float scrollY = topY + Sz(0.12f);
        float viewH = h - scrollY - _barH - h * 0.08f;
        Rect view = new Rect(w * 0.2f, scrollY, w * 0.6f, viewH);

        _credHover = view.Contains(Event.current.mousePosition);
        if (!_credHover) _credScroll += Time.unscaledDeltaTime * 12f;

        var credits = Credits;

        float totalH = credits.Length * Sz(0.12f);
        Rect content = new Rect(0, 0, w * 0.6f - 20, Mathf.Max(totalH, viewH));

        Vector2 credScrollVec = new Vector2(0, _credScroll);
        credScrollVec = GUI.BeginScrollView(view, credScrollVec, content, false, false, GUIStyle.none, GUIStyle.none);
        _credScroll = credScrollVec.y;

        float cy = 0;
        for (int i = 0; i < credits.Length; i++)
        {
            float fadeIn = Mathf.Clamp01((_credScroll + viewH * 0.3f - cy) / (viewH * 0.3f));
            float ca = a * Mathf.Clamp01(fadeIn);

            string role = credits[i][0];
            string name = credits[i][1];
            string localRole = !string.IsNullOrEmpty(role) ? L.Get(role) : "";
            string localName = L.Get(name);

            if (localRole.Length > 0)
            {
                var stCatT = S(_stBold, Sz(0.013f), ColA(CAmber, ca));
                GUI.Label(new Rect(0, cy, content.width, Sz(0.025f)), localRole, stCatT);
                cy += Sz(0.025f);
            }

            int lineCount = 1;
            foreach (char ch in localName) if (ch == '\n') lineCount++;
            var stName = S(_stBody, Sz(0.016f), ColA(CText, ca));
            float nameH = Sz(0.025f) * lineCount;
            GUI.Label(new Rect(0, cy, content.width, nameH + Sz(0.01f)), localName, stName);
            cy += nameH + Sz(0.04f);
        }

        GUI.EndScrollView();

        // Back button
        float backY = h - _barH - h * 0.06f;
        var backR = new Rect(w * 0.5f - 60, backY, 120, Sz(0.035f));
        bool bH = backR.Contains(Event.current.mousePosition);
        var stB = S(_stBold, Sz(0.016f), ColA(bH ? CAmber : CText60, a), TextAnchor.MiddleCenter);
        GUI.Label(backR, "\u2190 " + L.Get("back"), stB);
        if (bH && Event.current.type == EventType.MouseDown)
        {
            PlaySFX(sfxBack);
            GoTo(MenuPanel.MainMenu);
            Event.current.Use();
        }
    }

    #endregion

    // ───────────────────────────── DRAW: CONFIRM DIALOGS ─────────────────────────────

    #region Confirm Dialogs

    private void DrawConfirm(float a, float sl, bool isNewGame)
    {
        float w = Screen.width, h = Screen.height;

        // Overlay
        GUI.color = new Color(0, 0, 0, 0.75f * a);
        GUI.DrawTexture(new Rect(0, 0, w, h), MenuStyles.SolidTexture);
        GUI.color = Color.white;

        float cy = h * 0.38f + sl;

        // Message
        string msg = isNewGame
            ? L.Get("confirm_new_recording")
            : L.Get("confirm_eject");
        var stMsg = S(_stBody, Sz(0.020f), ColA(CText, a), TextAnchor.MiddleCenter);
        GUI.Label(new Rect(0, cy, w, Sz(0.06f)), msg, stMsg);

        // Warning (new game only)
        if (isNewGame)
        {
            float pulse = Mathf.Sin(_t * 3f) * 0.2f + 0.8f;
            var stWarn = S(_stBold, Sz(0.015f), ColA(CDanger, a * pulse), TextAnchor.MiddleCenter);
            GUI.Label(new Rect(0, cy + Sz(0.07f), w, Sz(0.03f)), L.Get("progress_lost_warning"), stWarn);
        }

        // Buttons
        float btnY = cy + (isNewGame ? Sz(0.14f) : Sz(0.09f));
        float btnW = 160f;
        float gap = 40f;
        float bx0 = w * 0.5f - btnW - gap * 0.5f;
        float bx1 = w * 0.5f + gap * 0.5f;
        float btnH = Sz(0.04f);

        string cancelTxt = L.Get("btn_cancel");
        string actionTxt = isNewGame ? L.Get("btn_erase_record") : L.Get("eject");

        // Cancel button
        DrawConfirmButton(new Rect(bx0, btnY, btnW, btnH), cancelTxt, _confirmBtn == 0, false, a, () =>
        {
            PlaySFX(sfxBack);
            GoTo(MenuPanel.MainMenu);
        });

        // Action button
        DrawConfirmButton(new Rect(bx1, btnY, btnW, btnH), actionTxt, _confirmBtn == 1, isNewGame, a, () =>
        {
            PlaySFX(sfxSelect);
            if (isNewGame) DoNewGame();
            else DoQuit();
        });
    }

    private void DrawConfirmButton(Rect r, string text, bool sel, bool danger, float a, Action onClick)
    {
        bool hov = r.Contains(Event.current.mousePosition);
        if (hov && Event.current.type == EventType.Repaint)
        {
            _confirmBtn = sel ? _confirmBtn : (1 - _confirmBtn);
        }

        Color c = sel ? (danger ? CDanger : CAmber) : CText60;
        var st = S(_stBold, Sz(0.016f), ColA(c, a), TextAnchor.MiddleCenter);
        GUI.Label(r, text, st);

        // Underline
        if (sel || hov)
        {
            GUI.color = ColA(danger ? CDanger : CAmber, a * 0.6f);
            GUI.DrawTexture(new Rect(r.x + r.width * 0.1f, r.y + r.height - 2, r.width * 0.8f, 1), MenuStyles.SolidTexture);
            GUI.color = Color.white;
        }

        if (hov && Event.current.type == EventType.MouseDown)
        {
            onClick?.Invoke();
            Event.current.Use();
        }
    }

    #endregion

    // ───────────────────────────── DRAW: VHS EFFECTS ─────────────────────────────

    #region VHS Effects

    private void UpdateVHS(float dt)
    {
        _scanY += dt * (Screen.height / 14f);
        if (_scanY > Screen.height * 1.1f) _scanY = -Screen.height * 0.1f;

        _vigBreath += dt;

        // Glitch
        _glitchCD -= dt;
        if (_glitchCD <= 0f)
        {
            _glitchOn = true;
            _glitchDur = 0.25f;
            _glitchCD = UnityEngine.Random.Range(12f, 20f);
        }
        if (_glitchOn)
        {
            _glitchDur -= dt;
            if (_glitchDur <= 0f) _glitchOn = false;
        }

        // Idle glitch
        if (_idleTime > 25f)
        {
            _glitchOn = true;
            _glitchDur = 0.15f;
            _idleTime = 0f;
        }

        // REC dropout
        _recDropCD -= dt;
        if (_recDropCD <= 0f)
        {
            _recShow = false;
            _recDropCD = UnityEngine.Random.Range(18f, 30f);
        }
        if (!_recShow)
        {
            _glitchDur -= dt; // reuse
            if (_recDropCD > 17f) _recShow = true; // show again after brief dropout
        }
        _recShow = _recDropCD > 0.3f || _recDropCD < 0f;
    }

    private void DrawVHSFX()
    {
        float w = Screen.width, h = Screen.height;

        // Film grain
        MenuStyles.DrawFilmGrain(new Rect(0, 0, w, h), _t, 0.28f);

        // Scanline sweep (single line)
        float scanPct = (_scanY / h);
        if (scanPct > 0.85f && scanPct < 0.95f)
        {
            GUI.color = new Color(1, 1, 1, 0.06f);
            GUI.DrawTexture(new Rect(0, _scanY, w, 1), MenuStyles.SolidTexture);
            GUI.color = Color.white;
        }

        // Vignette (breathing)
        float vigA = 0.85f + Mathf.Sin(_vigBreath * 0.785f) * 0.15f; // 8s period, range 0.7-1.0
        DrawVignette(vigA);

        // Particles
        DrawParticles();

        // Glitch (chromatic aberration burst)
        if (_glitchOn)
        {
            float ga = 0.15f;
            float off = UnityEngine.Random.Range(2f, 5f);
            GUI.color = new Color(0.8f, 0.2f, 0.2f, ga);
            GUI.DrawTexture(new Rect(-off, 0, w, h), MenuStyles.SolidTexture);
            GUI.color = new Color(0.2f, 0.7f, 0.8f, ga);
            GUI.DrawTexture(new Rect(off, 0, w, h), MenuStyles.SolidTexture);
            GUI.color = Color.white;
        }
    }

    private void DrawVignette(float strength)
    {
        MenuStyles.DrawVignette(Screen.width, Screen.height, strength);
    }

    #endregion

    // ───────────────────────────── DRAW: HUD ─────────────────────────────

    #region HUD

    private void DrawHUD(float a)
    {
        float w = Screen.width, h = Screen.height;
        float mx = w * 0.025f;
        float my = _barH + h * 0.02f;

        // Top-left: REC dot + timestamp
        if (_recShow)
        {
            bool blink = Mathf.Sin(_t * 4f) > 0f;
            if (blink)
            {
                GUI.color = ColA(new Color(0.9f, 0.15f, 0.15f), a);
                GUI.DrawTexture(new Rect(mx, my + 4, 8, 8), MenuStyles.SolidTexture);
                GUI.color = Color.white;
            }

            int ts = Mathf.FloorToInt(_t);
            if (ts != _lastRecTs) { _lastRecTs = ts; _cachedRecStr = $"REC \u00B7 {ts / 3600:00}:{(ts % 3600) / 60:00}:{ts % 60:00}"; }
            var stR = S(_stHud, Sz(0.012f), ColA(CText30, a * 0.7f));
            GUI.Label(new Rect(mx + 14, my, 200, 16), _cachedRecStr, stR);
        }

        // Top-right: Chapter
        var stCh = S(_stHud, Sz(0.012f), ColA(CText30, a * 0.5f), TextAnchor.MiddleRight);
        GUI.Label(new Rect(w - 200 - mx, my, 200, 16), L.Get("hud_chapter") + " II", stCh);
        var stChN = S(_stHud, Sz(0.011f), ColA(CText15, a * 0.4f), TextAnchor.MiddleRight);
        GUI.Label(new Rect(w - 200 - mx, my + 16, 200, 14), L.Get("hud_ch2_name"), stChN);

        // Bottom-left: Controls hint
        float by = h - _barH - h * 0.04f;
        string hint = MenuInput.GetNavigationHint();
        var stH = S(_stHud, Sz(0.011f), ColA(CText30, a * 0.4f));
        GUI.Label(new Rect(mx, by, w * 0.4f, 16), hint, stH);

        // Bottom-right: SCENE dots
        float drx = w - mx - 80;
        var stSc = S(_stHud, Sz(0.010f), ColA(CText15, a * 0.3f), TextAnchor.MiddleRight);
        GUI.Label(new Rect(drx, by, 80, 16), L.Get("hud_scene") + " \u2022\u2022\u2022", stSc);
    }

    #endregion

    // ───────────────────────────── DRAW: CINEMATIC BARS ─────────────────────────────

    #region Cinematic Bars

    private void DrawBars()
    {
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, _barH), MenuStyles.SolidTexture);
        GUI.DrawTexture(new Rect(0, Screen.height - _barH, Screen.width, _barH), MenuStyles.SolidTexture);
        GUI.color = Color.white;
    }

    #endregion

    // ───────────────────────────── CUSTOM WIDGETS ─────────────────────────────

    #region Widgets

    private float DrawSliderRow(string label, ref float value, float x, float y, float w, float a,
        Action<float> onChange, float minVal = 0f, float maxVal = 1f, string fmt = null)
    {
        float rowH = Sz(0.055f);
        float labelW = w * 0.4f;
        float sliderX = x + labelW;
        float sliderW = w * 0.42f;
        float valX = sliderX + sliderW + 10;
        float valW = w * 0.15f;

        // Label
        var stL = S(_stBold, Sz(0.015f), ColA(CText, a));
        GUI.Label(new Rect(x, y, labelW, rowH), label, stL);

        // Value text
        float displayVal = Mathf.Lerp(minVal, maxVal, value);
        string valStr = fmt == "0" ? $"{displayVal:F0}" : $"{Mathf.RoundToInt(value * 100)}%";
        var stV = S(_stBody, Sz(0.014f), ColA(CText60, a), TextAnchor.MiddleRight);
        GUI.Label(new Rect(valX, y, valW, rowH), valStr, stV);

        // Track
        float trackY = y + rowH * 0.5f - 1;
        GUI.color = ColA(CText15, a);
        GUI.DrawTexture(new Rect(sliderX, trackY, sliderW, 2), MenuStyles.SolidTexture);

        // Fill
        GUI.color = ColA(CAmber, a * 0.8f);
        GUI.DrawTexture(new Rect(sliderX, trackY, sliderW * value, 2), MenuStyles.SolidTexture);

        // Thumb
        float thumbR = 6f;
        float thumbX = sliderX + sliderW * value - thumbR;
        float thumbY = trackY - thumbR + 1;
        GUI.color = ColA(CAmber, a);
        GUI.DrawTexture(new Rect(thumbX, thumbY, thumbR * 2, thumbR * 2), MenuStyles.SolidTexture);
        GUI.color = Color.white;

        // Drag interaction
        int sid = _sliderCounter++;
        Rect dragRect = new Rect(sliderX - 10, y, sliderW + 20, rowH);

        if (_dragging && _dragID == sid)
        {
            float nv = Mathf.Clamp01((Event.current.mousePosition.x - sliderX) / sliderW);
            if (Mathf.Abs(nv - value) > 0.002f)
            {
                value = nv;
                onChange?.Invoke(Mathf.Lerp(minVal, maxVal, value));
            }
        }
        else if (dragRect.Contains(Event.current.mousePosition))
        {
            if (Event.current.type == EventType.MouseDown)
            {
                _dragging = true;
                _dragID = sid;
                float nv = Mathf.Clamp01((Event.current.mousePosition.x - sliderX) / sliderW);
                value = nv;
                onChange?.Invoke(Mathf.Lerp(minVal, maxVal, value));
                Event.current.Use();
            }
        }

        return y + rowH;
    }

    private float DrawToggleRow(string label, ref bool value, float x, float y, float w, float a,
        Action<bool> onChange)
    {
        float rowH = Sz(0.050f);
        float labelW = w * 0.7f;

        // Label
        var stL = S(_stBold, Sz(0.015f), ColA(CText, a));
        GUI.Label(new Rect(x, y, labelW, rowH), label, stL);

        // Toggle background
        float tW = 36f, tH = 16f;
        float tx = x + w - tW - 10;
        float ty = y + (rowH - tH) * 0.5f;
        Rect tr = new Rect(tx, ty, tW, tH);

        Color bgCol = value ? ColA(CAmber, a * 0.4f) : ColA(CText15, a);
        GUI.color = bgCol;
        GUI.DrawTexture(tr, MenuStyles.SolidTexture);

        // Border
        GUI.color = ColA(value ? CAmber : CText30, a * 0.5f);
        GUI.DrawTexture(new Rect(tx, ty, tW, 1), MenuStyles.SolidTexture);
        GUI.DrawTexture(new Rect(tx, ty + tH - 1, tW, 1), MenuStyles.SolidTexture);
        GUI.DrawTexture(new Rect(tx, ty, 1, tH), MenuStyles.SolidTexture);
        GUI.DrawTexture(new Rect(tx + tW - 1, ty, 1, tH), MenuStyles.SolidTexture);

        // Dot
        float dotSz = 10f;
        float dotX = value ? tx + tW - dotSz - 3 : tx + 3;
        float dotY = ty + (tH - dotSz) * 0.5f;
        GUI.color = ColA(value ? CAmber : CText60, a);
        GUI.DrawTexture(new Rect(dotX, dotY, dotSz, dotSz), MenuStyles.SolidTexture);
        GUI.color = Color.white;

        // Click
        if (tr.Contains(Event.current.mousePosition) && Event.current.type == EventType.MouseDown)
        {
            value = !value;
            onChange?.Invoke(value);
            PlaySFX(sfxSelect);
            Event.current.Use();
        }

        return y + rowH;
    }

    private float DrawSelectRow(string label, string[] options, ref int selected, float x, float y, float w, float a,
        Action<int> onChange)
    {
        float rowH = Sz(0.055f);
        float labelW = w * 0.35f;

        // Label
        var stL = S(_stBold, Sz(0.015f), ColA(CText, a));
        GUI.Label(new Rect(x, y, labelW, rowH), label, stL);

        // Buttons
        float bx = x + labelW;
        float bw = w - labelW;
        float gap = 4f;
        float btnW = (bw - gap * (options.Length - 1)) / options.Length;
        float btnH = Sz(0.032f);
        float by = y + (rowH - btnH) * 0.5f;

        for (int i = 0; i < options.Length; i++)
        {
            float bxi = bx + i * (btnW + gap);
            Rect br = new Rect(bxi, by, btnW, btnH);
            bool sel = (i == selected);
            bool hov = br.Contains(Event.current.mousePosition);

            // Background
            if (sel)
            {
                GUI.color = ColA(CAmber, a * 0.2f);
                GUI.DrawTexture(br, MenuStyles.SolidTexture);
            }

            // Border
            Color bc = sel ? ColA(CAmber, a * 0.8f) : ColA(CText15, a);
            if (hov && !sel) bc = ColA(CText30, a);
            GUI.color = bc;
            GUI.DrawTexture(new Rect(bxi, by, btnW, 1), MenuStyles.SolidTexture);
            GUI.DrawTexture(new Rect(bxi, by + btnH - 1, btnW, 1), MenuStyles.SolidTexture);
            GUI.DrawTexture(new Rect(bxi, by, 1, btnH), MenuStyles.SolidTexture);
            GUI.DrawTexture(new Rect(bxi + btnW - 1, by, 1, btnH), MenuStyles.SolidTexture);
            GUI.color = Color.white;

            // Text
            Color tc = sel ? ColA(CAmber, a) : hov ? ColA(CText, a) : ColA(CText60, a);
            var stB = S(_stBody, Sz(0.012f), tc, TextAnchor.MiddleCenter);
            GUI.Label(br, options[i], stB);

            // Click
            if (hov && Event.current.type == EventType.MouseDown)
            {
                selected = i;
                onChange?.Invoke(i);
                PlaySFX(sfxSelect);
                Event.current.Use();
            }
        }

        return y + rowH;
    }

    private float DrawOptionNav(string label, string display, float x, float y, float w, float a,
        Action onPrev, Action onNext)
    {
        float rowH = Sz(0.055f);
        float labelW = w * 0.4f;

        var stL = S(_stBold, Sz(0.015f), ColA(CText, a));
        GUI.Label(new Rect(x, y, labelW, rowH), label, stL);

        float navX = x + labelW;
        float navW = w - labelW;
        float arrowW = 30f;

        // Left arrow
        Rect lr = new Rect(navX, y, arrowW, rowH);
        bool lh = lr.Contains(Event.current.mousePosition);
        var stA = S(_stBold, Sz(0.018f), ColA(lh ? CAmber : CText60, a), TextAnchor.MiddleCenter);
        GUI.Label(lr, "<", stA);
        if (lh && Event.current.type == EventType.MouseDown)
        {
            onPrev?.Invoke();
            PlaySFX(sfxHover);
            Event.current.Use();
        }

        // Value
        var stVal = S(_stBody, Sz(0.015f), ColA(CText, a), TextAnchor.MiddleCenter);
        GUI.Label(new Rect(navX + arrowW, y, navW - arrowW * 2, rowH), display, stVal);

        // Right arrow
        Rect rr = new Rect(navX + navW - arrowW, y, arrowW, rowH);
        bool rh = rr.Contains(Event.current.mousePosition);
        stA.normal.textColor = ColA(rh ? CAmber : CText60, a);
        GUI.Label(rr, ">", stA);
        if (rh && Event.current.type == EventType.MouseDown)
        {
            onNext?.Invoke();
            PlaySFX(sfxHover);
            Event.current.Use();
        }

        return y + rowH;
    }

    #endregion

    // ───────────────────────────── BACKGROUND SCENE ─────────────────────────────

    #region Background Scene

    private IEnumerator LoadBgScene()
    {
        _bgLoading = true;

        // Fast path: use baked cubemap instead of loading the full scene (AAA optimization)
        if (bakedBackgroundCubemap != null)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                // HDRP uses Volume-based sky — create a global Volume with HDRI Sky
                _menuSkyGO = new GameObject("_MenuBakedSky");
                var volume = _menuSkyGO.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 100f;

                var profile = ScriptableObject.CreateInstance<VolumeProfile>();

                var visualEnv = profile.Add<VisualEnvironment>();
                visualEnv.skyType.overrideState = true;
                visualEnv.skyType.value = (int)SkyType.HDRI;
                visualEnv.skyAmbientMode.overrideState = true;
                visualEnv.skyAmbientMode.value = SkyAmbientMode.Dynamic;

                var hdriSky = profile.Add<HDRISky>();
                hdriSky.hdriSky.overrideState = true;
                hdriSky.hdriSky.value = bakedBackgroundCubemap;
                hdriSky.exposure.overrideState = true;
                hdriSky.exposure.value = 1f;

                volume.profile = profile;

                // HDRP: use HDAdditionalCameraData for clear mode (not Camera.clearFlags)
                if (cam.TryGetComponent<HDAdditionalCameraData>(out var hdCamData))
                    hdCamData.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
                else
                    cam.clearFlags = CameraClearFlags.Skybox; // fallback

                // Point camera at Camera7's original view direction
                _camRot0 = Quaternion.Euler(bakedCameraRotation);
                cam.transform.rotation = _camRot0;
                Debug.Log($"[Menu] Cubemap fast-path active — view rotation: {bakedCameraRotation}");
            }
            else
            {
                Debug.LogWarning("[Menu] Cubemap assigned but Camera.main is null!");
            }
            _bgLoaded = true;
            _bgLoading = false;
            if (_preloader != null && !_preloader.IsReady)
                _preloader.StartPreload();
            yield break;
        }

        // Fallback: load full scene (slower, higher memory)
        var op = SceneManager.LoadSceneAsync(backgroundSceneName, LoadSceneMode.Additive);
        if (op == null) { _bgLoading = false; yield break; }
        op.allowSceneActivation = true;
        yield return op;

        _bgLoaded = true;
        _bgLoading = false;

        _cachedCam = Camera.main;
        Camera mainCam = _cachedCam;
        var scene = SceneManager.GetSceneByName(backgroundSceneName);
        if (!scene.IsValid()) yield break;

        // Disable non-visual components only (cameras, audio, gameplay scripts).
        // All lights, particles, post-processing stay at full quality.
        Camera viewpointSource = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var cam in root.GetComponentsInChildren<Camera>(true))
            {
                if (cam == mainCam) continue;
                if (viewpointSource == null || cam.gameObject.name == "Camera7")
                    viewpointSource = cam;
                cam.enabled = false;
            }

            foreach (var al in root.GetComponentsInChildren<AudioListener>(true))
                al.enabled = false;
            foreach (var audioSrc in root.GetComponentsInChildren<AudioSource>(true))
                audioSrc.enabled = false;

            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var typeName = mb.GetType().Name;
                if (typeName == "Light" || typeName == "Volume") continue;
                if (typeName.Contains("HDAdditional") || typeName.Contains("HDRP") ||
                    typeName.Contains("Probe") || typeName.Contains("Decal") ||
                    typeName.Contains("Reflection") || typeName.Contains("Planar")) continue;
                mb.enabled = false;
            }
        }

        // Copy viewpoint from the bg camera to MainCamera (camera is already disabled, we only read Transform)
        bool viewpointCopied = false;
        if (viewpointSource != null && mainCam != null)
        {
            mainCam.transform.position = viewpointSource.transform.position;
            mainCam.transform.rotation = viewpointSource.transform.rotation;
            _camRot0 = viewpointSource.transform.rotation;

            var srcHD = viewpointSource.GetComponent<HDAdditionalCameraData>();
            var dstHD = mainCam.GetComponent<HDAdditionalCameraData>();
            if (srcHD != null && dstHD != null)
                dstHD.volumeLayerMask = srcHD.volumeLayerMask;

            viewpointCopied = true;
        }

#if UNITY_EDITOR
        Debug.Log($"[Menu] Background scene '{backgroundSceneName}' loaded. Viewpoint copied: {viewpointCopied}");
#endif

        // Start game scene preload now that bg scene is fully loaded (avoids I/O contention)
        if (_preloader != null && !_preloader.IsReady)
            _preloader.StartPreload();
    }

    private void UpdateParallax(float dt)
    {
        if (_cachedCam == null) return;
        float mx = (Input.mousePosition.x - Screen.width * 0.5f) / Screen.width;
        float my = (Input.mousePosition.y - Screen.height * 0.5f) / Screen.height;
        Quaternion target = _camRot0 * Quaternion.Euler(-my * parallaxAmount, mx * parallaxAmount, 0f);
        _cachedCam.transform.rotation = Quaternion.Slerp(_cachedCam.transform.rotation, target, dt * parallaxSmooth);
    }

    #endregion

    // ───────────────────────────── VIDEO ─────────────────────────────

    #region Video

    private void SetupVideo()
    {
        if (splashVideo == null)
        {
            _videoEnded = true;
            _splashPhase2 = true;
            if (!_bgLoaded && !_bgLoading) StartCoroutine(LoadBgScene());
            return;
        }

        _vp = gameObject.GetComponent<VideoPlayer>();
        if (_vp == null) _vp = gameObject.AddComponent<VideoPlayer>();

        _vpRT = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
        _vpRT.Create();

        _vp.clip = splashVideo;
        _vp.isLooping = false;
        _vp.playOnAwake = false;
        _vp.renderMode = VideoRenderMode.RenderTexture;
        _vp.targetTexture = _vpRT;
        _vp.skipOnDrop = true;
        _vp.audioOutputMode = VideoAudioOutputMode.Direct;
        _vp.loopPointReached += OnVideoEnd;
        _vp.prepareCompleted += vp =>
        {
            float mv = GameSettings.Instance != null ? GameSettings.Instance.MusicVolume : 0.5f;
            for (ushort i = 0; i < vp.audioTrackCount; i++)
                vp.SetDirectAudioVolume(i, mv);
            vp.Play();
        };
        _vp.Prepare();
    }

    private void OnVideoEnd(VideoPlayer vp)
    {
        _videoEnded = true;
        _splashPhase2 = true;
        if (!_bgLoaded && !_bgLoading) StartCoroutine(LoadBgScene());
    }

    #endregion

    // ───────────────────────────── AUDIO ─────────────────────────────

    #region Audio

    private void SetupAudio()
    {
        _ambSrc = gameObject.AddComponent<AudioSource>();
        _ambSrc.loop = true;
        _ambSrc.playOnAwake = false;
        _ambSrc.volume = 0.12f;

        _sfxSrc = gameObject.AddComponent<AudioSource>();
        _sfxSrc.loop = false;
        _sfxSrc.playOnAwake = false;
        _sfxSrc.volume = 0.5f;
    }

    private void StartHiss()
    {
        if (_hissStarted || ambientTapeHiss == null) return;
        _hissStarted = true;
        _ambSrc.clip = ambientTapeHiss;
        _ambSrc.Play();
    }

    private void PlaySFX(AudioClip clip)
    {
        if (clip != null && _sfxSrc != null)
            _sfxSrc.PlayOneShot(clip);
    }

    #endregion

    // ───────────────────────────── SETTINGS LOAD/SAVE ─────────────────────────────

    #region Settings Load/Save

    private void LoadSettings()
    {
        var gs = GameSettings.Instance;
        if (gs == null) return;

        _sMaster = gs.MasterVolume;
        _sMusic = gs.MusicVolume;
        _sSfx = gs.SFXVolume;
        _sAmbient = gs.AmbientVolume;

        _sFullscreen = gs.IsFullscreen;
        _sVsync = gs.VSync;
        _sQuality = gs.QualityLevel;
        _sDlssMode = gs.DLSSMode;
        _sDlssSharp = gs.DLSSSharpness;

        _sBloom = gs.Bloom;
        _sSsao = gs.SSAO;
        _sVolFog = gs.VolumetricFog;
        _sFilmGrain = gs.FilmGrain;
        _sVignette = gs.Vignette;
        _sMotionBlur = gs.MotionBlur;

        _sSens = Mathf.InverseLerp(0.1f, 2f, gs.MouseSensitivity);
        _sInvertY = gs.InvertY;
        _sFov = Mathf.InverseLerp(60f, 120f, gs.FieldOfView);

        _sLang = Loc.GetCurrentLocaleIndex();
        _sSubSize = 1;
        _sColorblind = 0;

        // FPS limit
        int fps = Application.targetFrameRate;
        _sFpsLimit = fps == 30 ? 0 : fps == 60 ? 1 : fps == 120 ? 2 : 3;
    }

    private void SaveSettings()
    {
        var gs = GameSettings.Instance;
        if (gs == null) return;

        gs.MasterVolume = _sMaster;
        gs.MusicVolume = _sMusic;
        gs.SFXVolume = _sSfx;
        gs.AmbientVolume = _sAmbient;
        gs.IsFullscreen = _sFullscreen;
        gs.VSync = _sVsync;
        gs.QualityLevel = _sQuality;
        gs.DLSSMode = _sDlssMode;
        gs.DLSSSharpness = _sDlssSharp;
        gs.Bloom = _sBloom;
        gs.SSAO = _sSsao;
        gs.VolumetricFog = _sVolFog;
        gs.FilmGrain = _sFilmGrain;
        gs.Vignette = _sVignette;
        gs.MotionBlur = _sMotionBlur;
        gs.MouseSensitivity = Mathf.Lerp(SENSITIVITY_MIN, SENSITIVITY_MAX, _sSens);
        gs.InvertY = _sInvertY;
        gs.FieldOfView = Mathf.Lerp(FOV_MIN, FOV_MAX, _sFov);

        gs.SaveAll();
    }

    private void InitResolutions()
    {
        var all = Screen.resolutions;
        var unique = new List<Resolution>();
        foreach (var r in all)
        {
            bool found = false;
            for (int i = 0; i < unique.Count; i++)
            {
                if (unique[i].width == r.width && unique[i].height == r.height)
                {
                    if (r.refreshRateRatio.value > unique[i].refreshRateRatio.value)
                        unique[i] = r;
                    found = true;
                    break;
                }
            }
            if (!found) unique.Add(r);
        }
        _resArr = unique.ToArray();

        _resIdx = 0;
        for (int i = 0; i < _resArr.Length; i++)
        {
            if (_resArr[i].width == Screen.width && _resArr[i].height == Screen.height)
            {
                _resIdx = i;
                break;
            }
        }
    }

    private void ApplyResolution()
    {
        if (_resArr == null || _resIdx >= _resArr.Length) return;
        var r = _resArr[_resIdx];
        Screen.SetResolution(r.width, r.height,
            _sFullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed,
            r.refreshRateRatio);
    }

    private void CheckDLSS()
    {
        var dlss = ServiceLocator.Instance != null ? ServiceLocator.Instance.DLSS : WorkingDLSSManager.Instance;
        _dlssOk = dlss != null && dlss.IsDLSSSupported;
        if (_dlssOk && dlss != null)
        {
            _sDlssMode = (int)dlss.CurrentMode;
            _sDlssSharp = dlss.DLSSSharpness;
        }
    }

    #endregion

    // ───────────────────────────── MENU ACTIONS ─────────────────────────────

    #region Menu Actions

    /// <summary>
    /// Open settings panel from external callers (e.g. WorkbenchMenuController).
    /// </summary>
    public void ShowSettings()
    {
        if (_panel != MenuPanel.Settings)
            GoTo(MenuPanel.Settings);
    }

    private void DoContinue()
    {
        if (_preloader != null && _preloader.IsReady)
            _preloader.TryActivate();
        else
            VHSLoadingScreen.Load(gameSceneName);
    }

    private void DoNewGame()
    {
        PlayerPrefs.DeleteKey("SaveExists");
        PlayerPrefs.SetFloat("PlayTime", 0f);
        PlayerPrefs.Save();

        if (_preloader != null && _preloader.IsReady)
            _preloader.TryActivate();
        else
            VHSLoadingScreen.Load(gameSceneName);
    }

    private void DoQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    #endregion

    // ───────────────────────────── PARTICLES ─────────────────────────────

    #region Particles

    private void InitParticles()
    {
        for (int i = 0; i < _ptcls.Length; i++)
        {
            _ptcls[i] = new Ptcl
            {
                x = UnityEngine.Random.Range(0f, Screen.width),
                y = UnityEngine.Random.Range(0f, Screen.height),
                spd = UnityEngine.Random.Range(8f, 25f),
                sz = UnityEngine.Random.Range(1f, 3f),
                a = UnityEngine.Random.Range(0.05f, 0.15f)
            };
        }
    }

    private void UpdateParticles(float dt)
    {
        for (int i = 0; i < _ptcls.Length; i++)
        {
            _ptcls[i].y -= _ptcls[i].spd * dt;
            if (_ptcls[i].y < -10f)
            {
                _ptcls[i].y = Screen.height + 10f;
                _ptcls[i].x = UnityEngine.Random.Range(0f, Screen.width);
            }
        }
    }

    private void DrawParticles()
    {
        for (int i = 0; i < _ptcls.Length; i++)
        {
            ref var p = ref _ptcls[i];
            GUI.color = new Color(CAmber.r, CAmber.g, CAmber.b, p.a);
            GUI.DrawTexture(new Rect(p.x, p.y, p.sz, p.sz * 2f), MenuStyles.SolidTexture);
        }
        GUI.color = Color.white;
    }

    #endregion

    // ───────────────────────────── HELPERS ─────────────────────────────

    #region Helpers

    private int Sz(float pct) => Mathf.RoundToInt(Screen.height * pct);

    private static Color ColA(Color c, float a) => MenuStyles.WithAlpha(c, a);

    #endregion
}
