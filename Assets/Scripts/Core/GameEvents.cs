using System;
using UnityEngine;

/// <summary>
/// Centralized Game Event Bus.
/// Provides a decoupled communication system between game components.
/// Eliminates tight coupling and FindObject calls.
/// </summary>
public static class GameEvents
{
    #region Game State Events
    
    /// <summary>
    /// Fired when game is paused or resumed.
    /// Parameter: true = paused, false = resumed
    /// </summary>
    public static event Action<bool> OnGamePaused;
    
    /// <summary>
    /// Fired when player dies
    /// </summary>
    public static event Action OnPlayerDied;
    
    /// <summary>
    /// Fired when player respawns
    /// </summary>
    public static event Action OnPlayerRespawned;
    
    /// <summary>
    /// Fired when a level is completed
    /// Parameter: level name
    /// </summary>
    public static event Action<string> OnLevelCompleted;
    
    #endregion
    
    #region Scene Events
    
    /// <summary>
    /// Fired when scene transition starts
    /// Parameter: target scene name
    /// </summary>
    public static event Action<string> OnSceneTransitionStart;
    
    /// <summary>
    /// Fired when scene transition completes
    /// Parameter: loaded scene name
    /// </summary>
    public static event Action<string> OnSceneTransitionComplete;
    
    /// <summary>
    /// Fired when scene loading progress updates
    /// Parameter: progress (0-1)
    /// </summary>
    public static event Action<float> OnSceneLoadProgress;
    
    #endregion
    
    #region Streaming Events
    
    /// <summary>
    /// Fired when a streaming zone is loaded
    /// Parameter: zone scene name
    /// </summary>
    public static event Action<string> OnStreamingZoneLoaded;
    
    /// <summary>
    /// Fired when a streaming zone is unloaded
    /// Parameter: zone scene name
    /// </summary>
    public static event Action<string> OnStreamingZoneUnloaded;
    
    #endregion
    
    #region Clone System Events
    
    /// <summary>
    /// Fired when clone system phase changes
    /// Parameter: new phase (as int: 0=Idle, 1=Recording, 2=Rewinding, 3=Review, 4=Playback)
    /// </summary>
    public static event Action<int> OnClonePhaseChanged;
    
    /// <summary>
    /// Fired when recording starts
    /// </summary>
    public static event Action OnRecordingStarted;
    
    /// <summary>
    /// Fired when recording stops
    /// </summary>
    public static event Action OnRecordingStopped;
    
    /// <summary>
    /// Fired when clone playback starts
    /// </summary>
    public static event Action OnPlaybackStarted;
    
    /// <summary>
    /// Fired when clone playback ends
    /// </summary>
    public static event Action OnPlaybackEnded;
    
    /// <summary>
    /// Fired when rewind sequence starts
    /// </summary>
    public static event Action OnRewindStarted;
    
    /// <summary>
    /// Fired when rewind sequence completes
    /// </summary>
    public static event Action OnRewindCompleted;
    
    #endregion
    
    #region Settings Events
    
    /// <summary>
    /// Fired when any setting changes
    /// </summary>
    public static event Action OnSettingsChanged;
    
    /// <summary>
    /// Fired when audio settings change
    /// </summary>
    public static event Action OnAudioSettingsChanged;
    
    /// <summary>
    /// Fired when graphics settings change
    /// </summary>
    public static event Action OnGraphicsSettingsChanged;
    
    /// <summary>
    /// Fired when DLSS mode changes
    /// Parameter: new mode index
    /// </summary>
    public static event Action<int> OnDLSSModeChanged;
    
    /// <summary>
    /// Fired when quality preset changes
    /// Parameter: new quality level index
    /// </summary>
    public static event Action<int> OnQualityChanged;
    
    #endregion
    
    #region UI Events
    
    /// <summary>
    /// Fired when pause menu opens
    /// </summary>
    public static event Action OnPauseMenuOpened;
    
    /// <summary>
    /// Fired when pause menu closes
    /// </summary>
    public static event Action OnPauseMenuClosed;
    
    /// <summary>
    /// Fired when settings panel opens
    /// </summary>
    public static event Action OnSettingsPanelOpened;
    
    /// <summary>
    /// Fired when settings panel closes
    /// </summary>
    public static event Action OnSettingsPanelClosed;
    
    #endregion
    
    #region Time Events
    
    /// <summary>
    /// Fired when time scale changes significantly
    /// Parameter: new time scale
    /// </summary>
    public static event Action<float> OnTimeScaleChanged;
    
    /// <summary>
    /// Fired when slow motion activates
    /// </summary>
    public static event Action OnSlowMotionStarted;
    
    /// <summary>
    /// Fired when slow motion deactivates
    /// </summary>
    public static event Action OnSlowMotionEnded;
    
    #endregion
    
    #region Event Invocation Methods
    
    // Game State
    public static void InvokeGamePaused(bool isPaused)
    {
        try { OnGamePaused?.Invoke(isPaused); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnGamePaused error: {e.Message}"); }
    }
    
    public static void InvokePlayerDied()
    {
        try { OnPlayerDied?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnPlayerDied error: {e.Message}"); }
    }
    
    public static void InvokePlayerRespawned()
    {
        try { OnPlayerRespawned?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnPlayerRespawned error: {e.Message}"); }
    }
    
    public static void InvokeLevelCompleted(string levelName)
    {
        try { OnLevelCompleted?.Invoke(levelName); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnLevelCompleted error: {e.Message}"); }
    }
    
    // Scene
    public static void InvokeSceneTransitionStart(string sceneName)
    {
        try { OnSceneTransitionStart?.Invoke(sceneName); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnSceneTransitionStart error: {e.Message}"); }
    }
    
    public static void InvokeSceneTransitionComplete(string sceneName)
    {
        try { OnSceneTransitionComplete?.Invoke(sceneName); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnSceneTransitionComplete error: {e.Message}"); }
    }
    
    public static void InvokeSceneLoadProgress(float progress)
    {
        try { OnSceneLoadProgress?.Invoke(progress); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnSceneLoadProgress error: {e.Message}"); }
    }
    
    // Streaming Zones
    public static void InvokeStreamingZoneLoaded(string zoneName)
    {
        try { OnStreamingZoneLoaded?.Invoke(zoneName); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnStreamingZoneLoaded error: {e.Message}"); }
    }
    
    public static void InvokeStreamingZoneUnloaded(string zoneName)
    {
        try { OnStreamingZoneUnloaded?.Invoke(zoneName); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnStreamingZoneUnloaded error: {e.Message}"); }
    }
    
    // Clone System
    public static void InvokeClonePhaseChanged(int phase)
    {
        try { OnClonePhaseChanged?.Invoke(phase); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnClonePhaseChanged error: {e.Message}"); }
    }
    
    public static void InvokeRecordingStarted()
    {
        try { OnRecordingStarted?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnRecordingStarted error: {e.Message}"); }
    }
    
    public static void InvokeRecordingStopped()
    {
        try { OnRecordingStopped?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnRecordingStopped error: {e.Message}"); }
    }
    
    public static void InvokePlaybackStarted()
    {
        try { OnPlaybackStarted?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnPlaybackStarted error: {e.Message}"); }
    }
    
    public static void InvokePlaybackEnded()
    {
        try { OnPlaybackEnded?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnPlaybackEnded error: {e.Message}"); }
    }
    
    public static void InvokeRewindStarted()
    {
        try { OnRewindStarted?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnRewindStarted error: {e.Message}"); }
    }
    
    public static void InvokeRewindCompleted()
    {
        try { OnRewindCompleted?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnRewindCompleted error: {e.Message}"); }
    }
    
    // Settings
    public static void InvokeSettingsChanged()
    {
        try { OnSettingsChanged?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnSettingsChanged error: {e.Message}"); }
    }
    
    public static void InvokeAudioSettingsChanged()
    {
        try { OnAudioSettingsChanged?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnAudioSettingsChanged error: {e.Message}"); }
    }
    
    public static void InvokeGraphicsSettingsChanged()
    {
        try { OnGraphicsSettingsChanged?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnGraphicsSettingsChanged error: {e.Message}"); }
    }
    
    public static void InvokeDLSSModeChanged(int mode)
    {
        try { OnDLSSModeChanged?.Invoke(mode); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnDLSSModeChanged error: {e.Message}"); }
    }
    
    public static void InvokeQualityChanged(int level)
    {
        try { OnQualityChanged?.Invoke(level); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnQualityChanged error: {e.Message}"); }
    }
    
    // UI
    public static void InvokePauseMenuOpened()
    {
        try { OnPauseMenuOpened?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnPauseMenuOpened error: {e.Message}"); }
    }
    
    public static void InvokePauseMenuClosed()
    {
        try { OnPauseMenuClosed?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnPauseMenuClosed error: {e.Message}"); }
    }
    
    public static void InvokeSettingsPanelOpened()
    {
        try { OnSettingsPanelOpened?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnSettingsPanelOpened error: {e.Message}"); }
    }
    
    public static void InvokeSettingsPanelClosed()
    {
        try { OnSettingsPanelClosed?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnSettingsPanelClosed error: {e.Message}"); }
    }
    
    // Time
    public static void InvokeTimeScaleChanged(float newScale)
    {
        try { OnTimeScaleChanged?.Invoke(newScale); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnTimeScaleChanged error: {e.Message}"); }
    }
    
    public static void InvokeSlowMotionStarted()
    {
        try { OnSlowMotionStarted?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnSlowMotionStarted error: {e.Message}"); }
    }
    
    public static void InvokeSlowMotionEnded()
    {
        try { OnSlowMotionEnded?.Invoke(); }
        catch (Exception e) { Debug.LogError($"[GameEvents] OnSlowMotionEnded error: {e.Message}"); }
    }
    
    #endregion
    
    #region Utility
    
    /// <summary>
    /// Clear all event subscriptions. Use with caution - typically only on application quit.
    /// </summary>
    public static void ClearAllEvents()
    {
        OnGamePaused = null;
        OnPlayerDied = null;
        OnPlayerRespawned = null;
        OnLevelCompleted = null;
        
        OnSceneTransitionStart = null;
        OnSceneTransitionComplete = null;
        OnSceneLoadProgress = null;
        
        OnClonePhaseChanged = null;
        OnRecordingStarted = null;
        OnRecordingStopped = null;
        OnPlaybackStarted = null;
        OnPlaybackEnded = null;
        OnRewindStarted = null;
        OnRewindCompleted = null;
        
        OnSettingsChanged = null;
        OnAudioSettingsChanged = null;
        OnGraphicsSettingsChanged = null;
        OnDLSSModeChanged = null;
        OnQualityChanged = null;
        
        OnPauseMenuOpened = null;
        OnPauseMenuClosed = null;
        OnSettingsPanelOpened = null;
        OnSettingsPanelClosed = null;
        
        OnTimeScaleChanged = null;
        OnSlowMotionStarted = null;
        OnSlowMotionEnded = null;
        
        Debug.Log("[GameEvents] All events cleared");
    }
    
    #endregion
}

/// <summary>
/// Helper component to auto-unsubscribe from events on destroy.
/// Inherit from this instead of MonoBehaviour for event-listening components.
/// </summary>
public abstract class EventListener : MonoBehaviour
{
    protected virtual void OnEnable()
    {
        SubscribeToEvents();
    }
    
    protected virtual void OnDisable()
    {
        UnsubscribeFromEvents();
    }
    
    /// <summary>
    /// Override to subscribe to GameEvents
    /// </summary>
    protected abstract void SubscribeToEvents();
    
    /// <summary>
    /// Override to unsubscribe from GameEvents
    /// </summary>
    protected abstract void UnsubscribeFromEvents();
}
