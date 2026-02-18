using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using System.Collections.Generic;

// Per-layer culling distances, light culling, and particle culling for HDRP.
// Skips rendering distant small objects, disables far lights, pauses far particles.
public class AdvancedCullingSystem : Singleton<AdvancedCullingSystem>
{
    [Header("Layer Culling")]
    [Tooltip("Max render distance per layer")]
    [SerializeField] private bool enableLayerCulling = true;

    [SerializeField] private float defaultCullDistance = 500f;
    [SerializeField] private float propsCullDistance = 150f;
    [SerializeField] private float detailCullDistance = 80f;
    [SerializeField] private float particleCullDistance = 100f;
    [SerializeField] private float decalCullDistance = 50f;

    [Header("Light Culling")]
    [Tooltip("Disable distant Point/Spot lights")]
    [SerializeField] private bool enableLightCulling = true;
    [SerializeField] private float lightCullDistance = 60f;
    [SerializeField] private float lightFadeStartDistance = 50f;

    [Header("Particle Culling")]
    [Tooltip("Stop distant particle systems")]
    [SerializeField] private bool enableParticleCulling = true;
    [SerializeField] private float particleStopDistance = 80f;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;
    [SerializeField] private KeyCode debugToggleKey = KeyCode.F5;

    private Camera mainCamera;
    private Transform cameraTransform;
    private List<CullableLight> cullableLights = new List<CullableLight>();
    private List<CullableParticle> cullableParticles = new List<CullableParticle>();
    private float lastUpdateTime;
    private float updateInterval = 0.1f; // 10Hz is fine for culling

    private int lightsActive;
    private int lightsCulled;
    private int particlesActive;
    private int particlesCulled;

    private class CullableLight
    {
        public Light light;
        public HDAdditionalLightData hdLight;
        public Transform transform;
        public float originalIntensity;
        public float originalRange;
        public bool wasEnabled;
    }

    private class CullableParticle
    {
        public ParticleSystem ps;
        public Transform transform;
        public bool wasPlaying;
    }

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
        mainCamera = Camera.main;
        if (mainCamera != null)
        {
            cameraTransform = mainCamera.transform;

            if (enableLayerCulling)
                SetupLayerCulling();
        }

        if (enableLightCulling)
            CollectLights();

        if (enableParticleCulling)
            CollectParticles();

        Debug.Log($"[AdvancedCulling] Initialized - Lights: {cullableLights.Count}, Particles: {cullableParticles.Count}");
    }

    private void Update()
    {
        if (Input.GetKeyDown(debugToggleKey))
            showDebugInfo = !showDebugInfo;

        if (Time.time - lastUpdateTime < updateInterval) return;
        lastUpdateTime = Time.time;

        if (cameraTransform == null)
        {
            mainCamera = Camera.main;
            cameraTransform = mainCamera?.transform;
            if (cameraTransform == null) return;
        }

        if (enableLightCulling)
            UpdateLightCulling();

        if (enableParticleCulling)
            UpdateParticleCulling();
    }

    private void SetupLayerCulling()
    {
        if (mainCamera == null) return;

        float[] distances = new float[32];

        for (int i = 0; i < 32; i++)
            distances[i] = defaultCullDistance;

        // layer names must match your project setup
        SetLayerDistance(distances, "Props", propsCullDistance);
        SetLayerDistance(distances, "SmallProps", propsCullDistance);
        SetLayerDistance(distances, "Detail", detailCullDistance);
        SetLayerDistance(distances, "Debris", detailCullDistance);
        SetLayerDistance(distances, "Particles", particleCullDistance);
        SetLayerDistance(distances, "VFX", particleCullDistance);
        SetLayerDistance(distances, "Decals", decalCullDistance);
        SetLayerDistance(distances, "Foliage", propsCullDistance);
        SetLayerDistance(distances, "Grass", detailCullDistance);

        mainCamera.layerCullDistances = distances;
        mainCamera.layerCullSpherical = true;

        Debug.Log("[AdvancedCulling] Layer culling distances set");
    }

    private void SetLayerDistance(float[] distances, string layerName, float distance)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer >= 0 && layer < 32)
            distances[layer] = distance;
    }

    private void CollectLights()
    {
        cullableLights.Clear();

        Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (var light in lights)
        {
            // directional lights are global so skip them
            if (light.type != LightType.Point && light.type != LightType.Spot)
                continue;

            var cullable = new CullableLight
            {
                light = light,
                hdLight = light.GetComponent<HDAdditionalLightData>(),
                transform = light.transform,
                originalIntensity = light.intensity,
                originalRange = light.range,
                wasEnabled = light.enabled
            };

            cullableLights.Add(cullable);
        }
    }

    private void UpdateLightCulling()
    {
        if (cameraTransform == null) return;

        Vector3 camPos = cameraTransform.position;
        float sqrCullDist = lightCullDistance * lightCullDistance;
        float sqrFadeDist = lightFadeStartDistance * lightFadeStartDistance;

        lightsActive = 0;
        lightsCulled = 0;

        foreach (var cullable in cullableLights)
        {
            if (cullable.light == null) continue;

            float sqrDist = (cullable.transform.position - camPos).sqrMagnitude;

            if (sqrDist > sqrCullDist)
            {
                if (cullable.light.enabled)
                    cullable.light.enabled = false;
                lightsCulled++;
            }
            else if (sqrDist > sqrFadeDist)
            {
                if (!cullable.light.enabled && cullable.wasEnabled)
                    cullable.light.enabled = true;

                float t = Mathf.InverseLerp(sqrCullDist, sqrFadeDist, sqrDist);
                cullable.light.intensity = cullable.originalIntensity * t;
                lightsActive++;
            }
            else
            {
                if (!cullable.light.enabled && cullable.wasEnabled)
                    cullable.light.enabled = true;

                if (cullable.light.intensity != cullable.originalIntensity)
                    cullable.light.intensity = cullable.originalIntensity;
                lightsActive++;
            }
        }
    }

    private void CollectParticles()
    {
        cullableParticles.Clear();

        ParticleSystem[] particles = FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (var ps in particles)
        {
            // skip child particle systems - only track root ones
            if (ps.transform.parent != null && ps.transform.parent.GetComponent<ParticleSystem>() != null)
                continue;

            cullableParticles.Add(new CullableParticle
            {
                ps = ps,
                transform = ps.transform,
                wasPlaying = ps.isPlaying
            });
        }
    }

    private void UpdateParticleCulling()
    {
        if (cameraTransform == null) return;

        Vector3 camPos = cameraTransform.position;
        float sqrDist = particleStopDistance * particleStopDistance;

        particlesActive = 0;
        particlesCulled = 0;

        foreach (var cullable in cullableParticles)
        {
            if (cullable.ps == null) continue;

            float dist = (cullable.transform.position - camPos).sqrMagnitude;

            if (dist > sqrDist)
            {
                // pause rather than stop so they resume correctly
                if (cullable.ps.isPlaying)
                    cullable.ps.Pause();
                particlesCulled++;
            }
            else
            {
                if (!cullable.ps.isPlaying && cullable.wasPlaying)
                    cullable.ps.Play();
                particlesActive++;
            }
        }
    }

    // call this after loading new scene content
    public void RefreshCullables()
    {
        Initialize();
    }

    public void SetLayerCullDistance(string layerName, float distance)
    {
        if (mainCamera == null) return;

        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0) return;

        float[] distances = mainCamera.layerCullDistances;
        distances[layer] = distance;
        mainCamera.layerCullDistances = distances;
    }

    private GUIStyle _debugStyle;

    private void OnGUI()
    {
        if (!showDebugInfo) return;

        if (_debugStyle == null)
        {
            _debugStyle = new GUIStyle(GUI.skin.box);
            _debugStyle.fontSize = 14;
            _debugStyle.normal.textColor = Color.white;
            _debugStyle.alignment = TextAnchor.UpperLeft;
            _debugStyle.padding = new RectOffset(10, 10, 10, 10);
            _debugStyle.richText = true;
        }

        string info = $"<b>ADVANCED CULLING (F5)</b>\n\n" +
                     $"<color=yellow>LIGHTS:</color>\n" +
                     $"  Active: {lightsActive}\n" +
                     $"  Culled: {lightsCulled}\n\n" +
                     $"<color=cyan>PARTICLES:</color>\n" +
                     $"  Active: {particlesActive}\n" +
                     $"  Paused: {particlesCulled}\n\n" +
                     $"<color=lime>LAYER CULLING:</color> {(enableLayerCulling ? "ON" : "OFF")}";

        GUI.Label(new Rect(320, 10, 250, 200), info, _debugStyle);
    }

    protected override void OnDestroy()
    {
        // restore lights before destroying
        foreach (var cullable in cullableLights)
        {
            if (cullable.light != null)
            {
                cullable.light.enabled = cullable.wasEnabled;
                cullable.light.intensity = cullable.originalIntensity;
            }
        }

        foreach (var cullable in cullableParticles)
        {
            if (cullable.ps != null && cullable.wasPlaying)
                cullable.ps.Play();
        }

        base.OnDestroy();
    }
}
