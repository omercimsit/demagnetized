using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System.Collections.Generic;

/// <summary>
/// Centralized Game Settings Manager.
/// Handles all game settings with unified load/save and apply functionality.
/// Singleton pattern for global access.
/// </summary>
public class GameSettings : MonoBehaviour
{
    #region Singleton
    
    private static GameSettings _instance;
    public static GameSettings Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<GameSettings>();
                if (_instance == null)
                {
                    var go = new GameObject("GameSettings");
                    _instance = go.AddComponent<GameSettings>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }
    
    #endregion
    
    #region PlayerPrefs Keys
    
    public static class Keys
    {
        // Audio
        public const string MASTER_VOLUME = "MasterVolume";
        public const string MUSIC_VOLUME = "MusicVolume";
        public const string SFX_VOLUME = "SFXVolume";
        public const string AMBIENT_VOLUME = "AmbientVolume";
        
        // Controls
        public const string MOUSE_SENSITIVITY = "MouseSensitivity";
        public const string INVERT_Y = "InvertY";
        public const string FIELD_OF_VIEW = "FieldOfView";
        
        // Display
        public const string QUALITY_LEVEL = "QualityLevel";
        public const string GAMMA = "Gamma";
        public const string DLSS_MODE = "DLSSMode";
        public const string DLSS_SHARPNESS = "DLSSSharpness";
        public const string DLSS_PRESET = "DLSSPreset";
        public const string FULLSCREEN = "FullScreen";
        public const string VSYNC = "VSync";
        public const string TARGET_FPS = "TargetFPS";
        public const string RES_WIDTH = "ResWidth";
        public const string RES_HEIGHT = "ResHeight";
        
        // Effects
        public const string FX_MOTION_BLUR = "FX_MotionBlur";
        public const string FX_BLOOM = "FX_Bloom";
        public const string FX_FILM_GRAIN = "FX_FilmGrain";
        public const string FX_VIGNETTE = "FX_Vignette";
        public const string FX_SSAO = "FX_SSAO";
        public const string FX_SSR = "FX_SSR";
        public const string FX_VOLUMETRIC_FOG = "FX_VolumetricFog";
        public const string FX_CHROMATIC_ABERRATION = "FX_ChromaticAberration";
    }
    
    #endregion
    
    #region Settings Values
    
    [Header("Audio Settings")]
    [Range(0f, 1f)] public float MasterVolume = 1f;
    [Range(0f, 1f)] public float MusicVolume = 0.5f;
    [Range(0f, 1f)] public float SFXVolume = 0.8f;
    [Range(0f, 1f)] public float AmbientVolume = 0.6f;
    
    [Header("Control Settings")]
    [Range(0.1f, 2f)] public float MouseSensitivity = 0.5f;
    public bool InvertY = false;
    [Range(60f, 120f)] public float FieldOfView = 75f;
    
    [Header("Display Settings")]
    public int QualityLevel = 0;
    [Range(0.5f, 2f)] public float Gamma = 1f;
    public int DLSSMode = 0;
    [Range(0f, 1f)] public float DLSSSharpness = 0.5f;
    public bool IsFullscreen = true;
    public bool VSync = false;
    
    [Header("Post-Processing Effects")]
    public bool MotionBlur = false;
    public bool Bloom = true;
    public bool FilmGrain = true;
    public bool Vignette = true;
    public bool SSAO = true;
    public bool SSR = false;
    public bool VolumetricFog = true;
    public bool ChromaticAberration = true;
    
    #endregion
    
    #region Events
    
    public event System.Action OnSettingsChanged;
    public event System.Action OnAudioSettingsChanged;
    public event System.Action OnGraphicsSettingsChanged;
    
    #endregion

    #region Runtime Caches

    private readonly List<Volume> _volumeCache = new List<Volume>(16);
    private float _lastVolumeCacheRefresh;
    private const float VOLUME_CACHE_REFRESH_INTERVAL = 2f;

    #endregion
    
    #region Lifecycle
    
    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        _instance = this;
        
        // DontDestroyOnLoad only works on root objects
        if (transform.parent != null)
        {
            transform.SetParent(null);
        }
        DontDestroyOnLoad(gameObject);
        
        LoadAll();
    }
    
    #endregion
    
    #region Load / Save
    
    /// <summary>
    /// Load all settings from PlayerPrefs
    /// </summary>
    public void LoadAll()
    {
        // Audio
        MasterVolume = PlayerPrefs.GetFloat(Keys.MASTER_VOLUME, 1f);
        MusicVolume = PlayerPrefs.GetFloat(Keys.MUSIC_VOLUME, 0.5f);
        SFXVolume = PlayerPrefs.GetFloat(Keys.SFX_VOLUME, 0.8f);
        AmbientVolume = PlayerPrefs.GetFloat(Keys.AMBIENT_VOLUME, 0.6f);
        
        // Controls
        MouseSensitivity = PlayerPrefs.GetFloat(Keys.MOUSE_SENSITIVITY, 0.5f);
        InvertY = PlayerPrefs.GetInt(Keys.INVERT_Y, 0) == 1;
        FieldOfView = PlayerPrefs.GetFloat(Keys.FIELD_OF_VIEW, 75f);
        
        // Display
        QualityLevel = PlayerPrefs.GetInt(Keys.QUALITY_LEVEL, 0);
        Gamma = PlayerPrefs.GetFloat(Keys.GAMMA, 1f);
        DLSSMode = PlayerPrefs.GetInt(Keys.DLSS_MODE, 0);
        DLSSSharpness = PlayerPrefs.GetFloat(Keys.DLSS_SHARPNESS, 0.5f);
        IsFullscreen = Screen.fullScreen;
        VSync = QualitySettings.vSyncCount > 0;
        
        // Effects
        MotionBlur = PlayerPrefs.GetInt(Keys.FX_MOTION_BLUR, 0) == 1;
        Bloom = PlayerPrefs.GetInt(Keys.FX_BLOOM, 1) == 1;
        FilmGrain = PlayerPrefs.GetInt(Keys.FX_FILM_GRAIN, 1) == 1;
        Vignette = PlayerPrefs.GetInt(Keys.FX_VIGNETTE, 1) == 1;
        SSAO = PlayerPrefs.GetInt(Keys.FX_SSAO, 1) == 1;
        SSR = PlayerPrefs.GetInt(Keys.FX_SSR, 0) == 1;
        VolumetricFog = PlayerPrefs.GetInt(Keys.FX_VOLUMETRIC_FOG, 1) == 1;
        ChromaticAberration = PlayerPrefs.GetInt(Keys.FX_CHROMATIC_ABERRATION, 1) == 1;
        
        // Apply audio immediately
        AudioListener.volume = MasterVolume;
        
#if UNITY_EDITOR
        Debug.Log("[GameSettings] All settings loaded");
#endif
    }
    
    /// <summary>
    /// Save all settings to PlayerPrefs
    /// </summary>
    public void SaveAll()
    {
        // Audio
        PlayerPrefs.SetFloat(Keys.MASTER_VOLUME, MasterVolume);
        PlayerPrefs.SetFloat(Keys.MUSIC_VOLUME, MusicVolume);
        PlayerPrefs.SetFloat(Keys.SFX_VOLUME, SFXVolume);
        PlayerPrefs.SetFloat(Keys.AMBIENT_VOLUME, AmbientVolume);
        
        // Controls
        PlayerPrefs.SetFloat(Keys.MOUSE_SENSITIVITY, MouseSensitivity);
        PlayerPrefs.SetInt(Keys.INVERT_Y, InvertY ? 1 : 0);
        PlayerPrefs.SetFloat(Keys.FIELD_OF_VIEW, FieldOfView);
        
        // Display
        PlayerPrefs.SetInt(Keys.QUALITY_LEVEL, QualityLevel);
        PlayerPrefs.SetFloat(Keys.GAMMA, Gamma);
        PlayerPrefs.SetInt(Keys.DLSS_MODE, DLSSMode);
        PlayerPrefs.SetFloat(Keys.DLSS_SHARPNESS, DLSSSharpness);
        
        // Effects
        PlayerPrefs.SetInt(Keys.FX_MOTION_BLUR, MotionBlur ? 1 : 0);
        PlayerPrefs.SetInt(Keys.FX_BLOOM, Bloom ? 1 : 0);
        PlayerPrefs.SetInt(Keys.FX_FILM_GRAIN, FilmGrain ? 1 : 0);
        PlayerPrefs.SetInt(Keys.FX_VIGNETTE, Vignette ? 1 : 0);
        PlayerPrefs.SetInt(Keys.FX_SSAO, SSAO ? 1 : 0);
        PlayerPrefs.SetInt(Keys.FX_SSR, SSR ? 1 : 0);
        PlayerPrefs.SetInt(Keys.FX_VOLUMETRIC_FOG, VolumetricFog ? 1 : 0);
        PlayerPrefs.SetInt(Keys.FX_CHROMATIC_ABERRATION, ChromaticAberration ? 1 : 0);
        
        PlayerPrefs.Save();
        OnSettingsChanged?.Invoke();
        GameEvents.InvokeSettingsChanged();

#if UNITY_EDITOR
        Debug.Log("[GameSettings] All settings saved");
#endif
    }
    
    #endregion
    
    #region Apply Methods
    
    /// <summary>
    /// Apply audio settings
    /// </summary>
    public void ApplyAudio()
    {
        AudioListener.volume = MasterVolume;
        
        // Notify AudioManager if exists
        var audioManager = ServiceLocator.Instance != null ? ServiceLocator.Instance.Audio : AudioManager.Instance;
        if (audioManager != null)
        {
            audioManager.SetMasterVolume(MasterVolume);
            audioManager.SetMusicVolume(MusicVolume);
            audioManager.SetSFXVolume(SFXVolume);
        }
        
        SaveAll();
        OnAudioSettingsChanged?.Invoke();
        GameEvents.InvokeAudioSettingsChanged();
    }
    
    /// <summary>
    /// Apply display settings (quality, vsync, fullscreen)
    /// </summary>
    public void ApplyDisplay()
    {
        Screen.fullScreen = IsFullscreen;
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        
        // NOTE: Do NOT call QualitySettings.SetQualityLevel here!
        // It causes "Blitter already initialized" errors in HDRP
        // GraphicsQualityManager is the ONLY place that should change quality level
        
        // Apply DLSS if supported
        var dlss = ServiceLocator.Instance != null ? ServiceLocator.Instance.DLSS : WorkingDLSSManager.Instance;
        if (dlss != null && dlss.IsDLSSSupported)
        {
            dlss.SetMode((WorkingDLSSManager.DLSSMode)DLSSMode);
        }
        
        // Apply via GraphicsQualityManager (this handles quality level safely)
        var graphics = ServiceLocator.Instance != null ? ServiceLocator.Instance.GraphicsQuality : GraphicsQualityManager.Instance;
        if (graphics != null)
        {
            graphics.SetQuality(QualityLevel);
        }
        
        SaveAll();
        OnGraphicsSettingsChanged?.Invoke();
        GameEvents.InvokeGraphicsSettingsChanged();
    }

    /// <summary>
    /// Apply camera settings (FOV, gamma)
    /// </summary>
    public void ApplyCamera()
    {
        Camera cam = ServiceLocator.Instance != null ? ServiceLocator.Instance.MainCamera : Camera.main;
        if (cam != null)
        {
            cam.fieldOfView = FieldOfView;
        }
        
        // Apply gamma via volumes
        foreach (var v in GetCachedVolumes())
        {
            if (v.profile == null) continue;
            if (v.profile.TryGet<LiftGammaGain>(out var lgg))
            {
                lgg.gamma.value = new Vector4(1f, 1f, 1f, Gamma);
            }
        }
        
        SaveAll();
    }
    
    /// <summary>
    /// Apply post-processing effect settings
    /// </summary>
    public void ApplyEffects()
    {
        foreach (var v in GetCachedVolumes())
        {
            if (v.profile == null) continue;
            
            if (v.profile.TryGet<Bloom>(out var bloom)) 
                bloom.active = Bloom;
            if (v.profile.TryGet<MotionBlur>(out var mb)) 
                mb.active = MotionBlur;
            if (v.profile.TryGet<Vignette>(out var vig)) 
                vig.active = Vignette;
            if (v.profile.TryGet<FilmGrain>(out var fg)) 
                fg.active = FilmGrain;
            if (v.profile.TryGet<ScreenSpaceAmbientOcclusion>(out var ssao)) 
                ssao.active = SSAO;
            if (v.profile.TryGet<ScreenSpaceReflection>(out var ssr)) 
                ssr.active = SSR;
            if (v.profile.TryGet<Fog>(out var fog)) 
                fog.enableVolumetricFog.value = VolumetricFog;
            if (v.profile.TryGet<ChromaticAberration>(out var ca)) 
                ca.active = ChromaticAberration;
        }
        
        SaveAll();
        OnGraphicsSettingsChanged?.Invoke();
        GameEvents.InvokeGraphicsSettingsChanged();
    }

    /// <summary>
    /// Apply all settings at once
    /// </summary>
    public void ApplyAll()
    {
        ApplyAudio();
        ApplyDisplay();
        ApplyCamera();
        ApplyEffects();
    }
    
    #endregion
    
    #region Reset to Defaults
    
    /// <summary>
    /// Reset all settings to factory defaults
    /// </summary>
    public void ResetToDefaults()
    {
        // Audio
        MasterVolume = 1f;
        MusicVolume = 0.5f;
        SFXVolume = 0.8f;
        AmbientVolume = 0.6f;
        
        // Controls
        MouseSensitivity = 0.5f;
        InvertY = false;
        FieldOfView = 75f;
        
        // Display
        QualityLevel = 0;
        Gamma = 1f;
        DLSSMode = 0;
        DLSSSharpness = 0.5f;
        IsFullscreen = true;
        VSync = false;
        
        // Effects
        MotionBlur = false;
        Bloom = true;
        FilmGrain = true;
        Vignette = true;
        SSAO = true;
        SSR = false;
        VolumetricFog = true;
        ChromaticAberration = true;
        
        ApplyAll();
        
#if UNITY_EDITOR
        Debug.Log("[GameSettings] Reset to defaults");
#endif
    }
    
    #endregion
    
    #region Convenience Setters (with auto-save)
    
    public void SetMasterVolume(float value)
    {
        MasterVolume = Mathf.Clamp01(value);
        ApplyAudio();
    }
    
    public void SetMusicVolume(float value)
    {
        MusicVolume = Mathf.Clamp01(value);
        ApplyAudio();
    }
    
    public void SetSFXVolume(float value)
    {
        SFXVolume = Mathf.Clamp01(value);
        ApplyAudio();
    }
    
    public void SetMouseSensitivity(float value)
    {
        MouseSensitivity = Mathf.Clamp(value, 0.1f, 2f);
        SaveAll();
    }
    
    public void SetFieldOfView(float value)
    {
        FieldOfView = Mathf.Clamp(value, 60f, 120f);
        ApplyCamera();
    }
    
    public void SetQuality(int level)
    {
        QualityLevel = Mathf.Clamp(level, 0, QualitySettings.names.Length - 1);
        ApplyDisplay();
    }
    
    public void SetDLSS(int mode)
    {
        DLSSMode = mode;
        ApplyDisplay();
    }
    
    public void SetEffect(string effectName, bool enabled)
    {
        switch (effectName)
        {
            case "MotionBlur": MotionBlur = enabled; break;
            case "Bloom": Bloom = enabled; break;
            case "FilmGrain": FilmGrain = enabled; break;
            case "Vignette": Vignette = enabled; break;
            case "SSAO": SSAO = enabled; break;
            case "SSR": SSR = enabled; break;
            case "VolumetricFog": VolumetricFog = enabled; break;
            case "ChromaticAberration": ChromaticAberration = enabled; break;
        }
        ApplyEffects();
    }
    
    #endregion

    #region Internal Helpers

    private IReadOnlyList<Volume> GetCachedVolumes()
    {
        if (_volumeCache.Count == 0 || Time.unscaledTime - _lastVolumeCacheRefresh > VOLUME_CACHE_REFRESH_INTERVAL)
        {
            _volumeCache.Clear();
            _volumeCache.AddRange(FindObjectsByType<Volume>(FindObjectsSortMode.None));
            _lastVolumeCacheRefresh = Time.unscaledTime;
        }
        return _volumeCache;
    }

    #endregion
}
