using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Graphics Quality Manager - Actually changes visual and performance settings!
/// Modifies shadow quality, LOD, render scale, effects, etc.
/// </summary>
public class GraphicsQualityManager : Singleton<GraphicsQualityManager>
{

    public enum QualityPreset
    {
        High = 0,      // Best visuals
        Medium = 1,    // Balanced
        Low = 2        // Max performance
    }
    
    [Header("Current Settings")]
    [SerializeField] private QualityPreset currentPreset = QualityPreset.High;
    
    [Header("High Quality Settings (Enthusiast - FPS Killer)")]
    [SerializeField] private float highShadowDistance = 200f;
    [SerializeField] private float highLODBias = 2.5f; // Keep high detail even at distance
    [SerializeField] private int highShadowCascades = 4;

    [Header("Medium Quality Settings (Sweet Spot - Recommended)")]
    [SerializeField] private float mediumShadowDistance = 120f;
    [SerializeField] private float mediumLODBias = 1.5f;
    [SerializeField] private int mediumShadowCascades = 3;

    [Header("Low Quality Settings (Max Performance)")]
    [SerializeField] private float lowShadowDistance = 40f;
    [SerializeField] private float lowLODBias = 0.6f;
    [SerializeField] private int lowShadowCascades = 1;
    
    private const string PREF_QUALITY = GameSettings.Keys.QUALITY_LEVEL;
    
    public QualityPreset CurrentPreset => currentPreset;
    
    protected override void OnAwake()
    {
        LoadSettings();
    }
    
    private void LoadSettings()
    {
        int saved = PlayerPrefs.GetInt(PREF_QUALITY, 0); // Default High
        currentPreset = (QualityPreset)Mathf.Clamp(saved, 0, 2);
    }
    
    private bool _startupComplete;

    private void Start()
    {
        // DISABLE AUTO-APPLY ON START COMPLETELY
        // This is the only way to avoid "Blitter already initialized" error in Unity Editor.
        // Quality settings are already set by Project Settings.
        // We only change them when the USER explicitly clicks a button.
        StartCoroutine(MarkStartupComplete());
    }

    private System.Collections.IEnumerator MarkStartupComplete()
    {
        // Wait for HDRP pipeline and cameras to fully initialize
        yield return new WaitForSecondsRealtime(3.0f);
        _startupComplete = true;

        // First launch: auto-detect optimal quality based on GPU
        CheckFirstRunAutoQuality();
    }

    public void SetQuality(QualityPreset preset)
    {
        // Guard: Prevent changes during startup to avoid Blitter reinitialization errors
        if (!_startupComplete)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[GraphicsQualityManager] Ignoring SetQuality({preset}) during startup to prevent Blitter error.");
#endif
            // Still update internal state so UI shows correct value
            currentPreset = preset;
            return;
        }

        currentPreset = preset;
        PlayerPrefs.SetInt(PREF_QUALITY, (int)preset);
        PlayerPrefs.Save();
        
        ApplyCurrentPreset();
#if UNITY_EDITOR
        Debug.Log($"<color=green>[Graphics] Quality set to: {preset}</color>");
#endif
    }
    
    public void SetQuality(int presetIndex)
    {
        SetQuality((QualityPreset)Mathf.Clamp(presetIndex, 0, 2));
    }
    
    private void ApplyCurrentPreset()
    {
        // 1. Apply Unity Quality Level (changes HDRP asset)
        // Only change if different to prevent Blitter reinitialization errors
        int targetLevel = (int)currentPreset;
        if (QualitySettings.GetQualityLevel() != targetLevel)
        {
            QualitySettings.SetQualityLevel(targetLevel, true);
        }

        // 2. Apply additional runtime settings
        switch (currentPreset)
        {
            case QualityPreset.High:
                ApplyHighQuality();
                break;
            case QualityPreset.Medium:
                ApplyMediumQuality();
                break;
            case QualityPreset.Low:
                ApplyLowQuality();
                break;
        }

        // 3. Apply to all cameras (skip AA settings - handled by DLSS Manager)
        ApplyToAllCameras();

        // 4. Apply to Volume settings
        ApplyVolumeSettings();

        // 5. Notify DLSS Manager to re-apply its settings after quality change
        // This prevents race conditions between the two managers
        if (WorkingDLSSManager.Instance != null)
        {
            // Use Coroutine with Realtime wait because Invoke doesn't work when Time.timeScale = 0 (Paused)
            StartCoroutine(RefreshDLSSDelayed());
        }
    }

    private System.Collections.IEnumerator RefreshDLSSDelayed()
    {
        yield return new WaitForSecondsRealtime(0.1f);
        // FIX: Don't reset manual override - just refresh to re-apply current settings
        // ResetManualOverride was causing user's DLSS selections to be ignored!
        if (WorkingDLSSManager.Instance != null)
            WorkingDLSSManager.Instance.RefreshDLSS();
    }
    
    private void ApplyHighQuality()
    {
        // ENTHUSIAST MODE - Maximum visuals, expect FPS drop

        // Shadow settings - Maximum quality
        QualitySettings.shadowDistance = highShadowDistance;
        QualitySettings.shadowCascades = highShadowCascades;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;

        // LOD settings - Aggressive, show high detail everywhere
        QualitySettings.lodBias = highLODBias;
        QualitySettings.maximumLODLevel = 0;

        // Texture settings - Maximum quality
        QualitySettings.globalTextureMipmapLimit = 0; // Full resolution
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;

        // All effects ON
        QualitySettings.softParticles = true;
        QualitySettings.softVegetation = true;
        QualitySettings.realtimeReflectionProbes = true;
        QualitySettings.billboardsFaceCameraPosition = true;
        QualitySettings.skinWeights = SkinWeights.FourBones;

        // High memory budget for streaming
        QualitySettings.streamingMipmapsActive = true;
        QualitySettings.streamingMipmapsMemoryBudget = 2048; // 2GB

        EnsureDynamicResolution();

#if UNITY_EDITOR
        Debug.Log("[Graphics] HIGH (Enthusiast): Shadows=200m, LOD=2.5, Max textures - EXPECT FPS DROP");
#endif
    }
    
    private void ApplyMediumQuality()
    {
        // SWEET SPOT - Great visuals AND smooth gameplay (RECOMMENDED)

        // Shadow settings - Good quality, optimized distance
        QualitySettings.shadowDistance = mediumShadowDistance;
        QualitySettings.shadowCascades = mediumShadowCascades;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.High;

        // LOD settings - Balanced
        QualitySettings.lodBias = mediumLODBias;
        QualitySettings.maximumLODLevel = 0;

        // Texture settings - FULL quality (important for good visuals)
        QualitySettings.globalTextureMipmapLimit = 0; // Full resolution!
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;

        // Most effects ON
        QualitySettings.softParticles = true;
        QualitySettings.softVegetation = true;
        QualitySettings.realtimeReflectionProbes = true;
        QualitySettings.billboardsFaceCameraPosition = true;
        QualitySettings.skinWeights = SkinWeights.FourBones;

        // Good memory budget
        QualitySettings.streamingMipmapsActive = true;
        QualitySettings.streamingMipmapsMemoryBudget = 1024; // 1GB

        EnsureDynamicResolution();

#if UNITY_EDITOR
        Debug.Log("[Graphics] MEDIUM (Recommended): Shadows=120m, LOD=1.5, Full textures, Smooth FPS");
#endif
    }
    
    private void ApplyLowQuality()
    {
        // MAX PERFORMANCE - For low-end systems or competitive play

        QualitySettings.shadowDistance = lowShadowDistance;
        QualitySettings.shadowCascades = lowShadowCascades;
        QualitySettings.shadows = ShadowQuality.HardOnly;
        QualitySettings.shadowResolution = ShadowResolution.Low;
        
        QualitySettings.lodBias = lowLODBias;
        QualitySettings.maximumLODLevel = 1; // Skip highest LOD
        
        QualitySettings.globalTextureMipmapLimit = 1; // Half resolution textures
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
        
        QualitySettings.softParticles = false;
        QualitySettings.softVegetation = false;
        QualitySettings.realtimeReflectionProbes = false;
        QualitySettings.billboardsFaceCameraPosition = false;
        QualitySettings.skinWeights = SkinWeights.TwoBones;
        
        QualitySettings.streamingMipmapsActive = true;
        QualitySettings.streamingMipmapsMemoryBudget = 256;
        
        EnsureDynamicResolution();

#if UNITY_EDITOR
        Debug.Log("[Graphics] LOW quality applied: Shadows=50m, LOD=0.7, Half textures, Volumetrics OFF");
#endif
    }

    private void EnsureDynamicResolution()
    {
        // Force ALL cameras to accept Dynamic Resolution
        var allCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (var cam in allCameras)
        {
            var hdCam = cam.GetComponent<HDAdditionalCameraData>();
            if (hdCam != null)
            {
                hdCam.allowDynamicResolution = true;
                // Don't force custom rendering settings true blindly, as it might break other things, 
                // but for DynRes to work with Override, it's often needed. 
                // Let's trust the Asset settings primarily, but allowDynRes is mandatory.
            }
        }
        
#if UNITY_EDITOR
        var currentPipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        string assetName = currentPipeline != null ? currentPipeline.name : "null";
        Debug.Log($"[Graphics] Pipeline Asset is now: {assetName}. Forced Dynamic Resolution on {allCameras.Length} cameras.");
#endif
    }
    
    private void ApplyToAllCameras()
    {
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);

        foreach (var cam in cameras)
        {
            if (!cam.enabled || !cam.gameObject.activeInHierarchy) continue;

            var hdData = cam.GetComponent<HDAdditionalCameraData>();
            if (hdData == null) continue;

            // Enable custom frame settings override
            hdData.customRenderingSettings = true;
            var frameSettings = hdData.renderingPathCustomFrameSettings;
            var mask = hdData.renderingPathCustomFrameSettingsOverrideMask;

            switch (currentPreset)
            {
                case QualityPreset.High:
                    // ENTHUSIAST: All effects maximum - no override, use HDRP defaults
                    mask.mask[(uint)FrameSettingsField.Volumetrics] = false;
                    mask.mask[(uint)FrameSettingsField.ContactShadows] = false;
                    mask.mask[(uint)FrameSettingsField.SubsurfaceScattering] = false;
                    break;

                case QualityPreset.Medium:
                    // SWEET SPOT: Most effects ON for good visuals
                    mask.mask[(uint)FrameSettingsField.Volumetrics] = false;
                    mask.mask[(uint)FrameSettingsField.ContactShadows] = false;
                    break;

                case QualityPreset.Low:
                    // PERFORMANCE: Disable expensive features
                    mask.mask[(uint)FrameSettingsField.ContactShadows] = true;
                    mask.mask[(uint)FrameSettingsField.Volumetrics] = true;
                    mask.mask[(uint)FrameSettingsField.SubsurfaceScattering] = true;
                    frameSettings.SetEnabled(FrameSettingsField.ContactShadows, false);
                    frameSettings.SetEnabled(FrameSettingsField.Volumetrics, false);
                    frameSettings.SetEnabled(FrameSettingsField.SubsurfaceScattering, false);
                    break;
            }

            hdData.renderingPathCustomFrameSettings = frameSettings;
            hdData.renderingPathCustomFrameSettingsOverrideMask = mask;
        }
    }
    
    private void ApplyVolumeSettings()
    {
        // Find global volume
        var volumes = FindObjectsByType<Volume>(FindObjectsSortMode.None);
        
        foreach (var volume in volumes)
        {
            if (!volume.isGlobal) continue;
            if (volume.profile == null) continue;
            
            // Modify Fog quality and density
            if (volume.profile.TryGet<Fog>(out var fog))
            {
                switch (currentPreset)
                {
                    case QualityPreset.High:
                        fog.quality.value = (int)ScalableSettingLevelParameter.Level.High;
                        fog.enableVolumetricFog.value = true;
                        break;
                    case QualityPreset.Medium:
                        fog.quality.value = (int)ScalableSettingLevelParameter.Level.Medium;
                        fog.enableVolumetricFog.value = true;
                        break;
                    case QualityPreset.Low:
                        fog.quality.value = (int)ScalableSettingLevelParameter.Level.Low;
                        fog.enableVolumetricFog.value = false;
                        break;
                }
            }
            
            // Modify Screen Space Ambient Occlusion
            if (volume.profile.TryGet<ScreenSpaceAmbientOcclusion>(out var ssao))
            {
                switch (currentPreset)
                {
                    case QualityPreset.High:
                        ssao.active = true;
                        ssao.intensity.value = 1.2f; // Extra strong
                        break;
                    case QualityPreset.Medium:
                        ssao.active = true;
                        ssao.intensity.value = 1.0f; // Full quality
                        break;
                    case QualityPreset.Low:
                        ssao.active = false;
                        break;
                }
            }

            // Modify Bloom
            if (volume.profile.TryGet<Bloom>(out var bloom))
            {
                switch (currentPreset)
                {
                    case QualityPreset.High:
                        bloom.active = true;
                        // Keep original intensity
                        break;
                    case QualityPreset.Medium:
                        bloom.active = true;
                        // Keep original intensity for good visuals
                        break;
                    case QualityPreset.Low:
                        bloom.active = true;
                        bloom.intensity.value = Mathf.Min(bloom.intensity.value, 0.3f);
                        break;
                }
            }

            // Modify Screen Space Reflections
            if (volume.profile.TryGet<ScreenSpaceReflection>(out var ssr))
            {
                switch (currentPreset)
                {
                    case QualityPreset.High:
                        ssr.active = true;
                        // Max quality SSR
                        break;
                    case QualityPreset.Medium:
                        ssr.active = true;
                        // Good SSR for nice reflections
                        break;
                    case QualityPreset.Low:
                        ssr.active = false;
                        break;
                }
            }

            // Modify Motion Blur
            if (volume.profile.TryGet<MotionBlur>(out var motionBlur))
            {
                switch (currentPreset)
                {
                    case QualityPreset.High:
                        motionBlur.active = true;
                        break;
                    case QualityPreset.Medium:
                        motionBlur.active = true;
                        break;
                    case QualityPreset.Low:
                        motionBlur.active = false;
                        break;
                }
            }
            
#if UNITY_EDITOR
            Debug.Log($"[Graphics] Volume effects updated for {currentPreset}");
#endif
        }
    }
    
    /// <summary>
    /// Get preset name for UI
    /// </summary>
    public static string GetPresetName(QualityPreset preset)
    {
        return preset switch
        {
            QualityPreset.High => "ULTRA",
            QualityPreset.Medium => "HIGH",
            QualityPreset.Low => "PERFORMANCE",
            _ => preset.ToString()
        };
    }

    public static string GetPresetDescription(QualityPreset preset)
    {
        return preset switch
        {
            QualityPreset.High => "Maximum visuals - lower FPS, RTX recommended",
            QualityPreset.Medium => "Good graphics + smooth gameplay (Recommended)",
            QualityPreset.Low => "Maximum FPS - for competitive play",
            _ => ""
        };
    }
    
    #region Auto Quality Detection
    
    private const string PREF_FIRST_RUN = "Graphics_FirstRun_Done";
    
    /// <summary>
    /// Detect optimal quality preset based on GPU capabilities
    /// </summary>
    public static QualityPreset DetectOptimalQuality()
    {
        string gpu = SystemInfo.graphicsDeviceName.ToLower();
        int vram = SystemInfo.graphicsMemorySize;
        
        Debug.Log($"[Graphics] Detecting optimal quality - GPU: {gpu}, VRAM: {vram}MB");
        
        // RTX 50 Series (Blackwell) - High Quality
        if (gpu.Contains("5090") || gpu.Contains("5080") || gpu.Contains("5070"))
        {
            return QualityPreset.High;
        }

        // RTX 50 Mid-Range - High Quality (still powerful)
        if (gpu.Contains("5060"))
        {
            return QualityPreset.High;
        }

        // RTX 40 Series - High Quality
        if (gpu.Contains("4090") || gpu.Contains("4080") || gpu.Contains("4070"))
        {
            return QualityPreset.High;
        }

        // RTX 40 Mid-Range - Medium/High
        if (gpu.Contains("4060") || gpu.Contains("4050"))
        {
            return vram >= 8000 ? QualityPreset.High : QualityPreset.Medium;
        }

        // RTX 30 Series High-End - High Quality
        if (gpu.Contains("3090") || gpu.Contains("3080") || gpu.Contains("3070 ti") || gpu.Contains("3070"))
        {
            return QualityPreset.High;
        }

        // RTX 30 Mid-Range and RTX 20 Series - Medium
        if (gpu.Contains("3060") || gpu.Contains("3050") ||
            gpu.Contains("2080") || gpu.Contains("2070") || gpu.Contains("2060"))
        {
            return QualityPreset.Medium;
        }

        // AMD High-End (RX 9000/7000 series)
        if (gpu.Contains("rx 9070") || gpu.Contains("rx 7900") || gpu.Contains("rx 6900") || gpu.Contains("rx 6800"))
        {
            return vram >= 12000 ? QualityPreset.High : QualityPreset.Medium;
        }

        // AMD Mid-Range
        if (gpu.Contains("rx 7600") || gpu.Contains("rx 6700") || gpu.Contains("rx 6600") || gpu.Contains("rx 5700"))
        {
            return QualityPreset.Medium;
        }
        
        // VRAM-based fallback
        if (vram >= 8000)
        {
            return QualityPreset.Medium;
        }
        else if (vram >= 4000)
        {
            return QualityPreset.Low;
        }
        
        // Default to Low for integrated graphics or unknown GPUs
        return QualityPreset.Low;
    }
    
    /// <summary>
    /// Check if this is first run and apply auto-detected preset
    /// </summary>
    public void CheckFirstRunAutoQuality()
    {
        if (PlayerPrefs.GetInt(PREF_FIRST_RUN, 0) == 0)
        {
            var detected = DetectOptimalQuality();
            Debug.Log($"[Graphics] First run detected! Auto-setting quality to: {detected}");

            SetQuality(detected);
            PlayerPrefs.SetInt(PREF_FIRST_RUN, 1);
            PlayerPrefs.Save();
        }
    }
    
    /// <summary>
    /// Force re-detect quality (can be called from settings menu)
    /// </summary>
    public void RedetectAndApplyQuality()
    {
        var detected = DetectOptimalQuality();
        SetQuality(detected);
        Debug.Log($"[Graphics] Re-detected optimal quality: {detected}");
    }
    
    #endregion
}
