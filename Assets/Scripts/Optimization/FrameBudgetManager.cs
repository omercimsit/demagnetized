using UnityEngine;
using System;
using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// Frame Budget Manager - Distributes heavy work across multiple frames.
/// Prevents frame spikes by scheduling work within a time budget per frame.
/// Use for: asset loading, spawning, pathfinding, etc.
/// </summary>
public class FrameBudgetManager : Singleton<FrameBudgetManager>
{

    #region Settings
    
    [Header("Budget Settings")]
    [Tooltip("Maximum milliseconds to spend on scheduled work per frame")]
    [SerializeField] private float frameBudgetMs = 4f; // ~4ms leaves room for other work at 60fps
    
    [Tooltip("Minimum yield time between heavy operations (ms)")]
    [SerializeField] private float minYieldTimeMs = 0.5f;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;
    
    #endregion
    
    #region State
    
    private Queue<ScheduledWork> _workQueue = new Queue<ScheduledWork>();
    private Stopwatch _frameTimer = new Stopwatch();
    private float _usedBudgetMs = 0f;
    
    // Stats
    private int _completedThisFrame = 0;
    private int _totalScheduled = 0;
    private int _totalCompleted = 0;
    
    public int PendingWorkCount => _workQueue.Count;
    public float UsedBudgetMs => _usedBudgetMs;
    public bool HasBudgetRemaining => _usedBudgetMs < frameBudgetMs;
    
    #endregion
    
    #region Work Item Definition
    
    private class ScheduledWork
    {
        public Action Work;
        public string Name;
        public int Priority; // Lower = higher priority
        public float EstimatedMs;
    }
    
    #endregion
    
    #region Lifecycle
    
    private void LateUpdate()
    {
        ProcessWorkQueue();
    }
    
    #endregion
    
    #region Public API
    
    /// <summary>
    /// Schedule work to be executed within frame budget
    /// </summary>
    public void Schedule(Action work, string name = "Work", int priority = 5, float estimatedMs = 1f)
    {
        if (work == null) return;
        
        _workQueue.Enqueue(new ScheduledWork
        {
            Work = work,
            Name = name,
            Priority = priority,
            EstimatedMs = estimatedMs
        });
        
        _totalScheduled++;
        Log($"Scheduled: {name} (Queue: {_workQueue.Count})");
    }
    
    /// <summary>
    /// Schedule multiple work items
    /// </summary>
    public void ScheduleBatch<T>(IEnumerable<T> items, Action<T> workPerItem, string batchName = "Batch")
    {
        int count = 0;
        foreach (var item in items)
        {
            var capturedItem = item;
            Schedule(() => workPerItem(capturedItem), $"{batchName}[{count}]");
            count++;
        }
        
        Log($"Scheduled batch '{batchName}' with {count} items");
    }
    
    /// <summary>
    /// Execute work immediately if budget allows, otherwise schedule
    /// </summary>
    public void ExecuteOrSchedule(Action work, string name = "Work", float estimatedMs = 1f)
    {
        if (HasBudgetRemaining && estimatedMs < (frameBudgetMs - _usedBudgetMs))
        {
            // Execute now
            ExecuteWithTracking(work, name);
        }
        else
        {
            // Schedule for later
            Schedule(work, name, estimatedMs: estimatedMs);
        }
    }
    
    /// <summary>
    /// Clear all pending work
    /// </summary>
    public void ClearQueue()
    {
        int cleared = _workQueue.Count;
        _workQueue.Clear();
        Log($"Cleared {cleared} pending work items");
    }
    
    /// <summary>
    /// Force process all remaining work immediately (blocking)
    /// </summary>
    public void FlushAll()
    {
        Log($"Flushing {_workQueue.Count} work items...");
        
        while (_workQueue.Count > 0)
        {
            var work = _workQueue.Dequeue();
            try
            {
                work.Work?.Invoke();
                _totalCompleted++;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[FrameBudget] Error in work '{work.Name}': {e.Message}");
            }
        }
    }
    
    /// <summary>
    /// Get debug stats
    /// </summary>
    public string GetStats()
    {
        return $"Pending: {_workQueue.Count}, Last Frame: {_completedThisFrame}, " +
               $"Budget Used: {_usedBudgetMs:F2}/{frameBudgetMs}ms, " +
               $"Total: {_totalCompleted}/{_totalScheduled}";
    }
    
    #endregion
    
    #region Internal
    
    private void ProcessWorkQueue()
    {
        _frameTimer.Restart();
        _usedBudgetMs = 0f;
        _completedThisFrame = 0;
        
        while (_workQueue.Count > 0 && _usedBudgetMs < frameBudgetMs)
        {
            var work = _workQueue.Peek();
            
            // Check if we have enough budget for this work
            if (work.EstimatedMs > 0 && _usedBudgetMs + work.EstimatedMs > frameBudgetMs)
            {
                // Not enough budget, wait until next frame
                break;
            }
            
            _workQueue.Dequeue();
            ExecuteWithTracking(work.Work, work.Name);
            
            _completedThisFrame++;
            _totalCompleted++;
            
            // Ensure we yield occasionally
            if (_frameTimer.Elapsed.TotalMilliseconds >= minYieldTimeMs)
            {
                _usedBudgetMs = (float)_frameTimer.Elapsed.TotalMilliseconds;
            }
        }
        
        _usedBudgetMs = (float)_frameTimer.Elapsed.TotalMilliseconds;
        _frameTimer.Stop();
    }
    
    private void ExecuteWithTracking(Action work, string name)
    {
        _frameTimer.Stop();
        float startMs = (float)_frameTimer.Elapsed.TotalMilliseconds;
        _frameTimer.Start();
        
        try
        {
            work?.Invoke();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[FrameBudget] Error in work '{name}': {e.Message}");
        }
        
        float elapsedMs = (float)_frameTimer.Elapsed.TotalMilliseconds - startMs;
        
        if (showDebugLogs && elapsedMs > 2f)
        {
            UnityEngine.Debug.Log($"[FrameBudget] '{name}' took {elapsedMs:F2}ms");
        }
    }
    
    private void Log(string message)
    {
        if (showDebugLogs)
        {
            UnityEngine.Debug.Log($"[FrameBudget] {message}");
        }
    }
    
    #endregion
}

/// <summary>
/// Extension methods for easier frame budget usage
/// </summary>
public static class FrameBudgetExtensions
{
    /// <summary>
    /// Schedule GameObject instantiation within frame budget
    /// </summary>
    public static void ScheduleInstantiate(this FrameBudgetManager fbm, GameObject prefab, Vector3 position, Quaternion rotation, Action<GameObject> onComplete = null)
    {
        fbm.Schedule(() =>
        {
            var instance = UnityEngine.Object.Instantiate(prefab, position, rotation);
            onComplete?.Invoke(instance);
        }, $"Instantiate({prefab.name})", estimatedMs: 2f);
    }
    
    /// <summary>
    /// Schedule component search within frame budget
    /// </summary>
    public static void ScheduleFind<T>(this FrameBudgetManager fbm, Action<T> onFound) where T : UnityEngine.Object
    {
        fbm.Schedule(() =>
        {
            var found = UnityEngine.Object.FindFirstObjectByType<T>();
            if (found != null) onFound?.Invoke(found);
        }, $"Find({typeof(T).Name})", estimatedMs: 1f);
    }
}
