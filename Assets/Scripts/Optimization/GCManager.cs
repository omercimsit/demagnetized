using UnityEngine;
using System;

/// <summary>
/// GC (Garbage Collection) Manager - Controls when GC runs to prevent stutters.
/// Schedules GC during opportune moments (loading screens, pauses, etc.)
/// </summary>
public class GCManager : Singleton<GCManager>
{

    #region Settings
    
    [Header("GC Settings")]
    [Tooltip("Automatically collect garbage when memory exceeds this MB")]
    [SerializeField] private float autoCollectThresholdMB = 150f;
    
    [Tooltip("Minimum seconds between auto-collections")]
    [SerializeField] private float minCollectIntervalSeconds = 30f;
    
    [Tooltip("Collect when game is paused")]
    [SerializeField] private bool collectOnPause = true;
    
    [Tooltip("Collect during scene transitions")]
    [SerializeField] private bool collectOnSceneTransition = true;
    
    [Header("Incremental GC (Unity 2019.1+)")]
    [Tooltip("Enable incremental GC to spread collection across frames")]
    [SerializeField] private bool useIncrementalGC = true;
    
    [Tooltip("Time slice for incremental GC in nanoseconds")]
    [SerializeField] private long incrementalGCSliceNs = 3000000; // 3ms
    
    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;
    
    #endregion
    
    #region State
    
    private float _lastCollectTime;
    private float _lastMemoryMB;
    private bool _isCollecting;
    
    public float CurrentMemoryMB => (float)GC.GetTotalMemory(false) / (1024 * 1024);
    public float LastCollectionTime => _lastCollectTime;
    public bool IsCollecting => _isCollecting;
    
    #endregion
    
    #region Lifecycle
    
    protected override void OnAwake()
    {
        // Configure incremental GC if supported
        ConfigureIncrementalGC();
    }
    
    private void Start()
    {
        // Subscribe to events
        GameEvents.OnGamePaused += HandleGamePaused;
        GameEvents.OnSceneTransitionStart += HandleSceneTransition;
        
        _lastCollectTime = Time.realtimeSinceStartup;
        _lastMemoryMB = CurrentMemoryMB;
    }
    
    private void Update()
    {
        CheckAutoCollect();
    }
    
    protected override void OnDestroy()
    {
        GameEvents.OnGamePaused -= HandleGamePaused;
        GameEvents.OnSceneTransitionStart -= HandleSceneTransition;
        base.OnDestroy();
    }
    
    #endregion
    
    #region Public API
    
    /// <summary>
    /// Force immediate garbage collection
    /// </summary>
    public void CollectNow()
    {
        if (_isCollecting) return;
        
        _isCollecting = true;
        float beforeMB = CurrentMemoryMB;
        
        Log($"Starting GC... (Memory: {beforeMB:F1} MB)");
        
        // Full collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        float afterMB = CurrentMemoryMB;
        _lastCollectTime = Time.realtimeSinceStartup;
        _lastMemoryMB = afterMB;
        _isCollecting = false;
        
        Log($"GC Complete. Freed: {beforeMB - afterMB:F1} MB (Now: {afterMB:F1} MB)");
    }
    
    /// <summary>
    /// Schedule garbage collection for next opportune moment (e.g., loading screen)
    /// </summary>
    public void ScheduleCollection()
    {
        if (FrameBudgetManager.Instance != null)
        {
            FrameBudgetManager.Instance.Schedule(CollectNow, "GC.Collect", priority: 10, estimatedMs: 10f);
        }
        else
        {
            CollectNow();
        }
    }
    
    /// <summary>
    /// Collect garbage during loading screen (blocking but acceptable)
    /// </summary>
    public void CollectDuringLoad()
    {
        Log("Collecting during load...");
        System.GC.Collect(2, GCCollectionMode.Optimized, false);
    }
    
    /// <summary>
    /// Get GC generation stats
    /// </summary>
    public string GetStats()
    {
        return $"Memory: {CurrentMemoryMB:F1} MB | " +
               $"Gen0: {GC.CollectionCount(0)} | " +
               $"Gen1: {GC.CollectionCount(1)} | " +
               $"Gen2: {GC.CollectionCount(2)}";
    }
    
    /// <summary>
    /// Suppress GC for a short period (use carefully!)
    /// </summary>
    public void SuppressGC(float durationSeconds)
    {
        // Note: This requires Unity 2021+ for proper support
        // For older versions, we just try to avoid triggering GC
        Log($"GC suppression requested for {durationSeconds}s");
        
        #if UNITY_2021_1_OR_NEWER
        // Use GarbageCollector.GCMode if available
        // GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
        // Invoke("EnableGC", durationSeconds);
        #endif
    }
    
    #endregion
    
    #region Internal
    
    private void ConfigureIncrementalGC()
    {
        if (!useIncrementalGC) return;
        
        #if UNITY_2019_1_OR_NEWER
        // Check if incremental GC is enabled in Player Settings
        if (UnityEngine.Scripting.GarbageCollector.isIncremental)
        {
            // Set time slice
            UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds = (ulong)incrementalGCSliceNs;
            Log($"Incremental GC configured: {incrementalGCSliceNs / 1000000f:F1}ms slice");
        }
        else
        {
            Log("Incremental GC not enabled in Player Settings");
        }
        #endif
    }
    
    private void CheckAutoCollect()
    {
        float currentMemory = CurrentMemoryMB;
        float timeSinceLastCollect = Time.realtimeSinceStartup - _lastCollectTime;
        
        // Check if we should auto-collect
        if (currentMemory > autoCollectThresholdMB && 
            timeSinceLastCollect > minCollectIntervalSeconds)
        {
            Log($"Auto-collecting: Memory {currentMemory:F1}MB > threshold {autoCollectThresholdMB}MB");
            ScheduleCollection();
        }
    }
    
    private void HandleGamePaused(bool isPaused)
    {
        if (collectOnPause && isPaused)
        {
            // Good time to collect - player won't notice
            Log("Collecting on pause...");
            CollectNow();
        }
    }
    
    private void HandleSceneTransition(string sceneName)
    {
        if (collectOnSceneTransition)
        {
            Log($"Collecting before loading {sceneName}...");
            CollectDuringLoad();
        }
    }
    
    private void Log(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[GCManager] {message}");
        }
    }
    
    #endregion
}
