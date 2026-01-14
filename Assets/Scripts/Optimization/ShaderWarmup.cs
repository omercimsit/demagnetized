using UnityEngine;
using UnityEngine.Rendering;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Shader Warmup System - Precompiles shaders to prevent runtime stuttering.
/// Call WarmupAll() during loading screens to eliminate shader compilation hitches.
/// </summary>
public class ShaderWarmup : Singleton<ShaderWarmup>
{

    #region Settings
    
    [Header("Warmup Settings")]
    [Tooltip("Automatically warmup on scene load")]
    [SerializeField] private bool warmupOnSceneLoad = true;
    
    [Tooltip("Frames to spread warmup across (higher = less spike, longer duration)")]
    [SerializeField] private int warmupFrameSpread = 10;
    
    [Tooltip("ShaderVariantCollection to warmup (optional)")]
    [SerializeField] private ShaderVariantCollection shaderVariants;
    
    [Header("Common Shaders to Warmup")]
    [SerializeField] private Shader[] additionalShaders;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;
    
    #endregion
    
    #region State
    
    private bool _isWarmedUp = false;
    private bool _isWarming = false;
    private float _warmupProgress = 0f;
    
    public bool IsWarmedUp => _isWarmedUp;
    public bool IsWarming => _isWarming;
    public float WarmupProgress => _warmupProgress;
    
    #endregion
    
    #region Lifecycle

    private void OnDisable()
    {
        StopAllCoroutines();
    }

    private void Start()
    {
        if (warmupOnSceneLoad)
        {
            WarmupAll();
        }
    }

    #endregion
    
    #region Public API
    
    /// <summary>
    /// Start shader warmup process
    /// </summary>
    public void WarmupAll()
    {
        if (_isWarming) return;
        StartCoroutine(WarmupCoroutine());
    }
    
    /// <summary>
    /// Warmup with progress callback
    /// </summary>
    public void WarmupAll(System.Action<float> onProgress, System.Action onComplete = null)
    {
        if (_isWarming) return;
        StartCoroutine(WarmupCoroutine(onProgress, onComplete));
    }
    
    /// <summary>
    /// Immediately warmup a specific shader
    /// </summary>
    public void WarmupShader(Shader shader)
    {
        if (shader == null) return;
        
        // Force shader compilation by creating a temporary material
        Material tempMat = new Material(shader);
        tempMat.SetPass(0);
        
        // Clean up
        DestroyImmediate(tempMat);
        
        Log($"Warmed up shader: {shader.name}");
    }
    
    #endregion
    
    #region Internal
    
    private IEnumerator WarmupCoroutine(System.Action<float> onProgress = null, System.Action onComplete = null)
    {
        _isWarming = true;
        _warmupProgress = 0f;
        
        Log("Starting shader warmup...");
        float startTime = Time.realtimeSinceStartup;
        
        // Step 1: Warmup ShaderVariantCollection (if assigned)
        if (shaderVariants != null)
        {
            Log($"Warming up ShaderVariantCollection: {shaderVariants.name}");
            shaderVariants.WarmUp();
            _warmupProgress = 0.3f;
            onProgress?.Invoke(_warmupProgress);
            yield return null;
        }
        
        // Step 2: Warmup additional shaders
        if (additionalShaders != null && additionalShaders.Length > 0)
        {
            int total = additionalShaders.Length;
            int perFrame = Mathf.Max(1, total / warmupFrameSpread);
            int processed = 0;
            
            for (int i = 0; i < total; i++)
            {
                if (additionalShaders[i] != null)
                {
                    WarmupShader(additionalShaders[i]);
                }
                
                processed++;
                _warmupProgress = 0.3f + (0.4f * processed / total);
                onProgress?.Invoke(_warmupProgress);
                
                // Spread across frames
                if (processed % perFrame == 0)
                {
                    yield return null;
                }
            }
        }
        
        // Step 3: Warmup common Unity/HDRP shaders
        yield return StartCoroutine(WarmupCommonShaders(onProgress));
        
        // Step 4: Force GC to clean up temporary materials
        System.GC.Collect();
        yield return null;
        
        _warmupProgress = 1f;
        _isWarmedUp = true;
        _isWarming = false;
        
        float elapsed = Time.realtimeSinceStartup - startTime;
        Log($"Shader warmup complete in {elapsed:F2}s");
        
        onProgress?.Invoke(1f);
        onComplete?.Invoke();
    }
    
    private IEnumerator WarmupCommonShaders(System.Action<float> onProgress)
    {
        // Common HDRP shader names
        string[] commonShaderNames = new string[]
        {
            "HDRP/Lit",
            "HDRP/LitTessellation",
            "HDRP/Unlit",
            "HDRP/Decal",
            "HDRP/LayeredLit",
            "Shader Graphs/",
            "Universal Render Pipeline/Lit"
        };
        
        int warmedup = 0;
        foreach (var shaderName in commonShaderNames)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader != null)
            {
                WarmupShader(shader);
                warmedup++;
            }
            
            _warmupProgress = 0.7f + (0.2f * warmedup / commonShaderNames.Length);
            onProgress?.Invoke(_warmupProgress);
            
            yield return null;
        }
        
        Log($"Warmed up {warmedup} common shaders");
    }
    
    private void Log(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[ShaderWarmup] {message}");
        }
    }
    
    #endregion
    
    #region Editor Helper
    
    #if UNITY_EDITOR
    [ContextMenu("Find All Project Shaders")]
    private void FindAllShaders()
    {
        var guids = UnityEditor.AssetDatabase.FindAssets("t:Shader");
        Debug.Log($"[ShaderWarmup] Found {guids.Length} shaders in project");
        
        List<Shader> shaders = new List<Shader>();
        foreach (var guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            Shader shader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader != null && !shader.name.StartsWith("Hidden/"))
            {
                shaders.Add(shader);
            }
        }
        
        additionalShaders = shaders.ToArray();
        Debug.Log($"[ShaderWarmup] Added {additionalShaders.Length} visible shaders to warmup list");
    }
    #endif
    
    #endregion
}
