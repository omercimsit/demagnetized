using UnityEngine;
using UnityEngine.SceneManagement;
// CAS_Demo.Scripts.FPS - resolved at runtime via reflection
using Menu.Pause;

// Color aliases from MenuStyles for cleaner code
using S = MenuStyles;

/// <summary>
/// DEMAGNETIZED - Modular Pause Menu Controller (v2)
/// 
/// REFACTORED ARCHITECTURE:
/// - PauseMenuState: Centralized state management
/// - PauseMenuVHSEffects: VHS visual effects
/// - PauseMenuPanelRenderer: Panel drawing
/// - PauseMenuButtonRenderer: Button drawing
/// - PauseMenuSettingsUI: Settings tabs (separate file)
/// 
/// This controller now acts as an orchestrator, delegating
/// rendering to specialized modules for better maintainability.
/// </summary>
public class PauseMenuManager : MonoBehaviour
{
    #region Serialized Fields

    [Header("Settings")]
    [SerializeField] private bool canOpenMenu = true;
    [SerializeField] private string mainMenuSceneName = "DemagnetizedMainMenu";
    [SerializeField] private float uiScale = 1.0f;

    [Header("Custom Fonts (Optional)")]
    [SerializeField] private Font titleFont;
    [SerializeField] private Font buttonFont;
    [SerializeField] private Font bodyFont;

    [Header("Audio")]
    [SerializeField] private AudioClip hoverSound;
    [SerializeField] private AudioClip selectSound;
    [SerializeField] [Range(0f, 1f)] private float sfxVolume = 0.5f;

    [Header("Pause Music")]
    [SerializeField] private AudioClip pauseMenuMusic;
    [SerializeField] [Range(0f, 1f)] private float pauseMusicVolume = 0.25f;
    [SerializeField] private float musicFadeSpeed = 2f;

    #endregion

    #region Private Fields

    // Static data (allocated once, not per-frame)
    private string[] _localeNames;
    private static readonly string[] DlssLabels = { "TAA", "DLAA", "QUAL", "BAL", "PERF", "ULTRA" };

    // Components
    private AudioSource _sfxSource;
    private AudioSource _musicSource;

    // State reference
    private PauseMenuState State => PauseMenuState.Instance;

    // Cached values
    private float _savedTimeScale = 1f;

    // Settings values (loaded from GameSettings)
    private int _currentLanguage;
    private float _masterVolume = 1f;
    private float _musicVolume = 0.5f;
    private float _mouseSensitivity = 0.5f;
    private float _fieldOfView = 75f;
    private bool _dlssSupported;
    private int _currentDLSSMode;
    private int _currentDLSSPreset;
    private bool _isRTX40OrNewer;
    private float _dlssSharpness = 0.85f;
    private int _currentQualityLevel;
    
    // Effects toggles
    private bool _bloom = true, _filmGrain = true, _vignette = true;
    private bool _motionBlur, _ssao = true, _volumetricFog = true;

    // Display settings
    private bool _fullScreen = true;
    private bool _vSync = true;
    private int _currentResolutionIndex;
    private Resolution[] _availableResolutions;

    // Cached scene references (avoid FindObjectsByType per call)
    private MonoBehaviour _cachedFPSController;
    private bool _fpsControllerSearched;
    private UnityEngine.Rendering.Volume[] _cachedVolumes;
    private System.Reflection.MethodInfo _updateSensitivityMethod;

    // Cached display strings (avoid per-frame GC allocations in OnGUI)
    private string[] _cachedResolutionStrings;
    private string _cachedSensStr;
    private float _lastCachedSens = -1f;
    private string _cachedFovStr;
    private int _lastCachedFov = -1;
    private string _cachedSharpStr;
    private int _lastCachedSharp = -1;
    private string _cachedMasterVolStr;
    private int _lastCachedMasterVol = -1;
    private string _cachedMusicVolStr;
    private int _lastCachedMusicVol = -1;
    private string _cachedSfxVolStr;
    private int _lastCachedSfxVol = -1;
    private string _cachedFpsStr = "-- FPS";
    private string _cachedRenderStr = "";
    private float _fpsUpdateTimer;
    private string _cachedGpuName;

    #endregion

    #region Lifecycle

    // --- INPUT STATE ---
    private enum InputDevice { Keyboard, Mouse }
    private InputDevice _lastInputDevice = InputDevice.Keyboard;
    private Vector3 _lastMousePos;

    private void Awake()
    {
        // CRITICAL INIT
        State.Reset(); // <--- CRITICAL FIX: Ensure clean state on reload

        Time.timeScale = 1f;
        AudioListener.pause = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        
        LoadFonts();
        InitializeResolutions();
        EnsureLocalization();
    }

    private void Start()
    {
        gameObject.name = "General [THE MONOLITH UI]";
        GameBootstrap.EnsureCoreServices();
        
        // Setup audio
        if (GetComponent<AudioSource>() == null)
        {
            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;

            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.playOnAwake = false;
            _musicSource.loop = true;
            _musicSource.volume = 0f;
        }
        else
        {
             _sfxSource = GetComponent<AudioSource>();
             if (_sfxSource == null) _sfxSource = gameObject.AddComponent<AudioSource>();
        }

        if (_musicSource == null)
        {
             _musicSource = gameObject.AddComponent<AudioSource>();
             _musicSource.playOnAwake = false;
             _musicSource.loop = true;
             _musicSource.volume = 0f;
        }

        if (pauseMenuMusic == null)
            pauseMenuMusic = Resources.Load<AudioClip>("Analog Corridor of Teeth");
        if (pauseMenuMusic != null && _musicSource != null)
            _musicSource.clip = pauseMenuMusic;

        LoadSettings();
        CheckDLSS();
        Loc.OnLocaleChanged += () => { _localeNames = Loc.GetLocaleNames(); _currentLanguage = Loc.GetCurrentLocaleIndex(); };

        // FORCE GAMEPLAY STATE
        ResumeGame();
    }

    private System.Action _deferredAction;

    private void OnDestroy()
    {
        // Destroy dynamically created VolumeProfile to prevent memory leak
        if (_blurVolume != null && _blurVolume.profile != null)
        {
            var profile = _blurVolume.profile;
            _blurVolume.profile = null;
            Destroy(profile);
        }

        PauseMenuPanelRenderer.Cleanup();
        PauseMenuButtonRenderer.Cleanup();
        PauseMenuVHSEffects.Cleanup();
        MenuStyles.Cleanup();
    }

    private void Update()
    {
        // 1. Detect Input Device Change
        if ((Input.mousePosition - _lastMousePos).sqrMagnitude > 1.0f)
        {
            _lastInputDevice = InputDevice.Mouse;
            _lastMousePos = Input.mousePosition;
        }
        
        // Detect keyboard activity (simple check)
        if (Input.anyKeyDown && !Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2))
        {
            _lastInputDevice = InputDevice.Keyboard;
        }

        // 2. Execute Deferred Actions
        if (_deferredAction != null)
        {
            _deferredAction.Invoke();
            _deferredAction = null;
        }

        // 3. Main Logic
        HandleInput();
        
        // SYNC REMOVED: It was preventing manual changes
        // Only sync Quality, not DLSS, to avoid fighting with user selection
        if (State.IsSettingsOpen && GraphicsQualityManager.Instance != null)
        {
             _currentQualityLevel = (int)GraphicsQualityManager.Instance.CurrentPreset;
        }
        
        State.UpdateAnimations(Time.unscaledDeltaTime);
        UpdatePauseMusic();
        
        // 4. Cursor Safety
        if (State.IsPaused && !Cursor.visible)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
    }

    #endregion

    #region Input Handling

    private void HandleInput()
    {
        // --- ESC TOGGLE ---
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // Debounce
            if (Time.realtimeSinceStartup - State.LastPauseToggleTime < 0.2f) return;
            State.LastPauseToggleTime = Time.realtimeSinceStartup;

            if (State.IsPaused)
            {
                if (State.IsSettingsOpen)
                {
                    State.IsSettingsOpen = false;
                    PlaySound(selectSound);
                }
                else
                {
                    ResumeGame();
                }
            }
            else
            {
                if (canOpenMenu) 
                    PauseGame();
                else
                    Debug.LogWarning("[PauseMenu] Cannot open menu! canOpenMenu is false.");
            }
            return; 
        }

        // --- KEYBOARD NAVIGATION ---
        // Only active if Paused AND using Keyboard
        if (State.IsPaused && !State.IsSettingsOpen && _lastInputDevice == InputDevice.Keyboard)
        {
            HandleKeyboardNavigation();
        }
    }

    private void HandleKeyboardNavigation()
    {
        if (MenuInput.Up)
        {
            State.SelectedIndex = (State.SelectedIndex - 1 + State.MaxMainButtons) % State.MaxMainButtons;
            PlaySound(hoverSound);
        }
        else if (MenuInput.Down)
        {
            State.SelectedIndex = (State.SelectedIndex + 1) % State.MaxMainButtons;
            PlaySound(hoverSound);
        }
        else if (MenuInput.Confirm)
        {
            ExecuteMainButton(State.SelectedIndex);
        }
    }

    #endregion

    #region Game State Control

    private void PauseGame()
    {
        if (State.IsPaused) return;

        _savedTimeScale = Time.timeScale > 0.01f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        AudioListener.pause = true;
        State.SetPaused(true);

        // Fire events
        GameEvents.InvokeGamePaused(true);
        GameEvents.InvokePauseMenuOpened();

        PausePlayerControlCoordinator.SetPaused(true, this);

        State.SelectedIndex = 0;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        
        StartPauseMusic();
        SetBlur(true);
    }

    private void ResumeGame()
    {
        if (!State.IsPaused) return;

        StopPauseMusic();
        Time.timeScale = _savedTimeScale > 0.01f ? _savedTimeScale : 1f;
        AudioListener.pause = false;
        State.SetPaused(false);

        // Fire events
        GameEvents.InvokeGamePaused(false);
        GameEvents.InvokePauseMenuClosed();

        PausePlayerControlCoordinator.SetPaused(false, this);

        State.IsSettingsOpen = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        SetBlur(false);
    }

    private void LoadFonts()
    {
        // Use centralized font loading from MenuStyles (shared with DemagnetizedMainMenu)
        MenuStyles.EnsureFonts();
        if (titleFont == null) titleFont = MenuStyles.FontTitle;
        if (buttonFont == null) buttonFont = MenuStyles.FontBold;
        if (bodyFont == null) bodyFont = MenuStyles.FontRegular;
    }

    private void ExecuteMainButton(int index)
    {
        PlaySound(selectSound);
        switch (index)
        {
            case 0:
                ResumeGame();
                break;
            case 1:
                State.IsSettingsOpen = true;
                State.SettingsTab = 0;
                break;
            case 2:
                ConfirmationDialog.Show(
                    L.Get("mainmenu"),
                    L.Get("confirm_mainmenu"),
                    () => { ResumeGame(); VHSLoadingScreen.Load(mainMenuSceneName); }
                );
                break;
            case 3:
                ConfirmationDialog.Show(
                    L.Get("quit"),
                    L.Get("confirm_quit"),
                    () =>
                    {
                        Application.Quit();
#if UNITY_EDITOR
                        UnityEditor.EditorApplication.isPlaying = false;
#endif
                    }
                );
                break;
        }
    }

    #endregion

    #region OnGUI Rendering



    private void OnGUI()
    {
        if (State.MenuAlpha <= 0.01f) return;

        MenuStyles.EnsureStyles(); // Shared base styles (cached, recreated on resize)
        GUI.depth = -100;

        float w = Screen.width;
        float h = Screen.height;
        float alpha = State.MenuAlpha;

        // === MONOLITH V6 PIPELINE ===
        
        // 1. Cinematic Background
        PauseMenuPanelRenderer.DrawDarkOverlay(w, h, alpha);
        
        // 2. The Obelisk Panel
        Rect panelRect = PauseMenuPanelRenderer.GetPanelRect(w, h, uiScale);
        PauseMenuPanelRenderer.DrawPanel(panelRect, alpha, Time.unscaledTime, State.ScanlineOffset);
        PauseMenuPanelRenderer.DrawDecals(panelRect, alpha, uiScale, bodyFont); 

        // 3. Paused Watermark (Game Title + Status)
        if (!State.IsSettingsOpen)
        {
            string pausedText = "DEMAGNETIZED"; // Game title - same in all languages
            string subtitleText = L.Get("system_suspended");
            PauseMenuPanelRenderer.DrawPausedIndicator(w, alpha, uiScale, titleFont, pausedText, subtitleText);
        }

        // 4. Content Hosting
        float contentX = panelRect.x + 40 * uiScale;

        if (State.SettingsAlpha > 0.01f)
        {
            GUI.color = new Color(1f, 1f, 1f, State.SettingsAlpha);
            DrawSettings(contentX);
        }

        if (State.SettingsAlpha < 0.99f)
        {
            GUI.color = new Color(1f, 1f, 1f, 1f - State.SettingsAlpha);
            DrawMainMenu(contentX);
        }

        // 5. Final Pass Effects (Subtle)
        if (State.IsPaused)
        {
            GUI.color = new Color(1f, 1f, 1f, alpha);
            PauseMenuVHSEffects.DrawScanlines(panelRect, alpha * 0.25f);
            
            if (State.GlitchTimer > 0) 
                 PauseMenuVHSEffects.DrawGlitchEffect(w, State.GlitchTimer, alpha);
        }

        // 6. Tooltip (Global)
        if (!string.IsNullOrEmpty(State.CurrentTooltip))
        {
             var ts = MenuStyles.S(MenuStyles.StyleBody, (int)(12*uiScale),
                 MenuStyles.WithAlpha(MenuStyles.TextMid, State.TooltipAlpha), TextAnchor.MiddleCenter);
             GUI.Label(new Rect(0, h - 40*uiScale, w, 30), State.CurrentTooltip, ts);
        }

        // 7. Feedback Message (Settings Saved / Defaults Restored)
        if (State.FeedbackAlpha > 0.01f)
        {
            var fs = MenuStyles.S(MenuStyles.StyleBold, Mathf.RoundToInt(14 * uiScale),
                MenuStyles.WithAlpha(S.MechanicGreen, State.FeedbackAlpha), TextAnchor.MiddleCenter);
            GUI.Label(new Rect(0, h - 70 * uiScale, w, 30), State.FeedbackMessage, fs);
        }

        // Cleanup
        State.CurrentTooltip = "";
        GUI.color = Color.white;
    }

    private void DrawMainMenu(float x)
    {
        // Content starts lower to make room for "PAUSED" big title
        float y = Screen.height * 0.4f; 
        float alpha = (1f - State.SettingsAlpha) * State.MenuAlpha;
        float buttonW = 450 * uiScale; 

        // Buttons (Underline style - Centered)
        DrawMenuButton(x, ref y, buttonW, L.Get("resume"), 0, alpha, false);
        DrawMenuButton(x, ref y, buttonW, L.Get("settings"), 1, alpha, false);
        DrawMenuButton(x, ref y, buttonW, L.Get("mainmenu"), 2, alpha, false);
        DrawMenuButton(x, ref y, buttonW, L.Get("quit"), 3, alpha, true);
    }

    private void DrawMenuButton(float x, ref float y, float w, string text, int index, float alpha, bool isDanger)
    {
        bool clicked = PauseMenuButtonRenderer.DrawButton(
            x, ref y, w, text, index, State.SelectedIndex, 
            State.ButtonAnimProgress[index], 
            alpha, uiScale, buttonFont, isDanger,
            onClick: () => ExecuteMainButton(index),
            onHover: () => {
                if (State.SelectedIndex != index)
                {
                    State.SelectedIndex = index;
                    PlaySound(hoverSound);
                }
            }
        );
        
        if (clicked) PlaySound(selectSound);
    }

    private void DrawSettings(float x)
    {
        float y = Screen.height * 0.12f; 
        float w = 450 * uiScale; 
        float alpha = State.SettingsAlpha * State.MenuAlpha;
        var tex = MenuStyles.SolidTexture;

        // === LUXE HEADER (Matching main menu style) ===
        string title = L.Get("settings");

        var titleStyle = MenuStyles.S(MenuStyles.StyleTitle, Mathf.RoundToInt(48 * uiScale),
            new Color(1f, 1f, 1f, alpha), TextAnchor.MiddleLeft);
        titleStyle.fontStyle = FontStyle.Bold;

        // Shadow
        GUI.color = new Color(0, 0, 0, 0.4f * alpha);
        GUI.Label(new Rect(x + 2, y + 2, w, 60), title, titleStyle);
        // Main
        GUI.color = Color.white;
        titleStyle.normal.textColor = Color.white * alpha;
        GUI.Label(new Rect(x, y, w, 60), title, titleStyle);

        // Amber accent underline (matching main menu style)
        var accent = S.Amber;
        GUI.color = MenuStyles.WithAlpha(accent, 0.85f * alpha);
        float titleWidth = title.Length * 24f * uiScale;
        GUI.DrawTexture(new Rect(x, y + 55, Mathf.Min(titleWidth, 220), 3), tex);

        // Decorative dot
        GUI.color = MenuStyles.WithAlpha(accent, alpha);
        GUI.DrawTexture(new Rect(x + Mathf.Min(titleWidth, 220) + 8, y + 52, 5, 9), tex);

        // Subtitle (italic, matching DemagnetizedMainMenu style)
        y += 68 * uiScale;
        var subStyle = MenuStyles.S(MenuStyles.StyleLight, Mathf.RoundToInt(11 * uiScale),
            MenuStyles.WithAlpha(S.TextMid, alpha * 0.8f));
        subStyle.fontStyle = FontStyle.Italic;
        GUI.Label(new Rect(x, y, w, 20), L.Get("configure_options"), subStyle);
        
        GUI.color = Color.white;
        y += 30 * uiScale;

        // === LUXE TAB BAR ===
        string[] tabs = { L.Get("tab_game"), L.Get("tab_graphics"), L.Get("tab_effects"), L.Get("tab_audio") };
        
        float tabW = w / 4f;
        float tabH = 42 * uiScale;
        
        // Tab background bar
        GUI.color = new Color(1f, 1f, 1f, 0.03f * alpha);
        GUI.DrawTexture(new Rect(x, y, w, tabH), tex);
        GUI.color = Color.white;

        for (int i = 0; i < tabs.Length; i++)
        {
            Rect tr = new Rect(x + i * tabW, y, tabW, tabH);
            bool sel = State.SettingsTab == i;
            bool hov = tr.Contains(Event.current.mousePosition);

            // Only selected tab gets background highlight
            if (sel)
            {
                GUI.color = new Color(accent.r, accent.g, accent.b, 0.12f * alpha);
                GUI.DrawTexture(tr, tex);
            }
            // Hover: NO background - just text color change (less aggressive)

            // Tab text (using shared styles)
            Color tabColor = sel ? accent : (hov ? MenuStyles.TextMain : S.DustyGray);
            var tabStyle = MenuStyles.S(sel ? MenuStyles.StyleBold : MenuStyles.StyleBody,
                Mathf.RoundToInt((sel ? 15 : 13) * uiScale),
                MenuStyles.WithAlpha(tabColor, alpha), TextAnchor.MiddleCenter);
            if (sel) tabStyle.fontStyle = FontStyle.Bold;
            GUI.color = Color.white;
            GUI.Label(tr, tabs[i], tabStyle);

            // Click handling
            if (hov && Event.current.type == EventType.MouseDown && !sel)
            {
                State.SettingsTab = i;
                PlaySound(selectSound);
                Event.current.Use();
            }
        }

        // Animated tab indicator (bottom line)
        float indicatorX = x + State.TabPosition * tabW;
        GUI.color = new Color(accent.r, accent.g, accent.b, 0.9f * alpha);
        GUI.DrawTexture(new Rect(indicatorX + 8, y + tabH - 3, tabW - 16, 3), tex);

        y += tabH + 25 * uiScale;
        GUI.color = Color.white;

        // Draw current tab
        switch (State.SettingsTab)
        {
            case 0: DrawGameTab(x, y, w, alpha); break;
            case 1: DrawGfxTab(x, y, w, alpha); break;
            case 2: DrawFxTab(x, y, w, alpha); break;
            case 3: DrawAudioTab(x, y, w, alpha); break;
        }

        // Back button
        float backY = Screen.height - 85 * uiScale;
        Rect backR = new Rect(x, backY, 140 * uiScale, 42 * uiScale);
        if (PauseMenuButtonRenderer.DrawSimpleButton(backR, "◀ " + L.Get("back"), alpha, uiScale, buttonFont))
        {
            State.IsSettingsOpen = false;
            PlayerPrefs.Save();
            State.ShowFeedback(L.Get("settings_saved"));
            PlaySound(selectSound);
        }

        // Reset button
        string resetText = L.Get("reset");
        Rect resetR = new Rect(x + 160 * uiScale, backY, 140 * uiScale, 42 * uiScale);
        bool resetHov = resetR.Contains(Event.current.mousePosition);
        if (resetHov)
        {
            GUI.color = MenuStyles.WithAlpha(S.Danger, 0.1f * alpha);
            GUI.DrawTexture(resetR, tex);
        }
        Color resetCol = resetHov ? S.Danger : S.MetalGray;
        var resetStyle = MenuStyles.S(MenuStyles.StyleBold, Mathf.RoundToInt(14 * uiScale),
            MenuStyles.WithAlpha(resetCol, alpha), TextAnchor.MiddleCenter);
        GUI.color = Color.white;
        GUI.Label(resetR, "↺ " + resetText, resetStyle);

        if (resetHov && Event.current.type == EventType.MouseDown)
        {
            ResetToDefaults();
            PlaySound(selectSound);
            Event.current.Use();
        }
    }

    private void DrawGameTab(float x, float y, float w, float alpha)
    {
        var tex = MenuStyles.SolidTexture;
        float sp = 55 * uiScale;
        
        // Section: Language
        DrawSettingLabel(x, y, L.Get("language"), alpha);
        y += 24 * uiScale;

        if (_localeNames == null || _localeNames.Length == 0) _localeNames = Loc.GetLocaleNames();
        int langCount = _localeNames.Length;
        if (langCount == 0) { _localeNames = new[] { "English" }; langCount = 1; }
        if (_currentLanguage >= langCount) _currentLanguage = 0;
        y = DrawLanguageGrid(x, y, w, alpha);

        y += 15 * uiScale;
        y = DrawSeparator(x, y, w, alpha);

        // Section: Controls
        DrawSettingLabel(x, y, L.Get("tab_controls"), alpha);
        y += 26 * uiScale;
        
        // Mouse Sensitivity
        DrawSettingLabel(x, y, L.Get("sensitivity"), alpha);
        y += 4 * uiScale;
        DrawSubLabel(x, y, L.Get("hint_mouse_speed"), alpha * 0.5f);
        y += 20 * uiScale;
        if (_mouseSensitivity != _lastCachedSens) { _lastCachedSens = _mouseSensitivity; _cachedSensStr = _mouseSensitivity.ToString("F2"); }
        DrawSlider(x, y, w, ref _mouseSensitivity, v => {
            PlayerPrefs.SetFloat(GameSettings.Keys.MOUSE_SENSITIVITY, v);
            ApplySensitivity();
        }, _cachedSensStr, alpha);
        
        y += sp;
        
        // Separator
        y = DrawSeparator(x, y, w, alpha);

        // Section: Camera
        DrawSettingLabel(x, y, L.Get("camera"), alpha);
        y += 26 * uiScale;
        
        // Field of View
        DrawSettingLabel(x, y, L.Get("fov"), alpha);
        y += 4 * uiScale;
        DrawSubLabel(x, y, L.Get("hint_fov"), alpha * 0.5f);
        y += 20 * uiScale;
        DrawSlider(x, y, w, ref _fieldOfView, v => {
            PlayerPrefs.SetFloat(GameSettings.Keys.FIELD_OF_VIEW, v);
            ApplyFOV();
        }, CacheFov(), alpha, 60f, 110f);
        
        y += sp;
        
        // FOV Preview Text
        string fovDesc = _fieldOfView switch {
            < 70 => L.Get("fov_narrow"),
            < 85 => L.Get("fov_standard"),
            < 100 => L.Get("fov_wide"),
            _ => L.Get("fov_ultrawide")
        };
        DrawSubLabel(x, y, fovDesc, alpha * 0.6f);
    }

    private void DrawGfxTab(float x, float y, float w, float alpha)
    {
        float sp = 50 * uiScale;

        DrawGfxLiveStats(x, y, w, alpha);
        y += 45 * uiScale;
        y = DrawSeparator(x, y, w, alpha);
        y = DrawGfxDisplaySection(x, y, w, sp, alpha);
        y = DrawSeparator(x, y, w, alpha);
        y = DrawGfxQualitySection(x, y, w, sp, alpha);
        DrawGfxDLSSSection(x, y, w, sp, alpha);
    }

    private float DrawGfxDisplaySection(float x, float y, float w, float sp, float alpha)
    {
        DrawSettingLabel(x, y, L.Get("display"), alpha);
        y += 24 * uiScale;

        if (_cachedResolutionStrings != null && _cachedResolutionStrings.Length > 0)
        {
            DrawSelector(x, y, w, L.Get("resolution"), _cachedResolutionStrings, _currentResolutionIndex, i => {
                _currentResolutionIndex = i;
                ApplyResolution();
            }, alpha);
        }
        y += sp;

        float half = w * 0.48f;
        DrawToggleWithHint(x, y, half, L.Get("fullscreen"), ref _fullScreen, GameSettings.Keys.FULLSCREEN,
            L.Get("hint_fullscreen"), alpha);
        if (_fullScreen != Screen.fullScreen) ApplyResolution();

        bool vsyncVal = QualitySettings.vSyncCount > 0;
        DrawToggleWithHint(x + half + 20, y, half, L.Get("vsync"), ref vsyncVal, GameSettings.Keys.VSYNC,
            L.Get("hint_vsync"), alpha);
        if (vsyncVal != (QualitySettings.vSyncCount > 0)) SetVSync(vsyncVal);

        y += sp + 20 * uiScale;
        return y;
    }

    private float DrawGfxQualitySection(float x, float y, float w, float sp, float alpha)
    {
        DrawSettingLabel(x, y, L.Get("quality"), alpha);
        y += 24 * uiScale;

        string[] qualityLabels = {
            L.Get("quality_high"),
            L.Get("quality_medium"),
            L.Get("quality_low")
        };
        DrawOptions(x, y, w, qualityLabels, _currentQualityLevel,
            i => { _currentQualityLevel = i; _deferredAction = () => SetQuality(i); }, alpha);

        y += 32 * uiScale;
        string qualityDesc = _currentQualityLevel switch {
            0 => L.Get("quality_desc_high"),
            1 => L.Get("quality_desc_balanced"),
            2 => L.Get("quality_desc_performance"),
            _ => ""
        };
        DrawSubLabel(x, y, qualityDesc, alpha * 0.6f);
        y += sp;
        return y;
    }

    private void DrawGfxDLSSSection(float x, float y, float w, float sp, float alpha)
    {
        if (_dlssSupported)
        {
            DrawSettingLabel(x, y, L.Get("dlss_mode"), alpha);
            y += 24 * uiScale;

            DrawOptions(x, y, w, DlssLabels, _currentDLSSMode,
                i => { _currentDLSSMode = i; _deferredAction = () => SetDLSS(i); }, alpha);

            y += 32 * uiScale;
            string dlssDesc = _currentDLSSMode switch {
                0 => L.Get("dlss_desc_off"),
                1 => L.Get("dlss_desc_dlaa"),
                2 => L.Get("dlss_desc_quality"),
                3 => L.Get("dlss_desc_balanced"),
                4 => L.Get("dlss_desc_performance"),
                5 => L.Get("dlss_desc_ultraperf"),
                _ => ""
            };
            DrawSubLabel(x, y, dlssDesc, alpha * 0.6f);

            if (_currentDLSSMode > 0)
            {
                y += 20 * uiScale;
                string modelInfo = _isRTX40OrNewer
                    ? L.Get("dlss_model_m")
                    : L.Get("dlss_model_k");
                DrawSubLabel(x, y, modelInfo, alpha * 0.5f);
            }

            if (_currentDLSSMode > 0)
            {
                y += 28 * uiScale;
                DrawSettingLabel(x, y, L.Get("sharpness"), alpha);
                y += 24 * uiScale;
                DrawSlider(x, y, w, ref _dlssSharpness, v => {
                    if (WorkingDLSSManager.Instance != null)
                        WorkingDLSSManager.Instance.DLSSSharpness = v;
                }, CachePercent(ref _cachedSharpStr, ref _lastCachedSharp, _dlssSharpness), alpha);
            }
        }
        else
        {
            y += 10 * uiScale;
            var noStyle = MenuStyles.S(MenuStyles.StyleBody, Mathf.RoundToInt(12 * uiScale),
                MenuStyles.WithAlpha(S.Danger, alpha));
            noStyle.fontStyle = FontStyle.Italic;
            GUI.Label(new Rect(x, y, w, 20), L.Get("dlss_not_supported"), noStyle);
        }
    }

    private float DrawSeparator(float x, float y, float w, float alpha)
    {
        GUI.color = PauseMenuConfig.SeparatorColor * new Color(1, 1, 1, alpha);
        GUI.DrawTexture(new Rect(x, y, w, 1), MenuStyles.SolidTexture);
        GUI.color = Color.white;
        return y + 15 * uiScale;
    }
    
    private void DrawGfxLiveStats(float x, float y, float w, float alpha)
    {
        // FPS Counter (update at 4Hz to reduce string allocations)
        float fps = 1f / Time.unscaledDeltaTime;
        _fpsUpdateTimer -= Time.unscaledDeltaTime;
        if (_fpsUpdateTimer <= 0f) { _fpsUpdateTimer = 0.25f; _cachedFpsStr = $"{fps:F0} FPS"; }

        Color fpsColor = fps >= 60 ? S.MechanicGreen : (fps >= 30 ? S.Amber : S.Danger);
        var fpsStyle = MenuStyles.S(MenuStyles.StyleBold, Mathf.RoundToInt(16 * uiScale),
            MenuStyles.WithAlpha(fpsColor, alpha));
        GUI.Label(new Rect(x, y, 100, 20), _cachedFpsStr, fpsStyle);

        // GPU Name (cached on first use)
        if (_cachedGpuName == null)
        {
            _cachedGpuName = SystemInfo.graphicsDeviceName;
            if (_cachedGpuName.Length > 25) _cachedGpuName = _cachedGpuName.Substring(0, 22) + "...";
        }
        var gpuStyle = MenuStyles.S(MenuStyles.StyleBody, Mathf.RoundToInt(11 * uiScale),
            MenuStyles.WithAlpha(MenuStyles.TextDim, alpha), TextAnchor.MiddleRight);
        GUI.Label(new Rect(x, y, w, 20), _cachedGpuName, gpuStyle);

        // Resolution Scale (second line, cached)
        y += 18 * uiScale;
        if (_cachedRenderStr == null || _cachedRenderStr.Length == 0)
        {
            string resScale = _currentDLSSMode switch {
                0 => "100%", 1 => "100%", 2 => "66%", 3 => "58%", 4 => "50%", 5 => "33%", _ => "100%"
            };
            _cachedRenderStr = $"{L.Get("render_label")}: {resScale} \u2192 {Screen.width}x{Screen.height}";
        }
        var resStyle = MenuStyles.S(MenuStyles.StyleBody, Mathf.RoundToInt(10 * uiScale),
            MenuStyles.WithAlpha(MenuStyles.MetalGray, alpha));
        GUI.Label(new Rect(x, y, w, 16), _cachedRenderStr, resStyle);
    }
    
    private void DrawSubLabel(float x, float y, string text, float alpha)
    {
        var style = MenuStyles.S(MenuStyles.StyleLight, Mathf.RoundToInt(10 * uiScale),
            MenuStyles.WithAlpha(MenuStyles.TextDim, alpha));
        style.fontStyle = FontStyle.Italic;
        GUI.Label(new Rect(x, y, 400, 16), text, style);
    }

    private void DrawFxTab(float x, float y, float w, float alpha)
    {
        var tex = MenuStyles.SolidTexture;
        float sp = 38 * uiScale;
        float half = w * 0.48f;
        
        // Section: Visual Effects
        DrawSettingLabel(x, y, L.Get("visual_effects"), alpha);
        y += 26 * uiScale;
        
        DrawToggleWithHint(x, y, half, L.Get("bloom"), ref _bloom, "FX_Bloom",
            L.Get("hint_bloom"), alpha);
        DrawToggleWithHint(x + half + 20, y, half, L.Get("vignette"), ref _vignette, "FX_Vignette",
            L.Get("hint_vignette"), alpha);
        y += sp;
        
        DrawToggleWithHint(x, y, half, L.Get("filmgrain"), ref _filmGrain, "FX_FilmGrain",
            L.Get("hint_filmgrain"), alpha);
        DrawToggleWithHint(x + half + 20, y, half, L.Get("motionblur"), ref _motionBlur, "FX_MotionBlur",
            L.Get("hint_motionblur"), alpha);
        
        y += sp + 20 * uiScale;
        
        y = DrawSeparator(x, y, w, alpha);

        // Section: Performance Effects
        DrawSettingLabel(x, y, L.Get("performance_effects"), alpha);
        y += 8 * uiScale;
        DrawSubLabel(x, y, L.Get("hint_perf_effects"), alpha * 0.5f);
        y += 22 * uiScale;
        
        DrawToggleWithHint(x, y, half, L.Get("ssao"), ref _ssao, "FX_SSAO",
            L.Get("hint_ssao"), alpha);
        DrawToggleWithHint(x + half + 20, y, half, L.Get("volumetricfog"), ref _volumetricFog, "FX_VolumetricFog",
            L.Get("hint_volumetricfog"), alpha);
    }
    
    private void DrawToggleWithHint(float x, float y, float w, string label, ref bool val, string key, string hint, float alpha)
    {
        DrawToggle(x, y, w, label, ref val, key, alpha);
        // Hint below toggle
        var hintStyle = MenuStyles.S(MenuStyles.StyleLight, Mathf.RoundToInt(9 * uiScale),
            MenuStyles.WithAlpha(MenuStyles.MetalGray, alpha * 0.7f));
        GUI.Label(new Rect(x, y + 22 * uiScale, w, 14), hint, hintStyle);
    }

    private void DrawAudioTab(float x, float y, float w, float alpha)
    {
        var tex = MenuStyles.SolidTexture;
        float sp = 55 * uiScale;
        
        // Master Volume
        DrawSettingLabel(x, y, L.Get("master_volume"), alpha);
        y += 24 * uiScale;
        DrawSlider(x, y, w, ref _masterVolume, v => {
            AudioListener.volume = v;
            PlayerPrefs.SetFloat(GameSettings.Keys.MASTER_VOLUME, v);
        }, CachePercent(ref _cachedMasterVolStr, ref _lastCachedMasterVol, _masterVolume), alpha);
        
        y += sp;
        
        // Music Volume
        DrawSettingLabel(x, y, L.Get("music"), alpha);
        y += 24 * uiScale;
        DrawSlider(x, y, w, ref _musicVolume, v => {
            PlayerPrefs.SetFloat(GameSettings.Keys.MUSIC_VOLUME, v);
        }, CachePercent(ref _cachedMusicVolStr, ref _lastCachedMusicVol, _musicVolume), alpha);
        
        y += sp;
        
        // SFX Volume
        DrawSettingLabel(x, y, L.Get("sound_effects"), alpha);
        y += 24 * uiScale;
        float sfxVol = PlayerPrefs.GetFloat(GameSettings.Keys.SFX_VOLUME, 1f);
        DrawSlider(x, y, w, ref sfxVol, v => {
            PlayerPrefs.SetFloat(GameSettings.Keys.SFX_VOLUME, v);
        }, CachePercent(ref _cachedSfxVolStr, ref _lastCachedSfxVol, sfxVol), alpha);
        
        y += sp + 10 * uiScale;
        
        // Audio Info
        var infoStyle = MenuStyles.S(MenuStyles.StyleBody, Mathf.RoundToInt(10 * uiScale),
            MenuStyles.WithAlpha(MenuStyles.MetalGray, alpha * 0.7f));
        infoStyle.fontStyle = FontStyle.Italic;
        string audioInfo = $"{L.Get("sample_rate")}: {AudioSettings.outputSampleRate} Hz";
        GUI.Label(new Rect(x, y, w, 16), audioInfo, infoStyle);
    }

    private void DrawSettingLabel(float x, float y, string text, float alpha)
    {
        var tex = MenuStyles.SolidTexture;
        var accent = S.Amber;

        // Small accent line before text
        GUI.color = MenuStyles.WithAlpha(accent, 0.6f * alpha);
        GUI.DrawTexture(new Rect(x, y + 5, 3, 10), tex);

        // Label text
        var s = MenuStyles.S(MenuStyles.StyleBold, Mathf.RoundToInt(12 * uiScale),
            MenuStyles.WithAlpha(MenuStyles.TextMid, alpha));
        s.fontStyle = FontStyle.Bold;
        GUI.Label(new Rect(x + 12, y, 350, 20), text, s);

        GUI.color = Color.white;
    }

    private void DrawSlider(float x, float y, float w, ref float val, System.Action<float> cb, string display, float alpha, float min = 0f, float max = 1f)
    {
        var tex = MenuStyles.SolidTexture;
        float valW = 75 * uiScale;
        float slW = w - valW - 30;
        float nv = Mathf.InverseLerp(min, max, val);
        
        // Track position
        float trackH = 6 * uiScale;
        float ty = y + 12 * uiScale;
        
        // Hover detection
        Rect intR = new Rect(x - 5, y - 5, slW + 10, 35 * uiScale);
        bool isHovered = intR.Contains(Event.current.mousePosition);
        
        // === PREMIUM SLIDER VISUALS ===
        
        // Track background
        GUI.color = MenuStyles.WithAlpha(MenuStyles.FilmBrown, 0.8f * alpha);
        GUI.DrawTexture(new Rect(x, ty, slW, trackH), tex);
        
        // Fill gradient (Amber)
        var accent = S.Amber;
        float fillW = slW * nv;
        
        // Glow under fill (subtle)
        if (fillW > 2)
        {
            GUI.color = new Color(accent.r, accent.g, accent.b, 0.15f * alpha);
            GUI.DrawTexture(new Rect(x, ty - 2, fillW, trackH + 4), tex);
        }
        
        // Main fill
        GUI.color = new Color(accent.r, accent.g, accent.b, 0.95f * alpha);
        GUI.DrawTexture(new Rect(x, ty, fillW, trackH), tex);
        
        // Handle
        float handleSize = (isHovered ? 16 : 14) * uiScale;
        float handleX = x + fillW - handleSize / 2;
        float handleY = ty + trackH / 2 - handleSize / 2;
        
        // Handle glow
        GUI.color = new Color(accent.r, accent.g, accent.b, 0.3f * alpha);
        GUI.DrawTexture(new Rect(handleX - 3, handleY - 3, handleSize + 6, handleSize + 6), tex);
        
        // Handle core
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(new Rect(handleX, handleY, handleSize, handleSize), tex);
        
        // Handle inner (accent colored)
        GUI.color = new Color(accent.r, accent.g, accent.b, 0.9f * alpha);
        float innerSize = handleSize * 0.5f;
        GUI.DrawTexture(new Rect(handleX + (handleSize - innerSize) / 2, handleY + (handleSize - innerSize) / 2, innerSize, innerSize), tex);

        // Value Text (Right side)
        var vs = MenuStyles.S(MenuStyles.StyleBold, Mathf.RoundToInt(20 * uiScale),
            MenuStyles.WithAlpha(MenuStyles.TextMain, alpha), TextAnchor.MiddleRight);
        vs.fontStyle = FontStyle.Bold;
        GUI.color = Color.white;
        GUI.Label(new Rect(x + slW + 15, y - 2, valW, 35 * uiScale), display, vs);

        // Interaction
        if ((Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag) && intR.Contains(Event.current.mousePosition))
        {
            float newVal = Mathf.Lerp(min, max, Mathf.Clamp01((Event.current.mousePosition.x - x) / slW));
            if (Mathf.Abs(newVal - val) > 0.002f) { val = newVal; cb?.Invoke(val); }
        }
    }

    private void DrawOptions(float x, float y, float w, string[] opts, int sel, System.Action<int> cb, float alpha)
    {
        var tex = MenuStyles.SolidTexture;
        var accent = S.Amber;
        
        // Calculate option width with gaps
        float gap = 8 * uiScale;
        float ow = (w - gap * (opts.Length - 1)) / opts.Length;
        float oh = 36 * uiScale;

        for (int i = 0; i < opts.Length; i++)
        {
            float ox = x + i * (ow + gap);
            Rect r = new Rect(ox, y, ow, oh);
            bool isSelected = sel == i;
            bool isHovered = r.Contains(Event.current.mousePosition);

            // === PREMIUM CARD STYLE ===
            
            // Background
            if (isSelected)
            {
                // Selected: Orange tinted background
                GUI.color = new Color(accent.r, accent.g, accent.b, 0.15f * alpha);
                GUI.DrawTexture(r, tex);
                
                // Top accent line
                GUI.color = new Color(accent.r, accent.g, accent.b, 0.9f * alpha);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width, 3), tex);
            }
            else if (isHovered)
            {
                // Hovered: Subtle white tint (indicating clickable)
                GUI.color = new Color(1f, 1f, 1f, 0.08f * alpha);
                GUI.DrawTexture(r, tex);
                
                // Top line hint
                GUI.color = new Color(1f, 1f, 1f, 0.3f * alpha);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), tex);
            }
            // Normal state: NO background - clean and non-misleading
            
            // Text (using shared styles)
            Color textColor = isSelected ? accent : (isHovered ? MenuStyles.TextMain : S.DustyGray);
            var st = MenuStyles.S(isSelected ? MenuStyles.StyleBold : MenuStyles.StyleBody,
                Mathf.RoundToInt((isSelected ? 15 : 13) * uiScale),
                MenuStyles.WithAlpha(textColor, alpha), TextAnchor.MiddleCenter);
            if (isSelected) st.fontStyle = FontStyle.Bold;
            
            GUI.color = Color.white;
            GUI.Label(r, opts[i], st);

            // Click handling
            if (isHovered && Event.current.type == EventType.MouseDown)
            {
                PlaySound(selectSound);
                cb?.Invoke(i);
                Event.current.Use();
            }
        }
        
        GUI.color = Color.white;
    }

    private float DrawLanguageGrid(float x, float y, float w, float alpha)
    {
        var tex = MenuStyles.SolidTexture;
        var accent = S.Amber;
        int cols = 4;
        float gap = 5 * uiScale;
        float btnW = (w - gap * (cols - 1)) / cols;
        float btnH = 28 * uiScale;
        float rowGap = 3 * uiScale;
        int count = _localeNames.Length;

        for (int i = 0; i < count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            float bx = x + col * (btnW + gap);
            float by = y + row * (btnH + rowGap);
            Rect br = new Rect(bx, by, btnW, btnH);
            bool sel = (i == _currentLanguage);
            bool hov = br.Contains(Event.current.mousePosition);

            // Selected: amber tint + top accent line
            if (sel)
            {
                GUI.color = new Color(accent.r, accent.g, accent.b, 0.12f * alpha);
                GUI.DrawTexture(br, tex);
                GUI.color = new Color(accent.r, accent.g, accent.b, 0.9f * alpha);
                GUI.DrawTexture(new Rect(bx, by, btnW, 2), tex);
            }
            else if (hov)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.04f * alpha);
                GUI.DrawTexture(br, tex);
            }

            // Subtle bottom border only
            float borderA = sel ? 0.3f : (hov ? 0.12f : 0.06f);
            Color bc = sel ? new Color(accent.r, accent.g, accent.b, borderA * alpha)
                          : new Color(1f, 1f, 1f, borderA * alpha);
            GUI.color = bc;
            GUI.DrawTexture(new Rect(bx, by + btnH - 1, btnW, 1), tex);
            GUI.color = Color.white;

            // Text
            Color tc = sel ? accent : (hov ? MenuStyles.TextMain : S.DustyGray);
            var st = MenuStyles.S(sel ? MenuStyles.StyleBold : MenuStyles.StyleBody,
                Mathf.RoundToInt((sel ? 12 : 11) * uiScale),
                MenuStyles.WithAlpha(tc, alpha), TextAnchor.MiddleCenter);
            GUI.Label(br, _localeNames[i], st);

            // Click
            if (hov && Event.current.type == EventType.MouseDown)
            {
                _currentLanguage = i;
                Loc.SetLocale(i);
                PlaySound(selectSound);
                Event.current.Use();
            }
        }

        int totalRows = (count + cols - 1) / cols;
        return y + totalRows * (btnH + rowGap);
    }

    private void DrawToggle(float x, float y, float w, string label, ref bool val, string key, float alpha)
    {
        var tex = MenuStyles.SolidTexture;
        
        // Switch area for hover detection (only the switch, not whole row)
        float swW = 48 * uiScale;
        float swH = 24 * uiScale;
        float swX = x + w - swW - 5;
        float swY = y + 1;
        
        Rect switchArea = new Rect(swX - 10, y - 5, swW + 20, swH + 10);
        bool isHovered = switchArea.Contains(Event.current.mousePosition);
        
        // Whole row is clickable
        Rect clickArea = new Rect(x, y, w, 28 * uiScale);
        bool isRowHovered = clickArea.Contains(Event.current.mousePosition);
        
        // Label (no hover effect - static)
        var ls = MenuStyles.S(MenuStyles.StyleBody, Mathf.RoundToInt(14 * uiScale),
            MenuStyles.WithAlpha(MenuStyles.TextMid, alpha));
        GUI.Label(new Rect(x, y + 2, w - 100, 24 * uiScale), label, ls);

        // === PREMIUM SWITCH ===

        // Switch track
        Color trackColor = val ? S.MechanicGreen : S.MetalGray;
        float trackAlpha = isHovered ? 0.8f : 0.6f; // Brighter on hover
        GUI.color = new Color(trackColor.r, trackColor.g, trackColor.b, trackAlpha * alpha);
        GUI.DrawTexture(new Rect(swX, swY, swW, swH), tex);
        
        // Track glow when ON or hovered
        if (val || isHovered)
        {
            Color glowColor = val ? S.MechanicGreen : Color.white;
            GUI.color = new Color(glowColor.r, glowColor.g, glowColor.b, (val ? 0.25f : 0.1f) * alpha);
            GUI.DrawTexture(new Rect(swX - 2, swY - 2, swW + 4, swH + 4), tex);
        }
        
        // Switch handle (knob)
        float handleW = swH - 4 * uiScale;
        float handleX = val ? (swX + swW - handleW - 2 * uiScale) : (swX + 2 * uiScale);
        float handleY = swY + 2 * uiScale;
        
        // Handle shadow
        GUI.color = new Color(0f, 0f, 0f, 0.3f * alpha);
        GUI.DrawTexture(new Rect(handleX + 1, handleY + 1, handleW, handleW), tex);
        
        // Handle main (slightly bigger on hover)
        float handleScale = isHovered ? 1.1f : 1f;
        float scaledHandleW = handleW * handleScale;
        float handleOffset = (scaledHandleW - handleW) / 2;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(new Rect(handleX - handleOffset, handleY - handleOffset, scaledHandleW, scaledHandleW), tex);
        
        // Handle inner (accent when on)
        if (val)
        {
            GUI.color = new Color(S.MechanicGreen.r, S.MechanicGreen.g, S.MechanicGreen.b, 0.5f * alpha);
            float innerSize = handleW * 0.4f;
            GUI.DrawTexture(new Rect(handleX + (handleW - innerSize) / 2, handleY + (handleW - innerSize) / 2, innerSize, innerSize), tex);
        }

        GUI.color = Color.white;
        
        // Interaction (whole row clickable)
        if (isRowHovered && Event.current.type == EventType.MouseDown)
        {
            val = !val; 
            PlayerPrefs.SetInt(key, val ? 1 : 0); 
            ApplyFx(); 
            PlaySound(selectSound); 
            Event.current.Use();
        }
    }

    #endregion

    #region Utility Methods



    private void EnsureLocalization()
    {
        GameBootstrap.EnsureCoreServices();
        _currentLanguage = Loc.GetCurrentLocaleIndex();
        _localeNames = Loc.GetLocaleNames();
    }

    private void LoadSettings()
    {
        var gs = GameSettings.Instance;
        
        // 1. Load Audio/Input Settings
        if (gs != null)
        {
            _masterVolume = gs.MasterVolume;
            _musicVolume = gs.MusicVolume;
            _mouseSensitivity = gs.MouseSensitivity;
            _fieldOfView = gs.FieldOfView;
            _currentDLSSMode = gs.DLSSMode;
        }
        else
        {
            _masterVolume = PlayerPrefs.GetFloat(GameSettings.Keys.MASTER_VOLUME, 1f);
            _musicVolume = PlayerPrefs.GetFloat(GameSettings.Keys.MUSIC_VOLUME, 0.5f);
            _mouseSensitivity = PlayerPrefs.GetFloat(GameSettings.Keys.MOUSE_SENSITIVITY, 0.5f);
            _fieldOfView = PlayerPrefs.GetFloat(GameSettings.Keys.FIELD_OF_VIEW, 75f);
            _currentDLSSMode = PlayerPrefs.GetInt(GameSettings.Keys.DLSS_MODE, 0);
        }

        // 2. Sync Quality Level State
        var graphics = ServiceLocator.Instance != null ? ServiceLocator.Instance.GraphicsQuality : GraphicsQualityManager.Instance;
        if (graphics != null)
        {
            _currentQualityLevel = (int)graphics.CurrentPreset;
        }
        else
        {
            // Fallback to Unity's Quality Settings
            _currentQualityLevel = QualitySettings.GetQualityLevel();
        }

        AudioListener.volume = _masterVolume;
    }

    private void CheckDLSS()
    {
        var dlss = ServiceLocator.Instance != null ? ServiceLocator.Instance.DLSS : WorkingDLSSManager.Instance;
        _dlssSupported = dlss != null && dlss.IsDLSSSupported;
        if (_dlssSupported && dlss != null)
        {
            _currentDLSSMode = (int)dlss.CurrentMode;
            _currentDLSSPreset = (int)dlss.CurrentPreset;
            _isRTX40OrNewer = dlss.IsRTX40OrNewer;
            _dlssSharpness = dlss.DLSSSharpness;
        }
    }

    private UnityEngine.Rendering.Volume _blurVolume;
    private UnityEngine.Rendering.HighDefinition.DepthOfField _blurDoF;

    private void SetBlur(bool enabled)
    {
        // Create blur volume on first use (lazy initialization)
        if (_blurVolume == null)
        {
            CreateBlurVolume();
        }
        
        if (_blurVolume != null)
        {
            _blurVolume.enabled = enabled;
        }
    }
    
    private void CreateBlurVolume()
    {
        // Create a new GameObject for our blur volume
        var blurGO = new GameObject("PauseMenu_BlurVolume");
        blurGO.transform.SetParent(this.transform);
        
        // Add Volume component
        _blurVolume = blurGO.AddComponent<UnityEngine.Rendering.Volume>();
        _blurVolume.isGlobal = true;
        _blurVolume.priority = 100; // High priority to override other volumes
        _blurVolume.weight = 1f;
        
        // Create a new VolumeProfile
        var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
        _blurVolume.profile = profile;
        
        // Add DepthOfField effect with AAA-quality settings
        _blurDoF = profile.Add<UnityEngine.Rendering.HighDefinition.DepthOfField>(true);
        _blurDoF.focusMode.Override(UnityEngine.Rendering.HighDefinition.DepthOfFieldMode.Manual);
        
        // Optimized blur settings for AAA look
        // Near focus: Everything very close is in focus (prevents UI blur)
        _blurDoF.nearFocusStart.Override(0.0f);
        _blurDoF.nearFocusEnd.Override(0.01f);
        
        // Far focus: Background blur starts immediately
        _blurDoF.farFocusStart.Override(0.5f);
        _blurDoF.farFocusEnd.Override(8f);
        
        // Start disabled
        _blurVolume.enabled = false;
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip == null || _sfxSource == null) return;
        _sfxSource.PlayOneShot(clip, sfxVolume);
    }

    private void StartPauseMusic()
    {
        if (AudioManager.Instance != null && AudioManager.Instance.IsMusicPlaying)
        {
            AudioManager.Instance.StopMusic(0.5f);
        }
        // Music implementation handled in Update
    }

    private void StopPauseMusic()
    {
        // Handled in UpdatePauseMusic
    }

    private void UpdatePauseMusic()
    {
        if (_musicSource == null || pauseMenuMusic == null) return;

        float targetVol = State.IsPaused ? pauseMusicVolume * _musicVolume : 0f;
        _musicSource.volume = Mathf.MoveTowards(_musicSource.volume, targetVol, musicFadeSpeed * Time.unscaledDeltaTime);

        if (State.IsPaused && !_musicSource.isPlaying && targetVol > 0)
        {
            _musicSource.Play();
        }
        else if (_musicSource.volume <= 0.001f && _musicSource.isPlaying)
        {
            _musicSource.Stop();
        }
    }

    private const string FPS_CONTROLLER_TYPE = "FPSExampleController";

    private void ApplySensitivity()
    {
        // Lazy-cache FPSExampleController + MethodInfo (avoids FindObjectsByType + reflection per call)
        if (!_fpsControllerSearched)
        {
            _fpsControllerSearched = true;
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb.GetType().Name == FPS_CONTROLLER_TYPE)
                {
                    _cachedFPSController = mb;
                    _updateSensitivityMethod = mb.GetType().GetMethod("UpdateSensitivity");
                    break;
                }
            }
        }
        if (_cachedFPSController != null && _updateSensitivityMethod != null)
            _updateSensitivityMethod.Invoke(_cachedFPSController, new object[] { _mouseSensitivity });
    }

    private void ApplyFOV()
    {
        Camera cam = Camera.main;
        if (cam != null) cam.fieldOfView = _fieldOfView;
    }

    private void ApplyFx()
    {
        // Lazy-cache Volume references (avoid FindObjectsByType per toggle)
        if (_cachedVolumes == null)
            _cachedVolumes = FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None);

        foreach (var v in _cachedVolumes)
        {
            if (v == null || v.profile == null) continue;
            if (v.profile.TryGet<UnityEngine.Rendering.HighDefinition.Bloom>(out var b)) b.active = _bloom;
            if (v.profile.TryGet<UnityEngine.Rendering.HighDefinition.MotionBlur>(out var m)) m.active = _motionBlur;
            if (v.profile.TryGet<UnityEngine.Rendering.HighDefinition.Vignette>(out var vi)) vi.active = _vignette;
            if (v.profile.TryGet<UnityEngine.Rendering.HighDefinition.FilmGrain>(out var f)) f.active = _filmGrain;
            if (v.profile.TryGet<UnityEngine.Rendering.HighDefinition.ScreenSpaceAmbientOcclusion>(out var s)) s.active = _ssao;
            if (v.profile.TryGet<UnityEngine.Rendering.HighDefinition.Fog>(out var fo)) fo.enableVolumetricFog.value = _volumetricFog;
        }
        
        PlayerPrefs.SetInt(GameSettings.Keys.FULLSCREEN, _fullScreen ? 1 : 0);
        PlayerPrefs.SetInt(GameSettings.Keys.VSYNC, _vSync ? 1 : 0);
        PlayerPrefs.Save();
    }

    private void SetQuality(int level)
    {
        // Save preference
        PlayerPrefs.SetInt(GameSettings.Keys.QUALITY_LEVEL, level);

        // Apply via Manager
        if (GraphicsQualityManager.Instance != null) 
        {
            GraphicsQualityManager.Instance.SetQuality(level);
            Debug.Log($"[PauseMenu] Setting Quality to: {level}");
        }
        else
        {
            // Try to find it in scene
            var mgr = FindFirstObjectByType<GraphicsQualityManager>();
            if (mgr != null)
            {
                mgr.SetQuality(level);
            }
            else
            {
                // Critical Fallback
                Debug.LogWarning("[PauseMenu] GraphicsQualityManager missing! Setting Quality directly.");
                QualitySettings.SetQualityLevel(level, true);
                
                // Try to create it for next time
                new GameObject("GraphicsQualityManager").AddComponent<GraphicsQualityManager>();
            }
        }
        
        // FIX: Removed forced DLSS override. User preference should stay.
        // If the user wants to change DLSS, they used the DLSS selector.
        // Changing "Quality Preset" (Shadows/LOD/Textures) shouldn't reset their Upscaling method.
    }

    private void SetDLSS(int mode)
    {
        Debug.Log($"[PauseMenu] SetDLSS called: mode={mode}, current={_currentDLSSMode}");

        if (WorkingDLSSManager.Instance != null)
        {
            WorkingDLSSManager.Instance.SetMode((WorkingDLSSManager.DLSSMode)mode);
            _currentDLSSMode = mode;
            _cachedRenderStr = null; // Invalidate render stats cache
            Debug.Log($"[PauseMenu] DLSS Mode applied: {(WorkingDLSSManager.DLSSMode)mode}");
        }
        else
        {
            Debug.LogWarning("[PauseMenu] WorkingDLSSManager not found! Cannot apply DLSS mode.");
        }
    }

    private void SetDLSSPreset(int preset)
    {
        Debug.Log($"[PauseMenu] SetDLSSPreset called: preset={preset}, current={_currentDLSSPreset}");

        if (WorkingDLSSManager.Instance != null)
        {
            WorkingDLSSManager.Instance.SetPreset((WorkingDLSSManager.DLSSPreset)preset);
            _currentDLSSPreset = preset; // Keep UI in sync
            Debug.Log($"[PauseMenu] DLSS Preset applied: {(WorkingDLSSManager.DLSSPreset)preset}");
        }
        else
        {
            Debug.LogWarning("[PauseMenu] WorkingDLSSManager not found! Cannot apply DLSS preset.");
        }
    }

    private void ResetToDefaults()
    {
        _masterVolume = 1f;
        _musicVolume = 0.5f;
        _mouseSensitivity = 0.5f;
        _fieldOfView = 75f;
        _bloom = true;
        _filmGrain = true;
        _vignette = true;
        _motionBlur = false;
        _ssao = true;
        _volumetricFog = true;
        _currentQualityLevel = 0;
        _currentDLSSMode = 0;
        _currentDLSSPreset = 0; // Auto-select

        AudioListener.volume = _masterVolume;
        SetQuality(0);
        if (_dlssSupported)
        {
            SetDLSS(0);
            SetDLSSPreset(0); // Reset to auto-select
        }
        ApplySensitivity();
        ApplyFOV();
        ApplyFx();

        PlayerPrefs.SetFloat(GameSettings.Keys.MASTER_VOLUME, _masterVolume);
        PlayerPrefs.SetFloat(GameSettings.Keys.MUSIC_VOLUME, _musicVolume);
        PlayerPrefs.SetFloat(GameSettings.Keys.MOUSE_SENSITIVITY, _mouseSensitivity);
        PlayerPrefs.SetFloat(GameSettings.Keys.FIELD_OF_VIEW, _fieldOfView);
        PlayerPrefs.Save();

        State.ShowFeedback(L.Get("defaults_restored"));
        Debug.Log("[PauseMenu] All settings reset to defaults");
    }

    private void InitializeResolutions()
    {
        // Filter duplicate resolutions
        var allRes = Screen.resolutions;
        var uniqueRes = new System.Collections.Generic.List<Resolution>();
        
        // Prefer higher refresh rates
        foreach (var r in allRes)
        {
            bool found = false;
            for (int i = 0; i < uniqueRes.Count; i++)
            {
                if (uniqueRes[i].width == r.width && uniqueRes[i].height == r.height)
                {
                    if (r.refreshRateRatio.value > uniqueRes[i].refreshRateRatio.value)
                    {
                        uniqueRes[i] = r; // Replace with higher refresh rate
                    }
                    found = true;
                    break;
                }
            }
            if (!found) uniqueRes.Add(r);
        }
        
        _availableResolutions = uniqueRes.ToArray();
        CacheResolutionStrings();

        // Find current
        _currentResolutionIndex = 0;
        for (int i = 0; i < _availableResolutions.Length; i++)
        {
            if (_availableResolutions[i].width == Screen.width && _availableResolutions[i].height == Screen.height)
            {
                _currentResolutionIndex = i;
                break;
            }
        }
        
        _fullScreen = Screen.fullScreen;
        _vSync = QualitySettings.vSyncCount > 0;
        
        // Load saved preference if exists
        if (PlayerPrefs.HasKey(GameSettings.Keys.FULLSCREEN))
        {
           bool savedFs = PlayerPrefs.GetInt(GameSettings.Keys.FULLSCREEN) == 1;
           if (savedFs != _fullScreen)
           {
               _fullScreen = savedFs;
               ApplyResolution();
           }
        }
        if (PlayerPrefs.HasKey(GameSettings.Keys.VSYNC))
        {
            SetVSync(PlayerPrefs.GetInt(GameSettings.Keys.VSYNC) == 1);
        }
    }

    private void ApplyResolution()
    {
        if (_availableResolutions == null || _availableResolutions.Length == 0) return;

        var r = _availableResolutions[_currentResolutionIndex];
        Screen.SetResolution(r.width, r.height, _fullScreen);
        _cachedRenderStr = null; // Invalidate render stats cache
        Debug.Log($"[PauseMenu] Set Resolution: {r.width}x{r.height}, Fullscreen: {_fullScreen}");
    }

    private void CacheResolutionStrings()
    {
        if (_availableResolutions == null) return;
        _cachedResolutionStrings = new string[_availableResolutions.Length];
        for (int i = 0; i < _availableResolutions.Length; i++)
            _cachedResolutionStrings[i] = $"{_availableResolutions[i].width}x{_availableResolutions[i].height} ({_availableResolutions[i].refreshRateRatio.value:0}Hz)";
    }

    private string CachePercent(ref string cached, ref int lastVal, float rawValue)
    {
        int val = Mathf.RoundToInt(rawValue * 100);
        if (val != lastVal) { lastVal = val; cached = val + "%"; }
        return cached;
    }

    private string CacheFov()
    {
        int fov = Mathf.RoundToInt(_fieldOfView);
        if (fov != _lastCachedFov) { _lastCachedFov = fov; _cachedFovStr = fov + "\u00B0"; }
        return _cachedFovStr;
    }
    
    private void SetVSync(bool enabled)
    {
        _vSync = enabled;
        QualitySettings.vSyncCount = enabled ? 1 : 0;
        PlayerPrefs.SetInt(GameSettings.Keys.VSYNC, enabled ? 1 : 0);
    }

    private void DrawSelector(float x, float y, float w, string label, string[] options, int currentIndex, System.Action<int> onChanged, float alpha)
    {
        var tex = MenuStyles.SolidTexture;
        var accent = S.Amber;

        // Label
        var ls = MenuStyles.S(MenuStyles.StyleBody, Mathf.RoundToInt(14*uiScale),
            MenuStyles.WithAlpha(MenuStyles.TextMid, alpha));
        GUI.Label(new Rect(x, y+2, 200, 24), label, ls);

        // Selector Box
        float selW = 200 * uiScale;
        float selH = 30 * uiScale;
        float selX = x + w - selW;

        Rect r = new Rect(selX, y, selW, selH);

        // Arrows
        Rect leftA = new Rect(selX, y, 30, selH);
        Rect rightA = new Rect(selX + selW - 30, y, 30, selH);

        // Draw Box
        GUI.color = MenuStyles.WithAlpha(MenuStyles.FilmBrown, 0.8f * alpha);
        GUI.DrawTexture(r, tex);

        // Value
        string val = (currentIndex >= 0 && currentIndex < options.Length) ? options[currentIndex] : "Unknown";
        var vs = MenuStyles.S(MenuStyles.StyleBold, Mathf.RoundToInt(13*uiScale),
            MenuStyles.WithAlpha(Color.white, alpha), TextAnchor.MiddleCenter);
        GUI.color = Color.white;
        GUI.Label(r, val, vs);
        
        // Arrow navigation (manual hit-test, consistent with rest of UI — no default Unity button skin)
        bool leftHov = leftA.Contains(Event.current.mousePosition);
        bool rightHov = rightA.Contains(Event.current.mousePosition);

        GUI.Label(leftA, "<", MenuStyles.S(MenuStyles.StyleBold, Mathf.RoundToInt(16*uiScale),
            MenuStyles.WithAlpha(leftHov ? Color.white : S.Amber, alpha), TextAnchor.MiddleCenter));
        GUI.Label(rightA, ">", MenuStyles.S(MenuStyles.StyleBold, Mathf.RoundToInt(16*uiScale),
            MenuStyles.WithAlpha(rightHov ? Color.white : S.Amber, alpha), TextAnchor.MiddleCenter));

        if (leftHov && Event.current.type == EventType.MouseDown)
        {
            int next = currentIndex - 1;
            if (next < 0) next = options.Length - 1;
            onChanged?.Invoke(next);
            PlaySound(selectSound);
            Event.current.Use();
        }
        if (rightHov && Event.current.type == EventType.MouseDown)
        {
            int next = currentIndex + 1;
            if (next >= options.Length) next = 0;
            onChanged?.Invoke(next);
            PlaySound(selectSound);
            Event.current.Use();
        }
    }
    
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus && !State.IsPaused && canOpenMenu)
        {
            PauseGame();
        }
    }

    #endregion
}
