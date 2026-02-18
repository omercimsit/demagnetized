using UnityEngine;
using CloneSystem;

namespace Interaction.KineticTension
{
    // Connects the chain mechanic with the clone system.
    // Records chain state during recording, replays it during clone playback.
    // Also triggers the time-freeze visuals when the clone grabs the chain.
    //
    // Attach to the same GameObject as AAACloneSystem or ServiceLocator.
    // TODO: track chain interactions frame-by-frame, not just start/end snapshots
    public class KineticTensionBridge : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Reference to TensionController on player")]
        [SerializeField] private TensionController playerController;

        [Tooltip("Reference to TimeFreezeVisuals")]
        [SerializeField] private TimeFreezeVisuals freezeVisuals;

        [Tooltip("Optional explicit clone transform (avoids tag/name lookups)")]
        [SerializeField] private Transform cloneTransform;

        [Header("Settings")]
        [Tooltip("Trigger freeze visuals when clone holds chain")]
        [SerializeField] private bool enableFreezeVisuals = true;

        private AAACloneSystem _cloneSystem;
        private bool _isCloneHoldingChain;
        private ChainInteractable _cloneHeldChain;

        // state captured at end of recording
        private bool _wasHoldingChainDuringRecording;
        private ChainInteractable _chainHeldDuringRecording;
        private float _progressAtRecordingEnd;

        private void Start()
        {
            _cloneSystem = ServiceLocator.Instance != null
                ? ServiceLocator.Instance.CloneSystem
                : FindFirstObjectByType<AAACloneSystem>();

            if (_cloneSystem == null)
            {
                Debug.LogWarning("[KineticTensionBridge] CloneSystem not found!");
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

                // nothing found, make one on the fly
                if (freezeVisuals == null && enableFreezeVisuals)
                {
                    var freezeObj = new GameObject("TimeFreezeVisuals");
                    freezeVisuals = freezeObj.AddComponent<TimeFreezeVisuals>();
                }
            }

            SubscribeToCloneEvents();

            Debug.Log("[KineticTensionBridge] initialized");
        }

        private void Update()
        {
            // placeholder - could track per-frame chain state here if needed
            if (_cloneSystem != null)
                MonitorRecordingState();
        }

        private void OnDestroy()
        {
            UnsubscribeFromCloneEvents();
        }

        private void SubscribeToCloneEvents()
        {
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
            Debug.Log("[KineticTensionBridge] recording started");

            if (playerController != null && playerController.IsCurrentlyPulling)
                _chainHeldDuringRecording = playerController.CurrentChain;
        }

        private void OnRecordingStopped()
        {
            Debug.Log("[KineticTensionBridge] recording stopped");

            if (playerController != null && playerController.IsCurrentlyPulling)
            {
                _wasHoldingChainDuringRecording = true;
                _chainHeldDuringRecording = playerController.CurrentChain;

                if (_chainHeldDuringRecording != null)
                    _progressAtRecordingEnd = _chainHeldDuringRecording.CurrentProgress;
            }
            else
            {
                _wasHoldingChainDuringRecording = false;
            }
        }

        private void OnPlaybackStarted()
        {
            Debug.Log("[KineticTensionBridge] playback started");

            if (_wasHoldingChainDuringRecording && _chainHeldDuringRecording != null)
                StartCloneChainHold();
        }

        private void OnPlaybackEnded()
        {
            Debug.Log("[KineticTensionBridge] playback ended");
            EndCloneChainHold();
        }

        private void StartCloneChainHold()
        {
            if (_chainHeldDuringRecording == null) return;

            _isCloneHoldingChain = true;
            _cloneHeldChain = _chainHeldDuringRecording;

            _cloneHeldChain.CloneHold();

            if (enableFreezeVisuals && freezeVisuals != null)
            {
                var cloneTf = FindCloneTransform();
                if (cloneTf != null)
                    freezeVisuals.TriggerFreeze(cloneTf);
            }

            Debug.Log($"[KineticTensionBridge] clone holding chain at progress {_progressAtRecordingEnd:F2}");
        }

        private void EndCloneChainHold()
        {
            if (!_isCloneHoldingChain) return;

            _isCloneHoldingChain = false;

            if (_cloneHeldChain != null)
            {
                _cloneHeldChain.CloneRelease();
                _cloneHeldChain = null;
            }

            if (freezeVisuals != null)
                freezeVisuals.EndFreeze();
        }

        private Transform FindCloneTransform()
        {
            // use cached reference if we have one
            if (cloneTransform != null)
                return cloneTransform;

            // try tag first
            var clones = GameObject.FindGameObjectsWithTag("Clone");
            if (clones.Length > 0)
            {
                cloneTransform = clones[0].transform;
                return cloneTransform;
            }

            // fallback to name search
            var cloneObj = GameObject.Find("Clone");
            if (cloneObj != null)
            {
                cloneTransform = cloneObj.transform;
                return cloneTransform;
            }

            // FIXME: if clone isn't tagged or named "Clone" this silently fails
            return null;
        }

        private void MonitorRecordingState()
        {
            // intentionally empty for now - extend if per-frame tracking is needed
        }

        // can call from inspector button or test code
        public void DebugTriggerFreeze()
        {
            if (freezeVisuals != null)
                freezeVisuals.TriggerFreeze(transform);
        }

        public bool IsCloneHoldingChain => _isCloneHoldingChain;
        public ChainInteractable CloneHeldChain => _cloneHeldChain;
    }
}
