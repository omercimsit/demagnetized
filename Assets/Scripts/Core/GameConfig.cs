using UnityEngine;

// ScriptableObject for all the config values - beats having magic numbers everywhere
// Create via: Assets > Create > Game > Config
[CreateAssetMenu(fileName = "GameConfig", menuName = "Game/Config", order = 0)]
public class GameConfig : ScriptableObject
{
    private static GameConfig _instance;

    // loads from Resources/GameConfig, creates empty one if missing
    public static GameConfig Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = Resources.Load<GameConfig>("GameConfig");
                if (_instance == null)
                {
                    Debug.LogWarning("[GameConfig] No GameConfig found in Resources. Using defaults.");
                    _instance = CreateInstance<GameConfig>();
                }
            }
            return _instance;
        }
    }

    [Header("Clone Recording")]
    [Tooltip("Maximum duration for recording player movements (seconds)")]
    [Range(1f, 30f)]
    public float maxRecordDuration = 5f;

    [Tooltip("Base recording interval (lower = smoother, higher memory)")]
    [Range(0.01f, 0.1f)]
    public float baseRecordInterval = 0.02f;

    [Tooltip("High precision mode interval (~60fps)")]
    public float highPrecisionInterval = 0.0166f;

    [Tooltip("Reverse playback speed multiplier")]
    [Range(1f, 10f)]
    public float reversePlaybackSpeed = 3f;

    [Header("Clone Playback")]
    [Tooltip("Clone transparency (0 = invisible, 1 = opaque)")]
    [Range(0f, 1f)]
    public float cloneTransparency = 0.6f;

    [Tooltip("Loop the clone playback")]
    public bool loopPlayback = false;

    [Tooltip("Delay between loop iterations")]
    [Range(0f, 2f)]
    public float loopResetDelay = 0.2f;

    [Tooltip("Use bone-level playback for higher fidelity")]
    public bool useBonePlayback = true;

    [Header("Delta Compression")]
    [Tooltip("Enable delta compression to reduce memory usage")]
    public bool useDeltaCompression = true;

    [Tooltip("Minimum position change to record a new frame")]
    [Range(0.001f, 0.1f)]
    public float positionThreshold = 0.01f;

    [Tooltip("Minimum rotation angle change to record a new frame")]
    [Range(0.1f, 5f)]
    public float rotationThreshold = 0.5f;

    [Tooltip("Force a keyframe every N seconds")]
    [Range(0.05f, 0.5f)]
    public float maxFrameInterval = 0.1f;

    [Header("Time Management")]
    [Tooltip("Slow motion time scale during recording")]
    [Range(0.01f, 1f)]
    public float slowMotionScale = 0.1f;

    [Tooltip("Normal time scale")]
    [Range(0.1f, 2f)]
    public float normalTimeScale = 1f;

    [Tooltip("Speed of time scale transitions")]
    [Range(0.5f, 20f)]
    public float timeTransitionSpeed = 2.5f;

    [Header("Scene Names")]
    [Tooltip("Scenes that should skip pause menu")]
    public string[] scenesToIgnorePause = { "Menu", "Main", "Loading" };

    [Tooltip("Main menu scene name")]
    public string mainMenuSceneName = "MainMenu";

    [Header("Clone Component Removal")]
    [Tooltip("Component name patterns to remove from clones")]
    public string[] componentsToRemove = {
        "Player",
        "Controller",
        "Input",
        "Time",
        "Movement",
        "Motion"
    };

    [Header("Audio Resources")]
    [Tooltip("Pause menu music clip name in Resources")]
    public string pauseMenuMusicPath = "Analog Corridor of Teeth";

    [Header("Interaction System")]
    [Tooltip("Raycast check interval for interaction (seconds)")]
    [Range(0.01f, 0.2f)]
    public float interactionCheckInterval = 0.05f;

    [Tooltip("Default interaction range")]
    [Range(1f, 10f)]
    public float defaultInteractionRange = 3f;

    private void OnValidate()
    {
        maxRecordDuration = Mathf.Clamp(maxRecordDuration, 1f, 30f);
        baseRecordInterval = Mathf.Clamp(baseRecordInterval, 0.01f, 0.1f);
        highPrecisionInterval = Mathf.Clamp(highPrecisionInterval, 0.01f, 0.05f);
        reversePlaybackSpeed = Mathf.Clamp(reversePlaybackSpeed, 1f, 10f);
        cloneTransparency = Mathf.Clamp01(cloneTransparency);
        slowMotionScale = Mathf.Clamp(slowMotionScale, 0.01f, 1f);
        normalTimeScale = Mathf.Clamp(normalTimeScale, 0.1f, 2f);
    }

    public bool ShouldIgnorePause(string sceneName)
    {
        if (scenesToIgnorePause == null) return false;

        foreach (var pattern in scenesToIgnorePause)
        {
            if (sceneName.Contains(pattern))
                return true;
        }
        return false;
    }

    // TODO: clean this up later, string matching is kinda fragile
    public bool ShouldRemoveComponent(string componentTypeName)
    {
        if (componentsToRemove == null) return false;

        foreach (var pattern in componentsToRemove)
        {
            if (componentTypeName.Contains(pattern))
                return true;
        }
        return false;
    }
}
