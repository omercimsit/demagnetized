using UnityEngine;
using System;

// Controls when GC runs so it doesn't stutter mid-gameplay.
// Hooks into pause and scene transition events to collect at safe moments.
public class GCManager : Singleton<GCManager>
{
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

    private float _lastCollectTime;
    private float _lastMemoryMB;
    private bool _isCollecting;

    public float CurrentMemoryMB => (float)GC.GetTotalMemory(false) / (1024 * 1024);
    public float LastCollectionTime => _lastCollectTime;
    public bool IsCollecting => _isCollecting;

    protected override void OnAwake()
    {
        ConfigureIncrementalGC();
    }

    private void Start()
    {
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

    // force a full blocking GC - only call when player won't notice
    public void CollectNow()
    {
        if (_isCollecting) return;

        _isCollecting = true;
        float beforeMB = CurrentMemoryMB;

        Log($"Starting GC... (Memory: {beforeMB:F1} MB)");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        float afterMB = CurrentMemoryMB;
        _lastCollectTime = Time.realtimeSinceStartup;
        _lastMemoryMB = afterMB;
        _isCollecting = false;

        Log($"GC Complete. Freed: {beforeMB - afterMB:F1} MB (Now: {afterMB:F1} MB)");
    }

    public void ScheduleCollection()
    {
        if (FrameBudgetManager.Instance != null)
            FrameBudgetManager.Instance.Schedule(CollectNow, "GC.Collect", priority: 10, estimatedMs: 10f);
        else
            CollectNow();
    }

    // non-blocking, good for loading screens
    public void CollectDuringLoad()
    {
        Log("Collecting during load...");
        System.GC.Collect(2, GCCollectionMode.Optimized, false);
    }

    public string GetStats()
    {
        return $"Memory: {CurrentMemoryMB:F1} MB | " +
               $"Gen0: {GC.CollectionCount(0)} | " +
               $"Gen1: {GC.CollectionCount(1)} | " +
               $"Gen2: {GC.CollectionCount(2)}";
    }

    // TODO: figure out if disabling GC entirely is worth it during intense combat sections
    public void SuppressGC(float durationSeconds)
    {
        Log($"GC suppression requested for {durationSeconds}s");

#if UNITY_2021_1_OR_NEWER
        // GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
        // Invoke("EnableGC", durationSeconds);
#endif
    }

    private void ConfigureIncrementalGC()
    {
        if (!useIncrementalGC) return;

#if UNITY_2019_1_OR_NEWER
        if (UnityEngine.Scripting.GarbageCollector.isIncremental)
        {
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
            // player is in menu, safe to block
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
            Debug.Log($"[GCManager] {message}");
    }
}
