using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// manages quality presets - shadows, LOD, textures, post effects
// IMPORTANT: don't call SetQuality during startup or you'll get Blitter reinitialization errors
public class GraphicsQualityManager : Singleton<GraphicsQualityManager>
{

    public enum QualityPreset
    {
        High = 0,
        Medium = 1,
        Low = 2
    }

    [Header("Current Settings")]
    [SerializeField] private QualityPreset currentPreset = QualityPreset.High;

    [Header("High Quality Settings")]
    [SerializeField] private float highShadowDistance = 200f;
    [SerializeField] private float highLODBias = 2.5f;
    [SerializeField] private int highShadowCascades = 4;

    [Header("Medium Quality Settings")]
    [SerializeField] private float mediumShadowDistance = 120f;
    [SerializeField] private float mediumLODBias = 1.5f;
    [SerializeField] private int mediumShadowCascades = 3;

    [Header("Low Quality Settings")]
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
        int saved = PlayerPrefs.GetInt(PREF_QUALITY, 0);
        currentPreset = (QualityPreset)Mathf.Clamp(saved, 0, 2);
    }

    private bool _startupComplete;

    private void Start()
    {
        // we can't apply quality changes during startup - Unity throws "Blitter already initialized"
        // so we wait 3 seconds and only change things when the user explicitly asks
        StartCoroutine(MarkStartupComplete());
    }

    private System.Collections.IEnumerator MarkStartupComplete()
    {
        yield return new WaitForSecondsRealtime(3.0f);
        _startupComplete = true;

        CheckFirstRunAutoQuality();
    }

    public void SetQuality(QualityPreset preset)
    {
        if (!_startupComplete)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[GraphicsQualityManager] Ignoring SetQuality({preset}) during startup to prevent Blitter error.");
#endif
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
        // only switch if actually different - prevents unnecessary Blitter reinit
        int targetLevel = (int)currentPreset;
        if (QualitySettings.GetQualityLevel() != targetLevel)
        {
            QualitySettings.SetQualityLevel(targetLevel, true);
        }

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

        ApplyToAllCameras();
        ApplyVolumeSettings();

        // tell DLSS to re-apply after quality change - they can race each other otherwise
        if (WorkingDLSSManager.Instance != null)
        {
            // using coroutine not Invoke because Invoke breaks when Time.timeScale = 0 (pause menu)
            StartCoroutine(RefreshDLSSDelayed());
        }
    }

    private System.Collections.IEnumerator RefreshDLSSDelayed()
    {
        yield return new WaitForSecondsRealtime(0.1f);
        // just refresh, don't reset - resetting caused user DLSS selections to get wiped
        if (WorkingDLSSManager.Instance != null)
            WorkingDLSSManager.Instance.RefreshDLSS();
    }

    private void ApplyHighQuality()
    {
        QualitySettings.shadowDistance = highShadowDistance;
        QualitySettings.shadowCascades = highShadowCascades;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;

        QualitySettings.lodBias = highLODBias;
        QualitySettings.maximumLODLevel = 0;

        QualitySettings.globalTextureMipmapLimit = 0;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;

        QualitySettings.softParticles = true;
        QualitySettings.softVegetation = true;
        QualitySettings.realtimeReflectionProbes = true;
        QualitySettings.billboardsFaceCameraPosition = true;
        QualitySettings.skinWeights = SkinWeights.FourBones;

        QualitySettings.streamingMipmapsActive = true;
        QualitySettings.streamingMipmapsMemoryBudget = 2048;

        EnsureDynamicResolution();

#if UNITY_EDITOR
        Debug.Log("[Graphics] HIGH: Shadows=200m, LOD=2.5, max textures");
#endif
    }

    private void ApplyMediumQuality()
    {
        QualitySettings.shadowDistance = mediumShadowDistance;
        QualitySettings.shadowCascades = mediumShadowCascades;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.High;

        QualitySettings.lodBias = mediumLODBias;
        QualitySettings.maximumLODLevel = 0;

        // keeping textures at full res even on medium - makes a big visual difference
        QualitySettings.globalTextureMipmapLimit = 0;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;

        QualitySettings.softParticles = true;
        QualitySettings.softVegetation = true;
        QualitySettings.realtimeReflectionProbes = true;
        QualitySettings.billboardsFaceCameraPosition = true;
        QualitySettings.skinWeights = SkinWeights.FourBones;

        QualitySettings.streamingMipmapsActive = true;
        QualitySettings.streamingMipmapsMemoryBudget = 1024;

        EnsureDynamicResolution();

#if UNITY_EDITOR
        Debug.Log("[Graphics] MEDIUM: Shadows=120m, LOD=1.5, full textures");
#endif
    }

    private void ApplyLowQuality()
    {
        QualitySettings.shadowDistance = lowShadowDistance;
        QualitySettings.shadowCascades = lowShadowCascades;
        QualitySettings.shadows = ShadowQuality.HardOnly;
        QualitySettings.shadowResolution = ShadowResolution.Low;

        QualitySettings.lodBias = lowLODBias;
        QualitySettings.maximumLODLevel = 1;

        QualitySettings.globalTextureMipmapLimit = 1;
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
        Debug.Log("[Graphics] LOW: Shadows=50m, LOD=0.7, half textures, volumetrics OFF");
#endif
    }

    private void EnsureDynamicResolution()
    {
        // TODO: should probably only do this to cameras that actually need it, not all of them
        var allCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (var cam in allCameras)
        {
            var hdCam = cam.GetComponent<HDAdditionalCameraData>();
            if (hdCam != null)
            {
                hdCam.allowDynamicResolution = true;
            }
        }

#if UNITY_EDITOR
        var currentPipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        string assetName = currentPipeline != null ? currentPipeline.name : "null";
        Debug.Log($"[Graphics] Pipeline: {assetName}. Forced DynRes on {allCameras.Length} cameras.");
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

            hdData.customRenderingSettings = true;
            var frameSettings = hdData.renderingPathCustomFrameSettings;
            var mask = hdData.renderingPathCustomFrameSettingsOverrideMask;

            switch (currentPreset)
            {
                case QualityPreset.High:
                    mask.mask[(uint)FrameSettingsField.Volumetrics] = false;
                    mask.mask[(uint)FrameSettingsField.ContactShadows] = false;
                    mask.mask[(uint)FrameSettingsField.SubsurfaceScattering] = false;
                    break;

                case QualityPreset.Medium:
                    mask.mask[(uint)FrameSettingsField.Volumetrics] = false;
                    mask.mask[(uint)FrameSettingsField.ContactShadows] = false;
                    break;

                case QualityPreset.Low:
                    // FIXME: disabling volumetrics here AND in the volume profile might be redundant
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
        var volumes = FindObjectsByType<Volume>(FindObjectsSortMode.None);

        foreach (var volume in volumes)
        {
            if (!volume.isGlobal) continue;
            if (volume.profile == null) continue;

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

            if (volume.profile.TryGet<ScreenSpaceAmbientOcclusion>(out var ssao))
            {
                switch (currentPreset)
                {
                    case QualityPreset.High:
                        ssao.active = true;
                        ssao.intensity.value = 1.2f;
                        break;
                    case QualityPreset.Medium:
                        ssao.active = true;
                        ssao.intensity.value = 1.0f;
                        break;
                    case QualityPreset.Low:
                        ssao.active = false;
                        break;
                }
            }

            if (volume.profile.TryGet<Bloom>(out var bloom))
            {
                switch (currentPreset)
                {
                    case QualityPreset.High:
                        bloom.active = true;
                        break;
                    case QualityPreset.Medium:
                        bloom.active = true;
                        break;
                    case QualityPreset.Low:
                        bloom.active = true;
                        bloom.intensity.value = Mathf.Min(bloom.intensity.value, 0.3f);
                        break;
                }
            }

            if (volume.profile.TryGet<ScreenSpaceReflection>(out var ssr))
            {
                switch (currentPreset)
                {
                    case QualityPreset.High:
                        ssr.active = true;
                        break;
                    case QualityPreset.Medium:
                        ssr.active = true;
                        break;
                    case QualityPreset.Low:
                        ssr.active = false;
                        break;
                }
            }

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

    private const string PREF_FIRST_RUN = "Graphics_FirstRun_Done";

    // tries to pick a reasonable default based on GPU name/VRAM - not perfect but good enough
    public static QualityPreset DetectOptimalQuality()
    {
        string gpu = SystemInfo.graphicsDeviceName.ToLower();
        int vram = SystemInfo.graphicsMemorySize;

        Debug.Log($"[Graphics] Detecting quality - GPU: {gpu}, VRAM: {vram}MB");

        // RTX 50 (Blackwell)
        if (gpu.Contains("5090") || gpu.Contains("5080") || gpu.Contains("5070"))
            return QualityPreset.High;

        if (gpu.Contains("5060"))
            return QualityPreset.High;

        // RTX 40
        if (gpu.Contains("4090") || gpu.Contains("4080") || gpu.Contains("4070"))
            return QualityPreset.High;

        if (gpu.Contains("4060") || gpu.Contains("4050"))
            return vram >= 8000 ? QualityPreset.High : QualityPreset.Medium;

        // RTX 30 high-end
        if (gpu.Contains("3090") || gpu.Contains("3080") || gpu.Contains("3070 ti") || gpu.Contains("3070"))
            return QualityPreset.High;

        // RTX 30 mid / RTX 20
        if (gpu.Contains("3060") || gpu.Contains("3050") ||
            gpu.Contains("2080") || gpu.Contains("2070") || gpu.Contains("2060"))
            return QualityPreset.Medium;

        // AMD high-end
        if (gpu.Contains("rx 9070") || gpu.Contains("rx 7900") || gpu.Contains("rx 6900") || gpu.Contains("rx 6800"))
            return vram >= 12000 ? QualityPreset.High : QualityPreset.Medium;

        // AMD mid
        if (gpu.Contains("rx 7600") || gpu.Contains("rx 6700") || gpu.Contains("rx 6600") || gpu.Contains("rx 5700"))
            return QualityPreset.Medium;

        // fallback by VRAM
        // TODO: this doesn't account for integrated graphics sharing system RAM
        if (vram >= 8000)
            return QualityPreset.Medium;
        else if (vram >= 4000)
            return QualityPreset.Low;

        return QualityPreset.Low;
    }

    public void CheckFirstRunAutoQuality()
    {
        if (PlayerPrefs.GetInt(PREF_FIRST_RUN, 0) == 0)
        {
            var detected = DetectOptimalQuality();
            Debug.Log($"[Graphics] First run - auto quality: {detected}");

            SetQuality(detected);
            PlayerPrefs.SetInt(PREF_FIRST_RUN, 1);
            PlayerPrefs.Save();
        }
    }

    public void RedetectAndApplyQuality()
    {
        var detected = DetectOptimalQuality();
        SetQuality(detected);
        Debug.Log($"[Graphics] Re-detected quality: {detected}");
    }
}
