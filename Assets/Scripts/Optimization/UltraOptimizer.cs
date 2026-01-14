using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System.Collections.Generic;

/// <summary>
/// Runtime optimizer for HDRP projects. Handles LOD cross-fading, GPU instancing,
/// distance-based shadow culling for dynamic objects, and reflection probe toggling.
/// Global quality settings are applied once at startup to avoid Blitter re-initialization errors.
/// </summary>
public class UltraOptimizer : Singleton<UltraOptimizer>
{
    [Header("Texture & Memory")]
    [Tooltip("Streaming mipmap memory budget in MB. 3584 targets ~3.5 GB for 4 GB GPUs.")]
    [SerializeField] private int textureBudgetMB = 3584;

    [Header("LOD System")]
    [Tooltip("Higher values keep high-detail LODs visible at greater distances.")]
    [SerializeField] private float lodBias = 4f;
    [Tooltip("Smooth cross-fade between LOD levels to eliminate pop-in.")]
    [SerializeField] private bool enableLODCrossFade = true;

    [Header("Shadow Optimization")]
    // TODO: make shadow distance threshold configurable per quality tier
    [Tooltip("Maximum distance at which shadows are visible.")]
    [SerializeField] private float shadowDistance = 150f;
    [Tooltip("Dynamic objects beyond this distance stop casting shadows.")]
    [SerializeField] private float shadowCasterMaxDistance = 40f;

    [Header("GPU Instancing")]
    [Tooltip("Enable GPU instancing on all eligible materials to reduce draw calls.")]
    [SerializeField] private bool enableGPUInstancing = true;

    [Header("Lighting")]
    [Tooltip("Reflection probes beyond this distance are disabled each frame.")]
    [SerializeField] private float reflectionProbeDistance = 50f;
    
    // Runtime
    private Transform cameraTransform;
    private List<ShadowCaster> shadowCasters = new List<ShadowCaster>();
    private List<ReflectionProbe> reflectionProbes = new List<ReflectionProbe>();
    private float lastOptimizeTime;
    private static bool settingsApplied = false;
    
    private class ShadowCaster
    {
        public Renderer renderer;
        public Transform transform;
        public ShadowCastingMode originalMode;
    }
    
    protected override void OnAwake() { }
    
    private void Start()
    {
        Initialize();
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
        Initialize();
    }
    
    private void Initialize()
    {
        cameraTransform = Camera.main?.transform;
        
        // Only apply global settings ONCE to prevent Blitter re-initialization
        if (!settingsApplied)
        {
            ApplyMaxQualitySettings();
            settingsApplied = true;
        }
        
        SetupLODSystem();
        SetupGPUInstancing();
        CollectShadowCasters();
        CollectReflectionProbes();
        DisableBadCullingScripts();
        
        Debug.Log("[AAA Optimizer] Systems active (settings: " + (settingsApplied ? "applied" : "skipped") + ")");
    }
    
    private void Update()
    {
        if (Time.time - lastOptimizeTime > 0.1f)
        {
            OptimizeShadowCasters();
            OptimizeReflectionProbes();
            lastOptimizeTime = Time.time;
        }
    }
    
    #region === QUALITY SETTINGS ===
    
    /// <summary>
    /// Applies safe quality settings that do not touch GraphicsSettings or the HDRP pipeline asset.
    /// Modifying GraphicsSettings.useScriptableRenderPipelineBatching at runtime triggers a Blitter
    /// re-initialization error in HDRP — that flag must be set through Project Settings instead.
    /// Shadow cascade count, shadow resolution, and shadow mode are also left to the HDRP asset.
    /// </summary>
    private void ApplyMaxQualitySettings()
    {
        QualitySettings.streamingMipmapsActive = true;
        QualitySettings.streamingMipmapsMemoryBudget = textureBudgetMB;
        QualitySettings.streamingMipmapsMaxLevelReduction = 0;
        QualitySettings.streamingMipmapsAddAllCameras = true;
        QualitySettings.streamingMipmapsRenderersPerFrame = 2048;

        QualitySettings.lodBias = lodBias;
        QualitySettings.maximumLODLevel = 0;

        QualitySettings.shadowDistance = shadowDistance;

        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
        QualitySettings.softParticles = true;
        QualitySettings.realtimeReflectionProbes = true;
        QualitySettings.skinWeights = SkinWeights.FourBones;

        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;

        Debug.Log("[AAA Optimizer] Quality settings applied.");
    }
    
    #endregion
    
    #region === LOD SYSTEM ===
    
    private void SetupLODSystem()
    {
        if (!enableLODCrossFade) return;

        LODGroup[] lodGroups = FindObjectsByType<LODGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var lodGroup in lodGroups)
        {
            if (lodGroup == null) continue;

            lodGroup.fadeMode = LODFadeMode.CrossFade;
            lodGroup.animateCrossFading = true;
        }

        Debug.Log($"[AAA Optimizer] {lodGroups.Length} LOD groups configured for cross-fade.");
    }
    
    #endregion
    
    #region === GPU INSTANCING ===
    
    private void SetupGPUInstancing()
    {
        if (!enableGPUInstancing) return;

        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int enabled = 0;

        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            if (renderer is ParticleSystemRenderer) continue;

            foreach (var mat in renderer.sharedMaterials)
            {
                if (mat == null) continue;
                if (!mat.enableInstancing)
                {
                    mat.enableInstancing = true;
                    enabled++;
                }
            }
        }

        Debug.Log($"[AAA Optimizer] GPU instancing enabled on {enabled} materials.");
    }
    
    #endregion
    
    #region === SHADOW OPTIMIZATION ===
    
    private void CollectShadowCasters()
    {
        shadowCasters.Clear();
        
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            if (renderer is ParticleSystemRenderer) continue;
            if (renderer.shadowCastingMode == ShadowCastingMode.Off) continue;
            
            // Static objects use baked shadows and are not managed at runtime.
            if (renderer.gameObject.isStatic) continue;
            
            shadowCasters.Add(new ShadowCaster
            {
                renderer = renderer,
                transform = renderer.transform,
                originalMode = renderer.shadowCastingMode
            });
        }
    }
    
    /// <summary>
    /// Disables shadow casting on dynamic renderers beyond shadowCasterMaxDistance.
    /// The renderer itself remains visible — only shadow casting is toggled.
    /// </summary>
    private void OptimizeShadowCasters()
    {
        if (cameraTransform == null)
        {
            cameraTransform = Camera.main?.transform;
            if (cameraTransform == null) return;
        }
        
        Vector3 camPos = cameraTransform.position;
        float sqrMaxDist = shadowCasterMaxDistance * shadowCasterMaxDistance;
        
        foreach (var caster in shadowCasters)
        {
            if (caster.renderer == null) continue;
            
            float sqrDist = (caster.transform.position - camPos).sqrMagnitude;
            
            if (sqrDist <= sqrMaxDist)
            {
                if (caster.renderer.shadowCastingMode != caster.originalMode)
                    caster.renderer.shadowCastingMode = caster.originalMode;
            }
            else
            {
                if (caster.renderer.shadowCastingMode != ShadowCastingMode.Off)
                    caster.renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }
    }
    
    #endregion
    
    #region === REFLECTION PROBE OPTIMIZATION ===
    
    private void CollectReflectionProbes()
    {
        reflectionProbes.Clear();
        
        ReflectionProbe[] probes = FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        reflectionProbes.AddRange(probes);
    }
    
    private void OptimizeReflectionProbes()
    {
        if (cameraTransform == null) return;
        
        Vector3 camPos = cameraTransform.position;
        float sqrMaxDist = reflectionProbeDistance * reflectionProbeDistance;
        
        foreach (var probe in reflectionProbes)
        {
            if (probe == null) continue;
            
            float sqrDist = (probe.transform.position - camPos).sqrMagnitude;
            bool shouldBeActive = sqrDist <= sqrMaxDist;
            
            if (probe.enabled != shouldBeActive)
                probe.enabled = shouldBeActive;
        }
    }
    
    #endregion
    
    #region === CLEANUP ===
    
    private void DisableBadCullingScripts()
    {
        var allScripts = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        
        foreach (var mb in allScripts)
        {
            if (mb == null || mb == this) continue;
            string typeName = mb.GetType().Name;
            
            if (typeName == "AdvancedCullingSystem") continue;
            
            if (typeName.Contains("FrustumCulling") || 
                typeName.Contains("DistanceCulling") ||
                typeName.Contains("CullingOptimizer"))
            {
                if (mb.enabled)
                {
                    mb.enabled = false;
#if UNITY_EDITOR
                    Debug.Log($"[AAA Optimizer] Disabled conflicting culling script: {typeName}");
#endif
                }
            }
        }
    }
    
    protected override void OnDestroy()
    {
        foreach (var caster in shadowCasters)
        {
            if (caster.renderer != null)
                caster.renderer.shadowCastingMode = caster.originalMode;
        }

        base.OnDestroy();
    }
    
    #endregion
}
