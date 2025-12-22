using UnityEngine;
using CloneSystem;

namespace Interaction.KineticTension
{
    /// <summary>
    /// KINETIC TENSION BRIDGE - AAACloneSystem Integration
    /// 
    /// Connects the Kinetic Tension mechanic with the clone system:
    /// - Records chain interaction state during recording
    /// - Replays chain interaction during playback
    /// - Triggers time-freeze visuals when SPACE pressed
    /// 
    /// Attach to same GameObject as AAACloneSystem or ServiceLocator
    /// </summary>
    public class KineticTensionBridge : MonoBehaviour
    {
        #region Configuration
        
        [Header("=== REFERENCES ===")]
        [Tooltip("Reference to TensionController on player")]
        [SerializeField] private TensionController playerController;
        
        [Tooltip("Reference to TimeFreezeVisuals")]
        [SerializeField] private TimeFreezeVisuals freezeVisuals;

        [Tooltip("Optional explicit clone transform (avoids tag/name lookups)")]
        [SerializeField] private Transform cloneTransform;
        
        [Header("=== SETTINGS ===")]
        [Tooltip("Trigger freeze visuals when clone holds chain")]
        [SerializeField] private bool enableFreezeVisuals = true;
        
        #endregion
        
        #region Runtime State
        
        private AAACloneSystem _cloneSystem;
        private bool _isCloneHoldingChain;
        private ChainInteractable _cloneHeldChain;
        
        // Recording data
        private bool _wasHoldingChainDuringRecording;
        private ChainInteractable _chainHeldDuringRecording;
        private float _progressAtRecordingEnd;
        
        #endregion
        
        #region Lifecycle
        
        private void Start()
        {
            // Find references
            _cloneSystem = ServiceLocator.Instance != null ? ServiceLocator.Instance.CloneSystem : FindFirstObjectByType<AAACloneSystem>();
            
            if (_cloneSystem == null)
            {
                Debug.LogWarning("[KineticTensionBridge] AAACloneSystem not found!");
                return;
            }
            
            if (playerController == null)
            {
                playerController = ServiceLocator.Instance != null
                    ? ServiceLocator.Instance.Resolve<TensionController>()
                    : FindFirstObjectByType<TensionController>();
            }
            
            if (freezeVisuals == null)
            {
                freezeVisuals = ServiceLocator.Instance != null
                    ? ServiceLocator.Instance.Resolve<TimeFreezeVisuals>()
                    : FindFirstObjectByType<TimeFreezeVisuals>();
                if (freezeVisuals == null && enableFreezeVisuals)
                {
                    // Create one
                    var freezeObj = new GameObject("TimeFreezeVisuals");
                    freezeVisuals = freezeObj.AddComponent<TimeFreezeVisuals>();
                }
            }
            
            // Subscribe to clone events
            SubscribeToCloneEvents();
            
            Debug.Log("[KineticTensionBridge] Initialized");
        }
        
        private void Update()
        {
            // Monitor recording state
            if (_cloneSystem != null)
            {
                MonitorRecordingState();
            }
        }
        
        private void OnDestroy()
        {
            UnsubscribeFromCloneEvents();
        }
        
        #endregion
        
        #region Clone Event Integration
        
        private void SubscribeToCloneEvents()
        {
            // Note: AAACloneSystem uses GameEvents, we can hook into those
            GameEvents.OnRecordingStarted += OnRecordingStarted;
            GameEvents.OnRecordingStopped += OnRecordingStopped;
            GameEvents.OnPlaybackStarted += OnPlaybackStarted;
            GameEvents.OnPlaybackEnded += OnPlaybackEnded;
        }
        
        private void UnsubscribeFromCloneEvents()
        {
            GameEvents.OnRecordingStarted -= OnRecordingStarted;
            GameEvents.OnRecordingStopped -= OnRecordingStopped;
            GameEvents.OnPlaybackStarted -= OnPlaybackStarted;
            GameEvents.OnPlaybackEnded -= OnPlaybackEnded;
        }
        
        private void OnRecordingStarted()
        {
            Debug.Log("[KineticTensionBridge] Recording started");
            
            // Track if player is holding chain at start
            if (playerController != null && playerController.IsCurrentlyPulling)
            {
                _chainHeldDuringRecording = playerController.CurrentChain;
            }
        }
        
        private void OnRecordingStopped()
        {
            Debug.Log("[KineticTensionBridge] Recording stopped");
            
            // Capture final state
            if (playerController != null && playerController.IsCurrentlyPulling)
            {
                _wasHoldingChainDuringRecording = true;
                _chainHeldDuringRecording = playerController.CurrentChain;
                
                if (_chainHeldDuringRecording != null)
                {
                    _progressAtRecordingEnd = _chainHeldDuringRecording.CurrentProgress;
                }
            }
            else
            {
                _wasHoldingChainDuringRecording = false;
            }
        }
        
        private void OnPlaybackStarted()
        {
            Debug.Log("[KineticTensionBridge] Playback started");
            
            // If recording included chain interaction, trigger clone hold
            if (_wasHoldingChainDuringRecording && _chainHeldDuringRecording != null)
            {
                StartCloneChainHold();
            }
        }
        
        private void OnPlaybackEnded()
        {
            Debug.Log("[KineticTensionBridge] Playback ended");
            
            // Release clone's chain hold
            EndCloneChainHold();
        }
        
        #endregion
        
        #region Chain Holding Logic
        
        private void StartCloneChainHold()
        {
            if (_chainHeldDuringRecording == null) return;
            
            _isCloneHoldingChain = true;
            _cloneHeldChain = _chainHeldDuringRecording;
            
            // Tell chain that clone is holding it
            _cloneHeldChain.CloneHold();
            
            // Trigger freeze visuals
            if (enableFreezeVisuals && freezeVisuals != null)
            {
                // Find clone transform
                var cloneTransform = FindCloneTransform();
                if (cloneTransform != null)
                {
                    freezeVisuals.TriggerFreeze(cloneTransform);
                }
            }
            
            Debug.Log($"[KineticTensionBridge] Clone holding chain at {_progressAtRecordingEnd:F2} progress");
        }
        
        private void EndCloneChainHold()
        {
            if (!_isCloneHoldingChain) return;
            
            _isCloneHoldingChain = false;
            
            // Release chain
            if (_cloneHeldChain != null)
            {
                _cloneHeldChain.CloneRelease();
                _cloneHeldChain = null;
            }
            
            // End freeze visuals
            if (freezeVisuals != null)
            {
                freezeVisuals.EndFreeze();
            }
        }
        
        private Transform FindCloneTransform()
        {
            if (cloneTransform != null)
                return cloneTransform;

            // Find active clone in scene
            // Look for objects with "Clone" in name that are active
            var clones = GameObject.FindGameObjectsWithTag("Clone");
            if (clones.Length > 0)
            {
                cloneTransform = clones[0].transform;
                return cloneTransform;
            }
            
            // Fallback: search by name
            var cloneObj = GameObject.Find("Clone");
            if (cloneObj != null)
            {
                cloneTransform = cloneObj.transform;
                return cloneTransform;
            }
            
            // Last resort: use recorded start position
            return null;
        }
        
        #endregion
        
        #region Recording State Monitor
        
        private void MonitorRecordingState()
        {
            // This could be used to track chain interactions frame-by-frame
            // For now, we just capture start/end states
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Force trigger time freeze visuals (for testing)
        /// </summary>
        public void DebugTriggerFreeze()
        {
            if (freezeVisuals != null)
            {
                freezeVisuals.TriggerFreeze(transform);
            }
        }
        
        /// <summary>
        /// Check if clone is currently holding a chain
        /// </summary>
        public bool IsCloneHoldingChain => _isCloneHoldingChain;
        
        /// <summary>
        /// Get the chain being held by clone
        /// </summary>
        public ChainInteractable CloneHeldChain => _cloneHeldChain;
        
        #endregion
    }
}
