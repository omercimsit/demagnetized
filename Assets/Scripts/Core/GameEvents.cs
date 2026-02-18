using System;
using UnityEngine;

// event bus so systems don't need direct references to each other
// using static events here - not ideal but way simpler than a full observer pattern
// TODO: consider switching to ScriptableObject-based events if this gets hard to debug
public static class GameEvents
{
    // game state
    // bool param: true = paused, false = resumed
    public static event Action<bool> OnGamePaused;
    public static event Action OnPlayerDied;
    public static event Action OnPlayerRespawned;
    public static event Action<string> OnLevelCompleted;

    // scene loading
    public static event Action<string> OnSceneTransitionStart;
    public static event Action<string> OnSceneTransitionComplete;
    public static event Action<float> OnSceneLoadProgress;

    // streaming zones
    public static event Action<string> OnStreamingZoneLoaded;
    public static event Action<string> OnStreamingZoneUnloaded;

    // clone system - phase int: 0=Idle, 1=Recording, 2=Rewinding, 3=Review, 4=Playback
    // FIXME: using raw ints for phase is messy, should probably pass the enum directly
    public static event Action<int> OnClonePhaseChanged;
    public static event Action OnRecordingStarted;
    public static event Action OnRecordingStopped;
    public static event Action OnPlaybackStarted;
    public static event Action OnPlaybackEnded;
    public static event Action OnRewindStarted;
    public static event Action OnRewindCompleted;

    // settings
    public static event Action OnSettingsChanged;
    public static event Action OnAudioSettingsChanged;
    public static event Action OnGraphicsSettingsChanged;
    public static event Action<int> OnDLSSModeChanged;
    public static event Action<int> OnQualityChanged;

    // ui
    public static event Action OnPauseMenuOpened;
    public static event Action OnPauseMenuClosed;
    public static event Action OnSettingsPanelOpened;
    public static event Action OnSettingsPanelClosed;

    // time / slow motion
    public static event Action<float> OnTimeScaleChanged;
    public static event Action OnSlowMotionStarted;
    public static event Action OnSlowMotionEnded;


    // --- invoke methods below ---
    // wrapping in try/catch so one bad subscriber doesn't break everything
    // not sure if this is the best approach but it keeps things stable during testing

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

    // nukes all subscriptions - only call this on quit or full scene teardown
    // TODO: breaks if scene reloads and old subscribers haven't cleaned up yet
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
}

// base class for anything that needs to listen to GameEvents
// handles the subscribe/unsubscribe boilerplate automatically via OnEnable/OnDisable
// FIXME: if a child class forgets to call base.OnEnable it will silently not subscribe
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

    protected abstract void SubscribeToEvents();
    protected abstract void UnsubscribeFromEvents();
}
