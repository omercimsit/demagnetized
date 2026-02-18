using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System.Reflection;
using System.Diagnostics;

// this took forever to get working - Unity doesn't expose DLSS settings publicly
// so we have to go through reflection to touch the HDRP asset fields
public class WorkingDLSSManager : Singleton<WorkingDLSSManager>
{

    public enum DLSSMode
    {
        Off = 0,        // Use TAA
        DLAA = 1,       // Native resolution + DLSS AA
        Quality = 2,    // 1.5x upscale
        Balanced = 3,   // 1.7x upscale
        Performance = 4,// 2x upscale
        UltraPerf = 5   // 3x upscale
    }

    // HDRP dynamic resolution constants - found by decompiling the package
    private const byte DYNRES_TYPE_HARDWARE = 1;
    private const byte UPSAMPLE_FILTER_DLSS = 4;
    private const uint HDRP_QUALITY_MAX = 0;
    private const uint HDRP_QUALITY_QUALITY = 1;
    private const uint HDRP_QUALITY_BALANCED = 2;
    private const uint HDRP_QUALITY_PERFORMANCE = 3;
    private const uint HDRP_QUALITY_ULTRA_PERF = 4;
    private const uint CAM_QUALITY_MAX = 0;
    private const uint CAM_QUALITY_BALANCED = 1;
    private const uint CAM_QUALITY_MAX_PERF = 2;
    private const uint CAM_QUALITY_ULTRA_PERF = 3;

    private float _lastModeChangeTime;
    private const float MODE_CHANGE_COOLDOWN = 0.3f;

    // DLSS 4.5 model presets - K is legacy, M/L need RTX 40+
    public enum DLSSPreset
    {
        Default = 0,
        PresetK = 1,  // RTX 20/30 series
        PresetM = 2,  // RTX 40+ performance
        PresetL = 3   // RTX 40+ 4K ultra
    }

    [Header("Current Settings")]
    [SerializeField] private DLSSMode currentMode = DLSSMode.DLAA;
    [SerializeField] private bool dlssSupported = false;
    [SerializeField] private string gpuName = "";
    [SerializeField] private string statusMessage = "Initializing...";

    [Header("TAA Alternative")]
    [SerializeField] private float taaSharpness = 0.8f;

    [Header("DLSS Quality Settings")]
    [SerializeField] [Range(0f, 1f)] private float dlssSharpness = 0.85f;
    [SerializeField] private bool useDLAAQualityPreset = true;

    [Header("DLSS 4.5 Model Selection")]
    [SerializeField] private DLSSPreset currentPreset = DLSSPreset.Default;
    [SerializeField] private bool isRTX40OrNewer = false;
    private const string PREF_DLSS_PRESET = GameSettings.Keys.DLSS_PRESET;

    [Header("Debug")]
    [SerializeField] private bool showDebugOverlay = false;

    private const string PREF_DLSS_MODE = GameSettings.Keys.DLSS_MODE;

    private float currentDynamicResScale = 1f;
    private bool dynamicResScalerRegistered = false;

    // fps tracking for debug overlay
    private float deltaTime = 0f;
    private float fps = 0f;
    private float minFps = float.MaxValue;
    private float maxFps = 0f;
    private float avgFps = 0f;
    private int frameCount = 0;
    private float fpsSum = 0f;

    // unity doesn't expose HDRP internals publicly so we cache reflection fields once
    // doing 20+ GetField calls per frame would be insane
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

    // cache property lookups too, same reason
    private static readonly System.Collections.Generic.Dictionary<string, PropertyInfo> _propCache
        = new System.Collections.Generic.Dictionary<string, PropertyInfo>(8);

    public DLSSMode CurrentMode => currentMode;
    public bool IsDLSSSupported => dlssSupported;
    public string StatusMessage => statusMessage;

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    private static void LogDebug(string message)
    {
        // too noisy, uncomment when debugging DLSS issues
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
        // disable debug UI or it conflicts with DLSS
        try { DebugManager.instance.enableRuntimeUI = false; }
        catch (System.Exception) { }

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
        LogDebug($"[DLSS] Scene loaded: {scene.name} - reapplying settings");
        ScheduleDLSSApply();
    }

    private void Start()
    {
        RegisterDynamicResScaler();
        ScheduleDLSSApply();
    }

    // deduplicated delayed apply - cameras need ~1 second to fully init before we touch them
    private void ScheduleDLSSApply()
    {
        CancelInvoke(nameof(ApplyCurrentMode));
        Invoke(nameof(ApplyCurrentMode), 1.0f);
    }

    private void RegisterDynamicResScaler()
    {
        if (dynamicResScalerRegistered) return;

        try
        {
            DynamicResolutionHandler.SetDynamicResScaler(GetDynamicResScale, DynamicResScalePolicyType.ReturnsPercentage);
            dynamicResScalerRegistered = true;
            LogDebug("[DLSS] Dynamic Resolution Scaler registered");
        }
        catch (System.Exception e)
        {
            LogError($"[DLSS] Failed to register Dynamic Resolution Scaler: {e.Message}");
        }
    }

    // called by Unity every frame to get our target render percentage
    private float GetDynamicResScale()
    {
        return currentDynamicResScale;
    }

    private float checkTimer = 0f;
    private RenderPipelineAsset lastPipelineAsset;

    private void Update()
    {
#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.F1)) showDebugOverlay = !showDebugOverlay;
#endif

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

        // watchdog: if the pipeline asset changes (e.g. quality level switch), re-apply
        checkTimer += Time.unscaledDeltaTime;
        if (checkTimer > 3.0f)
        {
            checkTimer = 0f;

            if (GraphicsSettings.currentRenderPipeline != lastPipelineAsset)
            {
                if (lastPipelineAsset != null)
                {
                    LogDebug("[DLSS] Pipeline changed, re-applying silently");
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
    }

    private void CheckDLSSSupport()
    {
        gpuName = SystemInfo.graphicsDeviceName;
        string gpuLower = gpuName.ToLower();

        bool isNvidia = gpuLower.Contains("nvidia") || gpuLower.Contains("geforce");
        bool isRTX = gpuLower.Contains("rtx");

        dlssSupported = isNvidia && isRTX;

        isRTX40OrNewer = false;
        if (dlssSupported)
        {
            // RTX 50 (Blackwell)
            if (gpuLower.Contains("rtx 50") || gpuLower.Contains("rtx50") ||
                gpuLower.Contains("5060") || gpuLower.Contains("5070") ||
                gpuLower.Contains("5080") || gpuLower.Contains("5090"))
            {
                isRTX40OrNewer = true;
            }
            // RTX 40 (Ada Lovelace)
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
            LogDebug($"<color=green>[DLSS] Supported! GPU: {gpuName}, RTX40+: {isRTX40OrNewer}</color>");
        }
        else
        {
            statusMessage = $"DLSS Not Supported - {gpuName} (falling back to TAA)";
            LogDebug($"<color=yellow>[DLSS] Not supported. GPU: {gpuName}. Using TAA fallback.</color>");
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

        // clamp just in case someone tampered with playerprefs
        dlssSharpness = Mathf.Clamp01(PlayerPrefs.GetFloat(GameSettings.Keys.DLSS_SHARPNESS, 0.7f));

        int savedPreset = PlayerPrefs.GetInt(PREF_DLSS_PRESET, -1);
        if (savedPreset == -1)
        {
            // auto-pick based on GPU generation
            if (isRTX40OrNewer)
                currentPreset = DLSSPreset.PresetM;
            else if (dlssSupported)
                currentPreset = DLSSPreset.PresetK;
            else
                currentPreset = DLSSPreset.Default;

            LogDebug($"[DLSS] Auto-selected preset: {currentPreset}");
        }
        else
        {
            currentPreset = (DLSSPreset)savedPreset;
            LogDebug($"[DLSS] Loaded saved preset: {currentPreset}");
        }
    }

    public void SetMode(DLSSMode mode)
    {
        // debounce - rapid switches cause pipeline thrashing
        if (Time.realtimeSinceStartup - _lastModeChangeTime < MODE_CHANGE_COOLDOWN)
        {
            LogDebug("[DLSS] Mode change debounced");
            return;
        }
        _lastModeChangeTime = Time.realtimeSinceStartup;

        DLSSMode oldMode = currentMode;
        currentMode = mode;

        PlayerPrefs.SetInt(PREF_DLSS_MODE, (int)mode);
        PlayerPrefs.Save();

        currentDynamicResScale = GetTargetScale(mode);

        ApplyCurrentMode();

        // force a second apply after a short delay, changes don't always stick on first call
        StartCoroutine(ForceReapplyAfterDelay(0.1f));

        LogDebug($"[DLSS] Mode changed: {oldMode} -> {mode}, Scale: {currentDynamicResScale}%");
    }

    public void SetPreset(DLSSPreset preset)
    {
        // TODO: should show a warning in the UI when preset is incompatible, not just fall back silently
        if ((preset == DLSSPreset.PresetM || preset == DLSSPreset.PresetL) && !isRTX40OrNewer)
        {
            LogWarning($"[DLSS] Model {preset} requires RTX 40+. Falling back to Model K.");
            preset = DLSSPreset.PresetK;
        }

        DLSSPreset oldPreset = currentPreset;
        currentPreset = preset;

        PlayerPrefs.SetInt(PREF_DLSS_PRESET, (int)preset);
        PlayerPrefs.Save();

        ApplyCurrentMode();

        LogDebug($"[DLSS] Preset changed: {oldPreset} -> {preset}");
    }

    public DLSSPreset CurrentPreset => currentPreset;
    public bool IsRTX40OrNewer => isRTX40OrNewer;

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

        // toggle AA mode to force Unity to rebuild render state - this took me forever to figure out
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            var hdData = mainCam.GetComponent<HDAdditionalCameraData>();
            if (hdData != null)
            {
                var currentAA = hdData.antialiasing;
                hdData.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                yield return null;
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
        LogDebug($"[DLSS] Applying mode: {currentMode}, supported: {dlssSupported}");

        bool useDLSS = (currentMode != DLSSMode.Off && dlssSupported);
        currentDynamicResScale = GetTargetScale(currentMode);

        ConfigureHDRPAsset(currentMode);

        int appliedCount = 0;

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            ApplyToCamera(mainCam, currentMode);
            appliedCount++;
        }

        // also hit any other active cameras (e.g. portal cameras, preview cams)
        Camera[] allCameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var cam in allCameras)
        {
            if (cam == mainCam) continue;

            if (cam.enabled && cam.gameObject.activeInHierarchy)
            {
                ApplyToCamera(cam, currentMode);
                appliedCount++;
            }
        }

        if (currentMode == DLSSMode.Off || !dlssSupported)
            statusMessage = "TAA Active (DLSS Off)";
        else if (currentMode == DLSSMode.DLAA)
            statusMessage = "DLAA Active (Native Resolution)";
        else
            statusMessage = $"DLSS {GetModeName(currentMode)} Aktif";

        LogDebug($"[DLSS] Applied to {appliedCount} active camera(s)");
    }

    // resolve all FieldInfo objects once on first call so we're not doing 20+ lookups per apply
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
        LogDebug("[DLSS] Reflection cache initialized");
    }

    private void ConfigureHDRPAsset(DLSSMode mode)
    {
        if (GraphicsSettings.currentRenderPipeline == null)
        {
            LogWarning("[DLSS] No Render Pipeline Asset in Graphics Settings");
            return;
        }

        var hdrpAsset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
        if (hdrpAsset == null)
        {
            LogWarning("[DLSS] Current pipeline is not HDRP");
            return;
        }

        InitReflectionCache(hdrpAsset);

        if (_rf_pipelineSettings == null || _rf_dynResSettings == null)
        {
            LogWarning("[DLSS] Reflection cache failed - HDRP API may have changed between versions");
            return;
        }

        try
        {
            object settings = _rf_pipelineSettings.GetValue(hdrpAsset);
            object dynRes = _rf_dynResSettings.GetValue(settings);

            bool useDLSS = (mode != DLSSMode.Off && dlssSupported);
            float targetScale = GetTargetScale(mode);

            _rf_enabled?.SetValue(dynRes, useDLSS);
            if (useDLSS) _rf_dynResType?.SetValue(dynRes, DYNRES_TYPE_HARDWARE);
            _rf_minPercentage?.SetValue(dynRes, targetScale);
            _rf_maxPercentage?.SetValue(dynRes, targetScale);
            _rf_forceResolution?.SetValue(dynRes, useDLSS);
            if (useDLSS) _rf_forcedPercentage?.SetValue(dynRes, targetScale);
            if (useDLSS) _rf_upsampleFilter?.SetValue(dynRes, UPSAMPLE_FILTER_DLSS);

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

            // preset values - 4 = Preset D (works on all RTX), 0 = let NVIDIA auto-pick
            uint renderPreset = currentPreset switch
            {
                DLSSPreset.PresetK => 4,
                DLSSPreset.PresetM => 0,
                DLSSPreset.PresetL => 0,
                _ => 0
            };
            if (_rf_presetFields != null)
            {
                foreach (var pf in _rf_presetFields) pf?.SetValue(dynRes, renderPreset);
            }

            if (mode == DLSSMode.DLAA) _rf_enableAntiGhosting?.SetValue(dynRes, false);
            if (useDLSS) _rf_useMipBias?.SetValue(dynRes, true);

            // write back (struct boxing means we must do this explicitly)
            _rf_dynResSettings.SetValue(settings, dynRes);
            _rf_pipelineSettings.SetValue(hdrpAsset, settings);

            LogDebug($"[DLSS] HDRP Asset configured. Mode: {mode}, Scale: {targetScale}%");
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
            LogDebug($"[DLSS] Added HDAdditionalCameraData to {cam.name}");
        }

        bool useDLSS = (mode != DLSSMode.Off && dlssSupported);

        // TAA is the foundation even when DLSS is on
        hdData.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;
        hdData.taaSharpenStrength = taaSharpness;
        hdData.taaHistorySharpening = 0.6f;
        hdData.taaAntiFlicker = 0.5f;
        hdData.taaMotionVectorRejection = 0.1f;

        if (!useDLSS)
        {
            hdData.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            hdData.allowDynamicResolution = false;
            cam.allowDynamicResolution = false;

            // unity doesn't expose this publicly so we use reflection
            TrySetProperty(hdData, "allowDeepLearningSuperSampling", false);

            LogDebug($"[DLSS] Camera '{cam.name}': TAA mode");
        }
        else
        {
            // DLSS in Unity 6 HDRP works via TAA + Dynamic Resolution + DLSS upscaler
            hdData.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;

            // IMPORTANT: must set allowDynamicResolution on both the Camera AND the HDData component
            hdData.allowDynamicResolution = true;
            cam.allowDynamicResolution = true;

            TrySetProperty(hdData, "allowDeepLearningSuperSampling", true);
            TrySetProperty(hdData, "deepLearningSuperSamplingUseCustomQualitySettings", false);
            TrySetProperty(hdData, "deepLearningSuperSamplingUseOptimalSettings", true);

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
            TrySetProperty(hdData, "deepLearningSuperSamplingSharpening", dlssSharpness);

            UnityEngine.Debug.Log($"[DLSS] Camera '{cam.name}': DLSS Mode={mode}, Quality={quality}, DynRes={cam.allowDynamicResolution}");
        }

        // re-register scaler for upscaling modes to make sure it's still hooked up
        if (useDLSS && mode != DLSSMode.DLAA)
        {
            try {
                DynamicResolutionHandler.SetDynamicResScaler(GetDynamicResScale, DynamicResScalePolicyType.ReturnsPercentage);
            } catch (System.Exception) { }
        }
    }

    // TODO: should probably cache this per-type instead of per-property-name to handle inheritance properly
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

    public void RefreshDLSS()
    {
        ApplyCurrentMode();
    }

    // call this after teleports/rewinds to clear TAA ghosting
    public void ResetCameraHistory()
    {
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);

        foreach (var cam in cameras)
        {
            if (!cam.enabled || !cam.gameObject.activeInHierarchy) continue;

            var hdData = cam.GetComponent<HDAdditionalCameraData>();
            if (hdData != null)
            {
                // FIXME: toggling AA to None and back is a bit hacky but it's the only way
                // to clear temporal buffers without a full pipeline reinit
                var currentAA = hdData.antialiasing;
                hdData.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                hdData.antialiasing = currentAA;

                LogDebug($"[DLSS] Reset AA history for: {cam.name}");
            }
        }
    }

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

    private GUIStyle _dbgBoxStyle;
    private GUIStyle _dbgLabelStyle;
    private GUIStyle _dbgHeaderStyle;
    private GUIStyle _dbgFpsStyle;

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
        GUI.Label(new Rect(x, y, boxWidth - 20, 30), "DLSS DEBUG PANEL", _dbgHeaderStyle);
        y += 35;

        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight), $"GPU: {gpuName}", _dbgLabelStyle);
        y += lineHeight;

        GUI.color = dlssSupported ? Color.green : Color.yellow;
        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight),
            $"DLSS Support: {(dlssSupported ? "YES" : "NO")}", _dbgLabelStyle);
        GUI.color = Color.white;
        y += lineHeight;

        GUI.color = currentMode == DLSSMode.Off ? Color.gray : Color.cyan;
        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight), $"Mode: {GetModeName(currentMode)}", _dbgLabelStyle);
        GUI.color = Color.white;
        y += lineHeight;

        GUI.color = isRTX40OrNewer ? Color.green : Color.yellow;
        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight), $"Preset: {GetPresetName(currentPreset)}", _dbgLabelStyle);
        GUI.color = Color.white;
        y += lineHeight;

        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight), $"Camera AA: {cameraAA}", _dbgLabelStyle);
        y += lineHeight;

        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight), $"Dynamic Res: {dynRes}", _dbgLabelStyle);
        y += lineHeight;

        float actualScale = DynamicResolutionHandler.instance.GetCurrentScale() * 100f;
        int displayW = Screen.width;
        int displayH = Screen.height;
        int renderW = Mathf.RoundToInt(displayW * actualScale / 100f);
        int renderH = Mathf.RoundToInt(displayH * actualScale / 100f);

        GUI.color = actualScale < 99f ? Color.green : Color.yellow;
        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight), $"Render Scale: {actualScale:F1}% (Target: {currentDynamicResScale}%)", _dbgLabelStyle);
        y += lineHeight;

        GUI.Label(new Rect(x, y, boxWidth - 20, lineHeight), $"Render: {renderW}x{renderH} -> Display: {displayW}x{displayH}", _dbgLabelStyle);
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
