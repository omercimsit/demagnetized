using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System.Reflection;
using System.Diagnostics;

/// <summary>
/// Working DLSS Manager for HDRP
/// Properly configures DLSS by modifying HDRP asset AND camera settings
/// </summary>
public class WorkingDLSSManager : Singleton<WorkingDLSSManager>
{

    public enum DLSSMode
    {
        Off = 0,          // Use TAA
        DLAA = 1,         // Native resolution + DLSS AA (best quality)
        Quality = 2,      // 1.5x upscale
        Balanced = 3,     // 1.7x upscale
        Performance = 4,  // 2x upscale
        UltraPerf = 5     // 3x upscale
    }

    // ── HDRP Dynamic Resolution Constants ──
    private const byte DYNRES_TYPE_HARDWARE = 1;
    private const byte UPSAMPLE_FILTER_DLSS = 4;
    // DLSSPerfQualitySetting mapping
    private const uint HDRP_QUALITY_MAX = 0;        // DLAA / MaxQuality
    private const uint HDRP_QUALITY_QUALITY = 1;
    private const uint HDRP_QUALITY_BALANCED = 2;
    private const uint HDRP_QUALITY_PERFORMANCE = 3;
    private const uint HDRP_QUALITY_ULTRA_PERF = 4;
    // Camera DLSS quality mapping
    private const uint CAM_QUALITY_MAX = 0;
    private const uint CAM_QUALITY_BALANCED = 1;
    private const uint CAM_QUALITY_MAX_PERF = 2;
    private const uint CAM_QUALITY_ULTRA_PERF = 3;

    // ── Mode Switch Debounce ──
    private float _lastModeChangeTime;
    private const float MODE_CHANGE_COOLDOWN = 0.3f;

    /// <summary>
    /// DLSS 4.5 Model Presets
    /// Model M: 2nd gen transformer, optimized for Performance mode (RTX 40+)
    /// Model L: Optimized for 4K Ultra Performance (RTX 40+)
    /// Model K: Legacy model, recommended for RTX 20/30 series
    /// </summary>
    public enum DLSSPreset
    {
        Default = 0,      // Let NVIDIA decide (Recommended)
        PresetK = 1,      // Legacy model - RTX 20/30 series
        PresetM = 2,      // 2nd gen transformer - RTX 40+ Performance
        PresetL = 3       // 4K Ultra Performance - RTX 40+
    }

    [Header("Current Settings")]
    [SerializeField] private DLSSMode currentMode = DLSSMode.DLAA;
    [SerializeField] private bool dlssSupported = false;
    [SerializeField] private string gpuName = "";
    [SerializeField] private string statusMessage = "Initializing...";

    [Header("TAA Alternative (when DLSS off)")]
    [SerializeField] private float taaSharpness = 0.8f;

    [Header("DLSS Quality Settings")]
    [SerializeField] [Range(0f, 1f)] private float dlssSharpness = 0.85f; // Increased for sharper image
    [SerializeField] private bool useDLAAQualityPreset = true;

    [Header("DLSS 4.5 Model Selection")]
    [SerializeField] private DLSSPreset currentPreset = DLSSPreset.Default;
    [SerializeField] private bool isRTX40OrNewer = false;
    private const string PREF_DLSS_PRESET = GameSettings.Keys.DLSS_PRESET;

    [Header("Debug")]
    [SerializeField] private bool showDebugOverlay = false;
    
    private const string PREF_DLSS_MODE = GameSettings.Keys.DLSS_MODE;
    
    // Dynamic Resolution Scaler
    private float currentDynamicResScale = 1f;
    private bool dynamicResScalerRegistered = false;
    
    // FPS Tracking
    private float deltaTime = 0f;
    private float fps = 0f;
    private float minFps = float.MaxValue;
    private float maxFps = 0f;
    private float avgFps = 0f;
    private int frameCount = 0;
    private float fpsSum = 0f;

    // ── Reflection Cache (resolved once, avoids 20+ GetField calls per mode apply) ──
    private static bool _reflectionCacheInit;
    private static FieldInfo _rf_pipelineSettings;
    private static FieldInfo _rf_dynResSettings;
    private static FieldInfo _rf_enabled;
    private static FieldInfo _rf_dynResType;
    private static FieldInfo _rf_minPercentage;
    private static FieldInfo _rf_maxPercentage;
    private static FieldInfo _rf_forceResolution;
    private static FieldInfo _rf_forcedPercentage;
    private static FieldInfo _rf_upsampleFilter;
    private static FieldInfo _rf_dlssPerfQuality;
    private static FieldInfo _rf_enableDLSS;
    private static FieldInfo _rf_dlssUseOptimal;
    private static FieldInfo _rf_dlssSharpness;
    private static FieldInfo _rf_enableAntiGhosting;
    private static FieldInfo _rf_useMipBias;
    private static FieldInfo[] _rf_presetFields;
    // Camera DLSS PropertyInfo cache (avoids per-call GetProperty)
    private static readonly System.Collections.Generic.Dictionary<string, PropertyInfo> _propCache
        = new System.Collections.Generic.Dictionary<string, PropertyInfo>(8);
    
    public DLSSMode CurrentMode => currentMode;
    public bool IsDLSSSupported => dlssSupported;
    public string StatusMessage => statusMessage;
    
    // Conditional logging - only compiles in Editor/Development builds
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    private static void LogDebug(string message)
    {
        // DISABLED - Too verbose. Uncomment for DLSS debugging only
        // UnityEngine.Debug.Log(message);
    }
    
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    private static void LogWarning(string message)
    {
        UnityEngine.Debug.LogWarning($"[DLSS] {message}");
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    private static void LogError(string message)
    {
        UnityEngine.Debug.LogError($"[DLSS] {message}");
    }
    
    protected override void OnAwake()
    {
        // Disable DebugManager UI to prevent DLSS conflicts
        try { DebugManager.instance.enableRuntimeUI = false; }
        catch (System.Exception) { /* DebugManager may not be initialized during startup */ }

        CheckDLSSSupport();
        LoadSettings();
    }
    
    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }
    
    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // Re-apply DLSS settings when a new scene loads
        LogDebug($"[DLSS] Scene loaded: {scene.name} - Reapplying DLSS settings...");
        ScheduleDLSSApply();
    }

    private void Start()
    {
        // Register our custom dynamic resolution scaler
        RegisterDynamicResScaler();
        // Initial scene load will trigger OnSceneLoaded, so only schedule if not already pending
        ScheduleDLSSApply();
    }

    /// <summary>
    /// Schedules DLSS application with deduplication.
    /// Cancels any pending invokes and schedules a single delayed apply.
    /// Uses 1.0s delay to ensure cameras are initialized.
    /// </summary>
    private void ScheduleDLSSApply()
    {
        // Cancel any pending invokes to prevent duplicate calls
        CancelInvoke(nameof(ApplyCurrentMode));
        // Single delayed apply - 1.0s is enough for camera initialization
        Invoke(nameof(ApplyCurrentMode), 1.0f);
    }
    
    private void RegisterDynamicResScaler()
    {
        if (dynamicResScalerRegistered) return;
        
        try
        {
            // Use SetDynamicResScaler to control resolution at runtime
            DynamicResolutionHandler.SetDynamicResScaler(GetDynamicResScale, DynamicResScalePolicyType.ReturnsPercentage);
            dynamicResScalerRegistered = true;
            LogDebug("[DLSS] Dynamic Resolution Scaler registered successfully!");
        }
        catch (System.Exception e)
        {
            LogError($"[DLSS] Failed to register Dynamic Resolution Scaler: {e.Message}");
        }
    }
    
    /// <summary>
    /// Called by Unity's DynamicResolutionHandler every frame
    /// Returns the target render percentage (50-100)
    /// </summary>
    private float GetDynamicResScale()
    {
        return currentDynamicResScale;
    }
    
    // Watchdog timer
    private float checkTimer = 0f;
    private RenderPipelineAsset lastPipelineAsset;

    private void Update()
    {
#if UNITY_EDITOR
        // Only F1 for debug overlay toggle - other keys removed to prevent accidental mode changes
        if (Input.GetKeyDown(KeyCode.F1)) showDebugOverlay = !showDebugOverlay;
        // F2-F7 keys disabled - use in-game settings menu instead
#endif

        // FPS Calculation
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        fps = 1f / deltaTime;
        if (fps > 1 && fps < 1000)
        {
            if (fps < minFps) minFps = fps;
            if (fps > maxFps) maxFps = fps;
            fpsSum += fps;
            frameCount++;
            avgFps = fpsSum / frameCount;
        }

        // WATCHDOG: Check if Pipeline Asset changed (silent after first detection)
        checkTimer += Time.unscaledDeltaTime;
        if (checkTimer > 3.0f)
        {
            checkTimer = 0f;

            if (GraphicsSettings.currentRenderPipeline != lastPipelineAsset)
            {
                // Only log once, then silently apply
                if (lastPipelineAsset != null)
                {
                    LogDebug("[DLSS] Pipeline changed, re-applying settings silently...");
                }
                lastPipelineAsset = GraphicsSettings.currentRenderPipeline;
                ApplyCurrentMode();
            }
        }
    }
    
    private void ResetFPSStats()
    {
        minFps = float.MaxValue;
        maxFps = 0f;
        avgFps = 0f;
        frameCount = 0;
        fpsSum = 0f;
        LogDebug("[DLSS] FPS stats reset");
    }
    
    private void CheckDLSSSupport()
    {
        gpuName = SystemInfo.graphicsDeviceName;
        string gpuLower = gpuName.ToLower();

        bool isNvidia = gpuLower.Contains("nvidia") || gpuLower.Contains("geforce");
        bool isRTX = gpuLower.Contains("rtx");

        dlssSupported = isNvidia && isRTX;

        // DLSS 4.5 Model M/L detection - RTX 40 series and newer
        isRTX40OrNewer = false;
        if (dlssSupported)
        {
            // RTX 50 series (Blackwell): 5060, 5070, 5080, 5090
            if (gpuLower.Contains("rtx 50") || gpuLower.Contains("rtx50") ||
                gpuLower.Contains("5060") || gpuLower.Contains("5070") ||
                gpuLower.Contains("5080") || gpuLower.Contains("5090"))
            {
                isRTX40OrNewer = true;
            }
            // RTX 40 series (Ada Lovelace): 4050, 4060, 4070, 4080, 4090
            else if (gpuLower.Contains("rtx 40") || gpuLower.Contains("rtx40") ||
                gpuLower.Contains("4050") || gpuLower.Contains("4060") ||
                gpuLower.Contains("4070") || gpuLower.Contains("4080") ||
                gpuLower.Contains("4090"))
            {
                isRTX40OrNewer = true;
            }
        }

        if (dlssSupported)
        {
            string modelSupport = isRTX40OrNewer ? "Model M/L" : "Model K";
            statusMessage = $"DLSS 4.5 Destekleniyor ({modelSupport}) - {gpuName}";
            LogDebug($"<color=green>[DLSS] ✓ Supported! GPU: {gpuName}, RTX40+: {isRTX40OrNewer}</color>");
        }
        else
        {
            statusMessage = $"DLSS Not Supported - {gpuName} (falling back to TAA)";
            LogDebug($"<color=yellow>[DLSS] ✗ Not supported. GPU: {gpuName}. Using TAA fallback.</color>");
        }
    }
    
    private void LoadSettings()
    {
        int saved = PlayerPrefs.GetInt(PREF_DLSS_MODE, -1);

        if (saved == -1)
        {
            currentMode = dlssSupported ? DLSSMode.DLAA : DLSSMode.Off;
            LogDebug($"[DLSS] First run - defaulting to: {currentMode}");
        }
        else
        {
            currentMode = (DLSSMode)saved;
            LogDebug($"[DLSS] Loaded saved mode: {currentMode}");
        }

        // FIX: clamp PlayerPrefs values - default 0.7 for sharper image
        dlssSharpness = Mathf.Clamp01(PlayerPrefs.GetFloat(GameSettings.Keys.DLSS_SHARPNESS, 0.7f));
        LogDebug($"[DLSS] Loaded sharpness: {dlssSharpness}");

        // DLSS 4.5 Preset loading
        int savedPreset = PlayerPrefs.GetInt(PREF_DLSS_PRESET, -1);
        if (savedPreset == -1)
        {
            // Auto-select best preset based on GPU
            if (isRTX40OrNewer)
            {
                currentPreset = DLSSPreset.PresetM; // Model M for RTX 40+
            }
            else if (dlssSupported)
            {
                currentPreset = DLSSPreset.PresetK; // Model K for RTX 20/30
            }
            else
            {
                currentPreset = DLSSPreset.Default;
            }
            LogDebug($"[DLSS] Auto-selected preset: {currentPreset} (RTX40+: {isRTX40OrNewer})");
        }
        else
        {
            currentPreset = (DLSSPreset)savedPreset;
            LogDebug($"[DLSS] Loaded saved preset: {currentPreset}");
        }
    }
    
    public void SetMode(DLSSMode mode)
    {
        // Debounce rapid mode switches to prevent pipeline thrashing
        if (Time.realtimeSinceStartup - _lastModeChangeTime < MODE_CHANGE_COOLDOWN)
        {
            LogDebug($"[DLSS] Mode change debounced (too fast)");
            return;
        }
        _lastModeChangeTime = Time.realtimeSinceStartup;

        LogDebug($"[DLSS] === Manual SetMode: {mode} ===");

        DLSSMode oldMode = currentMode;
        currentMode = mode;
        
        PlayerPrefs.SetInt(PREF_DLSS_MODE, (int)mode);
        PlayerPrefs.Save();
        
        // CRITICAL FIX: When switching between DLSS modes, we need to:
        // 1. Update the dynamic resolution scaler immediately
        // 2. Force camera to reset its rendering state
        // 3. Apply after a few frames to ensure pipeline picks up changes
        
        currentDynamicResScale = GetTargetScale(mode);
        
        ApplyCurrentMode();
        
        // Force a delayed re-application to ensure changes stick
        StartCoroutine(ForceReapplyAfterDelay(0.1f));
        
        LogDebug($"[DLSS] Mode changed: {oldMode} -> {mode}, Target Scale: {currentDynamicResScale}%");
    }

    /// <summary>
    /// Set DLSS 4.5 Model Preset (K/M/L)
    /// Model M: 2nd gen transformer for RTX 40+ Performance mode
    /// Model L: 4K Ultra Performance for RTX 40+
    /// Model K: Legacy for RTX 20/30 series
    /// </summary>
    public void SetPreset(DLSSPreset preset)
    {
        // Validate preset compatibility
        if ((preset == DLSSPreset.PresetM || preset == DLSSPreset.PresetL) && !isRTX40OrNewer)
        {
            LogWarning($"[DLSS] Model {preset} requires RTX 40 series or newer. Falling back to Model K.");
            preset = DLSSPreset.PresetK;
        }

        DLSSPreset oldPreset = currentPreset;
        currentPreset = preset;

        PlayerPrefs.SetInt(PREF_DLSS_PRESET, (int)preset);
        PlayerPrefs.Save();

        ApplyCurrentMode();

        LogDebug($"[DLSS] Preset changed: {oldPreset} -> {preset}");
    }

    /// <summary>
    /// Get current DLSS 4.5 Preset
    /// </summary>
    public DLSSPreset CurrentPreset => currentPreset;

    /// <summary>
    /// Check if RTX 40 series or newer (supports Model M/L)
    /// </summary>
    public bool IsRTX40OrNewer => isRTX40OrNewer;

    /// <summary>
    /// Get preset name for UI
    /// </summary>
    public static string GetPresetName(DLSSPreset preset)
    {
        return preset switch
        {
            DLSSPreset.Default => "Otomatik",
            DLSSPreset.PresetK => "Model K (RTX 20/30)",
            DLSSPreset.PresetM => "Model M (RTX 40+)",
            DLSSPreset.PresetL => "Model L (4K Ultra)",
            _ => preset.ToString()
        };
    }

    private System.Collections.IEnumerator ForceReapplyAfterDelay(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        
        // Force cameras to recognize the change by toggling their state
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            var hdData = mainCam.GetComponent<HDAdditionalCameraData>();
            if (hdData != null)
            {
                // Toggle AA mode to force Unity to rebuild render state
                var currentAA = hdData.antialiasing;
                hdData.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                yield return null; // Wait one frame
                hdData.antialiasing = currentAA;
                
                LogDebug($"[DLSS] Forced camera state refresh on {mainCam.name}");
            }
        }
        
        yield return new WaitForSecondsRealtime(0.1f);
        ApplyCurrentMode();
    }
    
    private float GetTargetScale(DLSSMode mode)
    {
        return mode switch
        {
            DLSSMode.Off => 100f,
            DLSSMode.DLAA => 100f,
            DLSSMode.Quality => 66.6f,
            DLSSMode.Balanced => 58f,
            DLSSMode.Performance => 50f,
            DLSSMode.UltraPerf => 33f,
            _ => 100f
        };
    }

    // ResetManualOverride removed - no longer needed since we always configure HDRP Asset
    
    /// <summary>
    /// Get/Set DLSS Sharpness (0-1)
    /// </summary>
    public float DLSSSharpness
    {
        get => dlssSharpness;
        set
        {
            dlssSharpness = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(GameSettings.Keys.DLSS_SHARPNESS, dlssSharpness);
            PlayerPrefs.Save();
            ApplyCurrentMode();
            LogDebug($"[DLSS] Sharpness set to: {dlssSharpness}");
        }
    }
    
    private void ApplyCurrentMode()
    {
        LogDebug($"[DLSS] Applying mode: {currentMode}, DLSS Supported: {dlssSupported}");
        
        // Calculate target resolution scale FIRST
        bool useDLSS = (currentMode != DLSSMode.Off && dlssSupported);
        currentDynamicResScale = GetTargetScale(currentMode);
        
        LogDebug($"[DLSS] Target resolution scale: {currentDynamicResScale}%");
        
        ConfigureHDRPAsset(currentMode);
        
        int appliedCount = 0;
        
        // Only apply to Main Camera (primary)
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            ApplyToCamera(mainCam, currentMode);
            appliedCount++;
            LogDebug($"[DLSS] Applied to MainCamera: {mainCam.name}");
        }
        
        // Only apply to other ACTIVE cameras that are actually rendering
        Camera[] allCameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var cam in allCameras)
        {
            // Skip main camera (already processed)
            if (cam == mainCam) continue;
            
            // Only apply to cameras that are enabled and active
            if (cam.enabled && cam.gameObject.activeInHierarchy)
            {
                ApplyToCamera(cam, currentMode);
                appliedCount++;
            }
        }
        
        if (currentMode == DLSSMode.Off || !dlssSupported)
        {
            statusMessage = "TAA Active (DLSS Off)";
        }
        else if (currentMode == DLSSMode.DLAA)
        {
            statusMessage = "DLAA Active (Native Resolution)";
        }
        else
        {
            statusMessage = $"DLSS {GetModeName(currentMode)} Aktif";
        }
        
        LogDebug($"[DLSS] Applied to {appliedCount} active camera(s)");
    }
    
    /// <summary>
    /// One-time reflection cache initialization. Resolves all FieldInfo/PropertyInfo
    /// objects once, avoiding 20+ GetField() calls on every mode apply.
    /// </summary>
    private void InitReflectionCache(HDRenderPipelineAsset hdrpAsset)
    {
        if (_reflectionCacheInit) return;

        const BindingFlags nonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags pub = BindingFlags.Instance | BindingFlags.Public;
        const BindingFlags pubOrNonPub = pub | nonPublic;

        _rf_pipelineSettings = typeof(HDRenderPipelineAsset).GetField("m_RenderPipelineSettings", nonPublic);
        if (_rf_pipelineSettings == null) return;

        object settings = _rf_pipelineSettings.GetValue(hdrpAsset);
        _rf_dynResSettings = settings.GetType().GetField("dynamicResolutionSettings", pubOrNonPub);
        if (_rf_dynResSettings == null) return;

        object dynRes = _rf_dynResSettings.GetValue(settings);
        var drt = dynRes.GetType();

        _rf_enabled = drt.GetField("enabled", pub);
        _rf_dynResType = drt.GetField("dynResType", pub);
        _rf_minPercentage = drt.GetField("minPercentage", pub);
        _rf_maxPercentage = drt.GetField("maxPercentage", pub);
        _rf_forceResolution = drt.GetField("forceResolution", pub);
        _rf_forcedPercentage = drt.GetField("forcedPercentage", pub);
        _rf_upsampleFilter = drt.GetField("upsampleFilter", pub);
        _rf_dlssPerfQuality = drt.GetField("DLSSPerfQualitySetting", pub);
        _rf_enableDLSS = drt.GetField("enableDLSS", pub);
        _rf_dlssUseOptimal = drt.GetField("DLSSUseOptimalSettings", pub);
        _rf_dlssSharpness = drt.GetField("DLSSSharpness", pub);
        _rf_enableAntiGhosting = drt.GetField("enableDLSSAntiGhosting", pub);
        _rf_useMipBias = drt.GetField("useMipBias", pub);
        _rf_presetFields = new FieldInfo[]
        {
            drt.GetField("DLSSRenderPresetForDLAA", pub),
            drt.GetField("DLSSRenderPresetForQuality", pub),
            drt.GetField("DLSSRenderPresetForBalanced", pub),
            drt.GetField("DLSSRenderPresetForPerformance", pub),
            drt.GetField("DLSSRenderPresetForUltraPerformance", pub)
        };

        _reflectionCacheInit = true;
        LogDebug("[DLSS] Reflection cache initialized (20+ field lookups cached)");
    }

    private void ConfigureHDRPAsset(DLSSMode mode)
    {
        if (GraphicsSettings.currentRenderPipeline == null)
        {
            LogWarning("[DLSS] No Render Pipeline Asset configured in Graphics Settings!");
            return;
        }

        var hdrpAsset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
        if (hdrpAsset == null)
        {
            LogWarning("[DLSS] Current Render Pipeline is not HDRP!");
            return;
        }

        LogDebug($"[DLSS] Configuring HDRP Asset: {hdrpAsset.name}");

        // Initialize reflection cache once (all subsequent calls are no-op)
        InitReflectionCache(hdrpAsset);

        if (_rf_pipelineSettings == null || _rf_dynResSettings == null)
        {
            LogWarning("[DLSS] Reflection cache init failed - HDRP API may have changed");
            return;
        }

        try
        {
            object settings = _rf_pipelineSettings.GetValue(hdrpAsset);
            object dynRes = _rf_dynResSettings.GetValue(settings);

            bool useDLSS = (mode != DLSSMode.Off && dlssSupported);
            float targetScale = GetTargetScale(mode);

            // All field accesses use cached FieldInfo — zero GetField() calls
            _rf_enabled?.SetValue(dynRes, useDLSS);
            if (useDLSS) _rf_dynResType?.SetValue(dynRes, DYNRES_TYPE_HARDWARE);
            _rf_minPercentage?.SetValue(dynRes, targetScale);
            _rf_maxPercentage?.SetValue(dynRes, targetScale);
            _rf_forceResolution?.SetValue(dynRes, useDLSS);
            if (useDLSS) _rf_forcedPercentage?.SetValue(dynRes, targetScale);
            if (useDLSS) _rf_upsampleFilter?.SetValue(dynRes, UPSAMPLE_FILTER_DLSS);

            // DLSS Quality
            if (_rf_dlssPerfQuality != null)
            {
                uint hdrpQuality = mode switch
                {
                    DLSSMode.DLAA => HDRP_QUALITY_MAX,
                    DLSSMode.Quality => HDRP_QUALITY_QUALITY,
                    DLSSMode.Balanced => HDRP_QUALITY_BALANCED,
                    DLSSMode.Performance => HDRP_QUALITY_PERFORMANCE,
                    DLSSMode.UltraPerf => HDRP_QUALITY_ULTRA_PERF,
                    _ => HDRP_QUALITY_MAX
                };
                _rf_dlssPerfQuality.SetValue(dynRes, hdrpQuality);
            }

            _rf_enableDLSS?.SetValue(dynRes, useDLSS);
            _rf_dlssUseOptimal?.SetValue(dynRes, mode == DLSSMode.DLAA && useDLAAQualityPreset);
            _rf_dlssSharpness?.SetValue(dynRes, dlssSharpness);

            // DLSS Model Preset (K/M/L)
            uint renderPreset = currentPreset switch
            {
                DLSSPreset.PresetK => 4,  // Preset D - works on all RTX GPUs
                DLSSPreset.PresetM => 0,  // Default - NVIDIA auto-selects Model M
                DLSSPreset.PresetL => 0,  // Default - NVIDIA auto-selects Model L
                _ => 0
            };
            if (_rf_presetFields != null)
            {
                foreach (var pf in _rf_presetFields) pf?.SetValue(dynRes, renderPreset);
            }

            // DLAA: disable anti-ghosting for sharper image
            if (mode == DLSSMode.DLAA) _rf_enableAntiGhosting?.SetValue(dynRes, false);

            // Enable MipBias for sharper textures
            if (useDLSS) _rf_useMipBias?.SetValue(dynRes, true);

            // Write back struct values
            _rf_dynResSettings.SetValue(settings, dynRes);
            _rf_pipelineSettings.SetValue(hdrpAsset, settings);

            LogDebug($"[DLSS] HDRP Asset configured successfully! Mode: {mode}, Scale: {targetScale}%");
        }
        catch (System.Exception e)
        {
            LogError($"[DLSS] Error configuring HDRP Asset: {e.Message}\n{e.StackTrace}");
        }
    }
    
    private void ApplyToCamera(Camera cam, DLSSMode mode)
    {
        var hdData = cam.GetComponent<HDAdditionalCameraData>();
        if (hdData == null)
        {
            hdData = cam.gameObject.AddComponent<HDAdditionalCameraData>();
            LogDebug($"[DLSS] Added HDAdditionalCameraData to camera: {cam.name}");
        }
        
        bool useDLSS = (mode != DLSSMode.Off && dlssSupported);
        
        // Base TAA settings (DLSS uses TAA as foundation)
        hdData.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;
        hdData.taaSharpenStrength = taaSharpness;
        hdData.taaHistorySharpening = 0.6f;
        hdData.taaAntiFlicker = 0.5f;
        hdData.taaMotionVectorRejection = 0.1f;
        
        if (!useDLSS)
        {
            // TAA mode - disable DLSS completely
            hdData.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            hdData.allowDynamicResolution = false;
            cam.allowDynamicResolution = false; // Also set on Camera component

            // Disable DLSS via reflection
            TrySetProperty(hdData, "allowDeepLearningSuperSampling", false);

            LogDebug($"[DLSS] Camera '{cam.name}': TAA Mode (DLSS disabled)");
        }
        else
        {
            // Unity 6 HDRP: DLSS works through TAA + Dynamic Resolution + DLSS Upscaler
            // Set TAA as base antialiasing (DLSS enhances TAA output)
            hdData.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;

            // CRITICAL: Enable Dynamic Resolution on BOTH Camera and HDAdditionalCameraData
            hdData.allowDynamicResolution = true;
            cam.allowDynamicResolution = true; // This is crucial for Unity 6!

            // Enable DLSS via reflection
            TrySetProperty(hdData, "allowDeepLearningSuperSampling", true);
            TrySetProperty(hdData, "deepLearningSuperSamplingUseCustomQualitySettings", false);

            // Use optimal settings - NVIDIA auto-selects best quality
            TrySetProperty(hdData, "deepLearningSuperSamplingUseOptimalSettings", true);

            // Set quality based on mode (camera-level DLSS quality enum)
            uint quality = mode switch
            {
                DLSSMode.DLAA => CAM_QUALITY_MAX,
                DLSSMode.Quality => CAM_QUALITY_MAX,
                DLSSMode.Balanced => CAM_QUALITY_BALANCED,
                DLSSMode.Performance => CAM_QUALITY_MAX_PERF,
                DLSSMode.UltraPerf => CAM_QUALITY_ULTRA_PERF,
                _ => CAM_QUALITY_MAX
            };
            TrySetProperty(hdData, "deepLearningSuperSamplingQuality", quality);

            // Set sharpness
            TrySetProperty(hdData, "deepLearningSuperSamplingSharpening", dlssSharpness);

            UnityEngine.Debug.Log($"[DLSS] Camera '{cam.name}': DLSS Mode={mode}, Quality={quality}, DynRes={cam.allowDynamicResolution}");
        }
        
        // CRITICAL: Force dynamic resolution handler to use our scale
        if (useDLSS && mode != DLSSMode.DLAA)
        {
            // For upscaling modes, ensure camera uses our dynamic res scaler
            try {
                DynamicResolutionHandler.SetDynamicResScaler(GetDynamicResScale, DynamicResScalePolicyType.ReturnsPercentage);
            } catch (System.Exception) { /* DynamicResolutionHandler may not be available on all platforms */ }
        }
    }
    
    private void TrySetProperty(object target, string propertyName, object value)
    {
        try
        {
            if (!_propCache.TryGetValue(propertyName, out var prop))
            {
                prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                _propCache[propertyName] = prop;
            }
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(target, value);
                LogDebug($"[DLSS] Set {propertyName} = {value}");
            }
            else
            {
                LogWarning($"[DLSS] Property '{propertyName}' not found or read-only on {target.GetType().Name}");
            }
        }
        catch (System.Exception e)
        {
            LogWarning($"[DLSS] Failed to set {propertyName}: {e.Message}");
        }
    }

    /// <summary>
    /// Force refresh DLSS on all cameras
    /// </summary>
    public void RefreshDLSS()
    {
        ApplyCurrentMode();
    }
    
    /// <summary>
    /// Reset TAA/DLSS history after teleport to prevent ghosting/artifacts.
    /// Call this after any instant position change (rewind, teleport, etc.)
    /// </summary>
    public void ResetCameraHistory()
    {
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        
        foreach (var cam in cameras)
        {
            if (!cam.enabled || !cam.gameObject.activeInHierarchy) continue;
            
            var hdData = cam.GetComponent<HDAdditionalCameraData>();
            if (hdData != null)
            {
                // Toggle TAA to force history reset
                // Save current AA mode
                var currentAA = hdData.antialiasing;
                
                // Briefly disable then re-enable - this clears temporal buffers
                hdData.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                hdData.antialiasing = currentAA;
                
                LogDebug($"[DLSS] Reset AA history for camera: {cam.name}");
            }
        }
    }
    
    /// <summary>
    /// Get mode names for UI
    /// </summary>
    public static string GetModeName(DLSSMode mode)
    {
        return mode switch
        {
            DLSSMode.Off => "TAA",
            DLSSMode.DLAA => "DLAA",
            DLSSMode.Quality => "QUALITY",
            DLSSMode.Balanced => "BALANCED",
            DLSSMode.Performance => "PERFORMANS",
            DLSSMode.UltraPerf => "ULTRA PERF",
            _ => mode.ToString()
        };
    }
    
    public static string GetModeDescription(DLSSMode mode)
    {
        return mode switch
        {
            DLSSMode.Off => L.Get("dlss_desc_off"),
            DLSSMode.DLAA => L.Get("dlss_desc_dlaa"),
            DLSSMode.Quality => L.Get("dlss_desc_quality"),
            DLSSMode.Balanced => L.Get("dlss_desc_balanced"),
            DLSSMode.Performance => L.Get("dlss_desc_performance"),
            DLSSMode.UltraPerf => L.Get("dlss_desc_ultraperf"),
            _ => ""
        };
    }
    
    // Cached debug GUI styles
    private GUIStyle _dbgBoxStyle;
    private GUIStyle _dbgLabelStyle;
    private GUIStyle _dbgHeaderStyle;
    private GUIStyle _dbgFpsStyle;

    // Debug overlay - Only show when enabled
    private void OnGUI()
    {
        if (!showDebugOverlay) return;

        if (_dbgBoxStyle == null)
        {
            _dbgBoxStyle = new GUIStyle(GUI.skin.box);
            _dbgBoxStyle.normal.background = MakeBackgroundTexture(new Color(0f, 0f, 0f, 0.85f));
        }
        if (_dbgLabelStyle == null)
        {
            _dbgLabelStyle = new GUIStyle(GUI.skin.label);
            _dbgLabelStyle.fontSize = 14;
            _dbgLabelStyle.normal.textColor = Color.white;
        }
        if (_dbgHeaderStyle == null)
        {
            _dbgHeaderStyle = new GUIStyle(GUI.skin.label);
            _dbgHeaderStyle.fontSize = 20;
            _dbgHeaderStyle.fontStyle = FontStyle.Bold;
        }
        if (_dbgFpsStyle == null)
        {
            _dbgFpsStyle = new GUIStyle(GUI.skin.label);
            _dbgFpsStyle.fontSize = 28;
            _dbgFpsStyle.fontStyle = FontStyle.Bold;
        }
        
        string cameraAA = "Unknown";
        string dynRes = "Unknown";
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            var hdData = mainCam.GetComponent<HDAdditionalCameraData>();
            if (hdData != null)
            {
                cameraAA = hdData.antialiasing.ToString();
                dynRes = hdData.allowDynamicResolution.ToString();
            }
        }
        
        float boxWidth = 400;
        float boxHeight = 340;
        
        GUI.Box(new Rect(10, 10, boxWidth, boxHeight), "", _dbgBoxStyle);
        
        float x = 20;
        float y = 15;
        float lineHeight = 22;
        
        _dbgHeaderStyle.normal.textColor = new Color(0.4f, 0.8f, 1f);
        GUI.Label(new Rect(x, y, boxWidth - 20, 30), "═══ DLSS TEST PANEL ═══", _dbgHeaderStyle);
        y += 35;
        
        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight), $"GPU: {gpuName}", _dbgLabelStyle);
        y += lineHeight;
        
        GUI.color = dlssSupported ? Color.green : Color.yellow;
        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight), 
            $"DLSS Support: {(dlssSupported ? "✓ YES" : "✗ NO")}", _dbgLabelStyle);
        GUI.color = Color.white;
        y += lineHeight;
        
        GUI.color = currentMode == DLSSMode.Off ? Color.gray : Color.cyan;
        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight), $"Mode: {GetModeName(currentMode)}", _dbgLabelStyle);
        GUI.color = Color.white;
        y += lineHeight;

        // DLSS 4.5 Preset info
        GUI.color = isRTX40OrNewer ? Color.green : Color.yellow;
        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight), $"Preset: {GetPresetName(currentPreset)}", _dbgLabelStyle);
        GUI.color = Color.white;
        y += lineHeight;

        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight), $"Camera AA: {cameraAA}", _dbgLabelStyle);
        y += lineHeight;
        
        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight), $"Dynamic Res: {dynRes}", _dbgLabelStyle);
        y += lineHeight;

        // Show actual render resolution vs display resolution
        float actualScale = DynamicResolutionHandler.instance.GetCurrentScale() * 100f;
        int displayW = Screen.width;
        int displayH = Screen.height;
        int renderW = Mathf.RoundToInt(displayW * actualScale / 100f);
        int renderH = Mathf.RoundToInt(displayH * actualScale / 100f);

        GUI.color = actualScale < 99f ? Color.green : Color.yellow;
        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight), $"Render Scale: {actualScale:F1}% (Target: {currentDynamicResScale}%)", _dbgLabelStyle);
        y += lineHeight;

        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight), $"Render: {renderW}x{renderH} → Display: {displayW}x{displayH}", _dbgLabelStyle);
        GUI.color = Color.white;
        y += lineHeight + 10;
        
        GUI.color = fps > 60 ? Color.green : (fps > 30 ? Color.yellow : Color.red);
        _dbgFpsStyle.normal.textColor = GUI.color;
        GUI.Label(new Rect(x, y, boxWidth - 20, 35), $"FPS: {fps:F1}", _dbgFpsStyle);
        GUI.color = Color.white;
        y += 35;
        
        string minDisplay = minFps < 1000 ? $"{minFps:F0}" : "---";
        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight), 
            $"Min: {minDisplay} | Max: {maxFps:F0} | Avg: {avgFps:F0}", _dbgLabelStyle);
        y += lineHeight + 10;
        
        GUI.color = new Color(1f, 1f, 1f, 0.5f);
        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight), L.Get("debug_overlay_controls"), _dbgLabelStyle);
        GUI.color = Color.white;
    }
    
    private Texture2D _bgTexture;
    private Texture2D MakeBackgroundTexture(Color color)
    {
        if (_bgTexture == null)
        {
            _bgTexture = new Texture2D(2, 2);
            Color[] pixels = new Color[4];
            for (int i = 0; i < 4; i++) pixels[i] = color;
            _bgTexture.SetPixels(pixels);
            _bgTexture.Apply();
        }
        return _bgTexture;
    }

    protected override void OnDestroy()
    {
        if (_bgTexture != null)
        {
            Destroy(_bgTexture);
            _bgTexture = null;
        }
        _dbgBoxStyle = null;
        _dbgLabelStyle = null;
        _dbgHeaderStyle = null;
        _dbgFpsStyle = null;
        base.OnDestroy();
    }
}
