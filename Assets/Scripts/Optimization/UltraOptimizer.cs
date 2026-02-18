using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System.Collections.Generic;

// runtime optimizer - handles LOD crossfade, GPU instancing, distance shadow culling, reflection probes
// global quality settings only apply once at startup to avoid Blitter reinit errors
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
    [SerializeField] private float shadowDistance = 150f;
    [SerializeField] private float shadowCasterMaxDistance = 40f;

    [Header("GPU Instancing")]
    [SerializeField] private bool enableGPUInstancing = true;

    [Header("Lighting")]
    [SerializeField] private float reflectionProbeDistance = 50f;

    private Transform cameraTransform;
    private List<ShadowCaster> shadowCasters = new List<ShadowCaster>();
    private List<ReflectionProbe> reflectionProbes = new List<ReflectionProbe>();
    private float lastOptimizeTime;
    // static flag so settings only get applied once even across scene reloads
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
        // TODO: should probably cache this somewhere instead of looking it up every scene load
        cameraTransform = Camera.main?.transform;

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

        Debug.Log("[Optimizer] Systems active (settings: " + (settingsApplied ? "applied" : "skipped") + ")");
    }

    private void Update()
    {
        // run optimization every 100ms instead of every frame - no need to check shadows per-frame
        if (Time.time - lastOptimizeTime > 0.1f)
        {
            OptimizeShadowCasters();
            OptimizeReflectionProbes();
            lastOptimizeTime = Time.time;
        }
    }

    // applies safe quality settings - does NOT touch GraphicsSettings.useScriptableRenderPipelineBatching
    // because that triggers a Blitter reinit error in HDRP at runtime
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

        Debug.Log("[Optimizer] Quality settings applied.");
    }

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

        Debug.Log($"[Optimizer] {lodGroups.Length} LOD groups set to cross-fade.");
    }

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

        Debug.Log($"[Optimizer] GPU instancing enabled on {enabled} materials.");
    }

    private void CollectShadowCasters()
    {
        shadowCasters.Clear();

        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            if (renderer is ParticleSystemRenderer) continue;
            if (renderer.shadowCastingMode == ShadowCastingMode.Off) continue;

            // static objects use baked shadows, skip them
            if (renderer.gameObject.isStatic) continue;

            shadowCasters.Add(new ShadowCaster
            {
                renderer = renderer,
                transform = renderer.transform,
                originalMode = renderer.shadowCastingMode
            });
        }
    }

    // disables shadow casting on dynamic renderers past the max distance
    // the object stays visible, just stops casting shadows (big perf win)
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

            // FIXME: toggling probe.enabled every frame might be causing some reflection flickering at the boundary
            if (probe.enabled != shouldBeActive)
                probe.enabled = shouldBeActive;
        }
    }

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
                    Debug.Log($"[Optimizer] Disabled conflicting culling script: {typeName}");
#endif
                }
            }
        }
    }

    protected override void OnDestroy()
    {
        // restore shadow modes so we don't leave renderers in a bad state
        foreach (var caster in shadowCasters)
        {
            if (caster.renderer != null)
                caster.renderer.shadowCastingMode = caster.originalMode;
        }

        base.OnDestroy();
    }
}
