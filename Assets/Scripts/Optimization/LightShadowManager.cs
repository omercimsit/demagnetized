using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Optimization
{
    /// <summary>
    /// Enforces single shadow-casting directional light in HDRP.
    /// HDRP only supports shadows from one directional light at a time.
    /// This picks the main sun and disables shadows on the rest.
    /// </summary>
    [ExecuteAlways]
    public class LightShadowManager : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Primary sun light - always casts shadows")]
        [SerializeField] private Light mainSunLight;
        
        [Tooltip("Auto-find directional lights in scene")]
        [SerializeField] private bool autoFindLights = true;
        
        [Tooltip("Check interval in seconds")]
        [SerializeField] private float checkInterval = 1f;
        
        [Header("Debug")]
        [SerializeField] private bool showLogs = true;
        
        private Light[] _allDirectionalLights;
        private float _lastCheckTime;
        private bool _initialized;
        
        private void OnEnable()
        {
            Initialize();
        }
        
        private void Start()
        {
            Initialize();
            EnforceSingleShadowLight();
        }
        
        private void Initialize()
        {
            if (_initialized) return;
            
            if (autoFindLights || mainSunLight == null)
            {
                FindAllDirectionalLights();
            }
            
            _initialized = true;
        }
        
        private void Update()
        {
            // Only check periodically to save performance
            if (Time.realtimeSinceStartup - _lastCheckTime < checkInterval) return;
            _lastCheckTime = Time.realtimeSinceStartup;
            
            EnforceSingleShadowLight();
        }
        
        private void FindAllDirectionalLights()
        {
            _allDirectionalLights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            
            // Find main sun (first active directional light with shadows)
            foreach (var light in _allDirectionalLights)
            {
                if (light.type == LightType.Directional && 
                    light.shadows != LightShadows.None &&
                    light.gameObject.activeInHierarchy)
                {
                    if (mainSunLight == null)
                    {
                        mainSunLight = light;
                        Log($"Auto-selected main sun: {light.name}");
                    }
                    break;
                }
            }
        }
        
        private void EnforceSingleShadowLight()
        {
            if (_allDirectionalLights == null || _allDirectionalLights.Length == 0)
                return;
            
            int shadowLightCount = 0;
            Light firstShadowLight = null;
            
            foreach (var light in _allDirectionalLights)
            {
                if (light == null || !light.gameObject.activeInHierarchy) continue;
                if (light.type != LightType.Directional) continue;
                
                bool isShadowCaster = light.shadows != LightShadows.None;
                
                if (isShadowCaster)
                {
                    shadowLightCount++;
                    
                    if (firstShadowLight == null)
                    {
                        firstShadowLight = light;
                    }
                    
                    // If this is not the main sun and we already have one, disable shadows
                    if (light != mainSunLight && shadowLightCount > 1)
                    {
                        DisableShadows(light);
                        Log($"Disabled shadows on: {light.name} (only 1 directional can cast shadows)");
                    }
                }
            }
            
            // If no main sun set, use first found
            if (mainSunLight == null && firstShadowLight != null)
            {
                mainSunLight = firstShadowLight;
            }
        }
        
        private void DisableShadows(Light light)
        {
            // Simply disable shadows via Unity's standard API
            light.shadows = LightShadows.None;
        }
        
        /// <summary>
        /// Force refresh all lights
        /// </summary>
        public void RefreshLights()
        {
            _initialized = false;
            _allDirectionalLights = null;
            Initialize();
            EnforceSingleShadowLight();
        }
        
        private void Log(string message)
        {
            if (showLogs)
                Debug.Log($"<color=yellow>[LightShadowManager]</color> {message}");
        }
        
        private void OnValidate()
        {
            if (Application.isPlaying) return;
            
            // Also run in editor
            Initialize();
            EnforceSingleShadowLight();
        }
    }
}
