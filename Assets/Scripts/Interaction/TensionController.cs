using UnityEngine;
using UnityEngine.InputSystem;
using CloneSystem;

namespace Interaction.KineticTension
{
    /// <summary>
    /// Tension Controller - Clone System Integration
    ///
    /// [T] KEY = Grab / release chain (manual)
    /// [R] KEY = Start clone recording (AAACloneSystem)
    /// If the chain is held during recording, the clone will also hold it.
    ///
    /// KINEMATION INTEGRATION:
    /// ChainHandIK modifier binds to IKTargetTransform property for runtime IK targeting.
    /// </summary>
    public class TensionController : MonoBehaviour
    {
        #region Configuration

        [Header("=== DETECTION ===")]
        [Tooltip("Radius to detect nearby chains")]
        [SerializeField] private float detectionRadius = 3f;

        [Tooltip("Layer mask for chain detection")]
        [SerializeField] private LayerMask interactableLayer = -1;

        [Tooltip("Auto-grab chain when recording starts near chain")]
        [SerializeField] private bool autoGrabOnRecording = false;

        [Header("=== INPUT ===")]
        #pragma warning disable CS0414
        [Tooltip("Key to grab/release chain")]
        [SerializeField] private KeyCode grabKey = KeyCode.T;
        #pragma warning restore CS0414

        [Header("=== IK SETTINGS ===")]
        [Tooltip("Right hand IK target (created at runtime if null)")]
        [SerializeField] private Transform rightHandTarget;

        [Tooltip("Left hand IK target")]
        [SerializeField] private Transform leftHandTarget;

        [Tooltip("IK weight blend speed")]
        [SerializeField] private float ikBlendSpeed = 5f;

        [Header("=== PLAYER FEEDBACK ===")]
        [Tooltip("How much the player leans back when pulling")]
        [SerializeField] private float maxLeanAngle = 15f;

        [Tooltip("Camera shake intensity at max tension")]
        [SerializeField] private float maxCameraShake = 0.1f;

        [Header("=== REFERENCES ===")]
        [SerializeField] private Animator playerAnimator;
        [SerializeField] private Transform cameraTransform;

        #endregion

        #region KINEMATION Binding Properties

        /// <summary>
        /// Public property for KINEMATION BindableProperty binding.
        /// ChainHandIK modifier should bind to: TensionController.IKTargetTransform
        /// Returns the current IK target (chain grab point) when pulling, null otherwise.
        /// </summary>
        public Transform IKTargetTransform => _isPulling && rightHandTarget != null ? rightHandTarget : null;

        /// <summary>
        /// Current IK weight (0-1) for the chain grab.
        /// ChainHandIK modifier can bind to this via: TensionController.ChainIKWeight
        /// </summary>
        public float ChainIKWeight => _currentIKWeight;

        #endregion

        #region Runtime State

        private ChainInteractable _currentChain;
        private ChainInteractable _nearbyChain; // Nearest chain (grabbed when recording starts)
        private bool _isPulling;
        private float _currentIKWeight;

        private static readonly int AnimPulling = Animator.StringToHash("IsPulling");
        private static readonly int AnimTensionAmount = Animator.StringToHash("TensionAmount");

        private Quaternion _originalRotation;
        private Vector3 _originalCameraPos;

        private float _shakeIntensity;
        private float _shakeDuration;

        private AAACloneSystem _cloneSystem;
        private AAACloneSystem.Phase _lastPhase = AAACloneSystem.Phase.Idle;

        private bool _wasHoldingDuringRecording;
        private float _progressAtRecordingEnd;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            if (playerAnimator == null)
                playerAnimator = GetComponentInChildren<Animator>();

            if (cameraTransform == null)
            {
                var cam = ServiceLocator.Instance != null ? ServiceLocator.Instance.MainCamera : Camera.main;
                if (cam != null) cameraTransform = cam.transform;
            }

            _originalRotation = transform.rotation;

            if (cameraTransform != null)
                _originalCameraPos = cameraTransform.localPosition;
        }

        private void Start()
        {
            _cloneSystem = AAACloneSystem.Instance;
            if (_cloneSystem == null)
            {
                _cloneSystem = ServiceLocator.Instance != null
                    ? ServiceLocator.Instance.CloneSystem
                    : FindFirstObjectByType<AAACloneSystem>();
            }

            if (_cloneSystem != null)
            {
                Debug.Log("[TensionController] Connected to AAACloneSystem - Listening for Recording phase");
            }
            else
            {
                Debug.LogWarning("[TensionController] AAACloneSystem not found!");
            }

            GameEvents.OnRecordingStarted += OnRecordingStarted;
            GameEvents.OnRecordingStopped += OnRecordingStopped;
            GameEvents.OnPlaybackStarted += OnPlaybackStarted;
            GameEvents.OnPlaybackEnded += OnPlaybackEnded;

            SetupIKTargets();
        }

        private void OnDestroy()
        {
            GameEvents.OnRecordingStarted -= OnRecordingStarted;
            GameEvents.OnRecordingStopped -= OnRecordingStopped;
            GameEvents.OnPlaybackStarted -= OnPlaybackStarted;
            GameEvents.OnPlaybackEnded -= OnPlaybackEnded;
        }

        private void Update()
        {
            ScanForNearbyChain();
            HandleGrabInput();
            CheckCloneSystemPhase();

            UpdateIK();
            UpdateCameraShake();
            UpdateLean();
            UpdateHaptics();

            if (_isPulling && _currentChain != null)
            {
                UpdatePulling();
            }
        }

        /// <summary>
        /// Grab or release chain with [T] key.
        /// </summary>
        private void HandleGrabInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            bool tPressed = keyboard.tKey.wasPressedThisFrame;

            if (tPressed)
            {
                Debug.Log("[TensionController] T KEY PRESSED!");

                if (_isPulling)
                {
                    StopPulling();
                    Debug.Log("[TensionController] [T] Chain released");
                }
                else if (_nearbyChain != null && _nearbyChain.CanInteract(transform))
                {
                    StartPulling(_nearbyChain);
                    Debug.Log("[TensionController] [T] Chain grabbed");
                }
                else
                {
                    Debug.Log($"[TensionController] [T] No nearby chain! nearbyChain={_nearbyChain}");
                }
            }
        }

        #endregion

        #region Clone System Integration

        private void CheckCloneSystemPhase()
        {
            if (_cloneSystem == null) return;

            var currentPhase = _cloneSystem.CurrentPhase;

            if (currentPhase != _lastPhase)
            {
                OnPhaseChanged(_lastPhase, currentPhase);
                _lastPhase = currentPhase;
            }
        }

        private void OnPhaseChanged(AAACloneSystem.Phase oldPhase, AAACloneSystem.Phase newPhase)
        {
            Debug.Log($"[TensionController] Phase changed: {oldPhase} -> {newPhase}");

            // Auto-grab chain when recording starts if nearby
            if (newPhase == AAACloneSystem.Phase.Recording && autoGrabOnRecording)
            {
                if (_nearbyChain != null && _nearbyChain.CanInteract(transform))
                {
                    StartPulling(_nearbyChain);
                }
            }
            // Recording ended: stop pulling but mark that clone should hold
            else if (oldPhase == AAACloneSystem.Phase.Recording && newPhase != AAACloneSystem.Phase.Recording)
            {
                if (_isPulling)
                {
                    _wasHoldingDuringRecording = true;
                    if (_currentChain != null)
                    {
                        _progressAtRecordingEnd = _currentChain.CurrentProgress;
                    }

                    StopPulling();
                }
            }
        }

        /// <summary>
        /// Called via GameEvents when recording starts.
        /// </summary>
        private void OnRecordingStarted()
        {
            Debug.Log("[TensionController] Recording started via GameEvents");

            // Fallback: phase check handles this, but keep as backup
            if (autoGrabOnRecording && _nearbyChain != null && !_isPulling)
            {
                if (_nearbyChain.CanInteract(transform))
                {
                    StartPulling(_nearbyChain);
                }
            }
        }

        /// <summary>
        /// Called via GameEvents when recording stops.
        /// </summary>
        private void OnRecordingStopped()
        {
            Debug.Log("[TensionController] Recording stopped via GameEvents");

            if (_isPulling && _currentChain != null)
            {
                _wasHoldingDuringRecording = true;
                _progressAtRecordingEnd = _currentChain.CurrentProgress;
                StopPulling();
            }
        }

        /// <summary>
        /// Clone playback started - clone should hold the chain if player was holding during recording.
        /// </summary>
        private void OnPlaybackStarted()
        {
            Debug.Log("[TensionController] Playback started");

            if (_wasHoldingDuringRecording && _currentChain != null)
            {
                _currentChain.CloneHold();
                Debug.Log($"[TensionController] Clone holding chain at {_progressAtRecordingEnd:P0}");
            }
        }

        /// <summary>
        /// Clone playback ended.
        /// </summary>
        private void OnPlaybackEnded()
        {
            Debug.Log("[TensionController] Playback ended");

            if (_currentChain != null)
            {
                _currentChain.CloneRelease();
            }

            _wasHoldingDuringRecording = false;
        }

        #endregion

        #region Chain Detection

        // Cached chain array - refreshed every CHAIN_CACHE_INTERVAL seconds
        private ChainInteractable[] _cachedChains;
        private float _lastChainCacheTime;
        private const float CHAIN_CACHE_INTERVAL = 0.3f;

        /// <summary>
        /// Scans for the nearest interactable chain within detection radius.
        /// Uses a cached FindObjectsByType result refreshed every 0.3 seconds.
        /// </summary>
        private void ScanForNearbyChain()
        {
            if (_cachedChains == null || Time.time - _lastChainCacheTime > CHAIN_CACHE_INTERVAL)
            {
                _cachedChains = FindObjectsByType<ChainInteractable>(FindObjectsSortMode.None);
                _lastChainCacheTime = Time.time;
            }

            ChainInteractable closest = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < _cachedChains.Length; i++)
            {
                var chain = _cachedChains[i];
                if (chain == null) continue;

                float dist = Vector3.Distance(transform.position, chain.GetGrabPosition());

                if (dist <= detectionRadius && chain.CanInteract(transform))
                {
                    if (dist < closestDist)
                    {
                        closest = chain;
                        closestDist = dist;
                    }
                }
            }

            _nearbyChain = closest;
        }

        /// <summary>
        /// Force refresh chain cache (call when new chains are spawned).
        /// </summary>
        public void RefreshChainCache()
        {
            _cachedChains = null;
            _lastChainCacheTime = 0;
        }

        #endregion

        #region Pulling Logic

        private void StartPulling(ChainInteractable chain)
        {
            if (chain == null) return;
            if (_isPulling) return;

            _currentChain = chain;
            _isPulling = true;
            _currentChain.StartPull(transform, true);

            _currentChain.OnMaxTensionReached += OnMaxTension;
            _currentChain.OnProgressChanged += OnProgressChanged;

            // NOTE: KINEMATION doesn't use IsPulling/TensionAmount parameters
            // Animator calls removed to prevent warning spam

            Debug.Log($"[TensionController] Started pulling {chain.name}");
        }

        private void StopPulling()
        {
            if (!_isPulling) return;

            _isPulling = false;

            if (_currentChain != null)
            {
                _currentChain.OnMaxTensionReached -= OnMaxTension;
                _currentChain.OnProgressChanged -= OnProgressChanged;
                _currentChain.StopPull();
            }

            // NOTE: KINEMATION doesn't use IsPulling/TensionAmount parameters
            // Animator calls removed to prevent warning spam

            _shakeIntensity = 0f;

            Debug.Log("[TensionController] Stopped pulling");
        }

        private void UpdatePulling()
        {
            if (_currentChain == null) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            float moveY = 0f;
            if (keyboard.wKey.isPressed) moveY += 1f;
            if (keyboard.sKey.isPressed) moveY -= 1f;

            Vector3 toChain = (_currentChain.GetGrabPosition() - transform.position).normalized;
            Vector3 playerForward = transform.forward;

            float facingDot = Vector3.Dot(playerForward, toChain);

            float pullInput = 0f;

            // S key = pull (moving backward pulls the chain toward you)
            if (keyboard.sKey.isPressed)
            {
                pullInput = 1f;
            }
            else if (moveY != 0f)
            {
                Vector3 moveDir = transform.forward * moveY;
                float movingAwayDot = Vector3.Dot(moveDir, -toChain);
                if (movingAwayDot > 0.3f)
                {
                    pullInput = movingAwayDot;
                }
            }

            if (pullInput > 0.01f)
            {
                _currentChain.ApplyPullInput(pullInput);
            }
        }

        #endregion

        #region Event Handlers

        private void OnMaxTension()
        {
            TriggerCameraShake(maxCameraShake, 0.5f);
            Debug.Log("[TensionController] MAX TENSION! Solo limit reached - need Clone help!");
        }

        private void OnProgressChanged(float progress)
        {
            // NOTE: Animator parameter "TensionAmount" doesn't exist in KINEMATION animator
            // Skipping animator update to prevent warning spam

            _shakeIntensity = progress * maxCameraShake * 0.3f;
        }

        #endregion

        #region IK System

        private KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core.ProceduralAnimationComponent _casComponent;
        private KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK.TwoBoneIkSettings _chainIkSettings;

        private object _ikTargetBindableProperty;
        private System.Reflection.MethodInfo _setDefaultValueMethod;
        private System.Reflection.FieldInfo _alphaField;
        private bool _ikInitialized;

        private void SetupIKTargets()
        {
            _casComponent = GetComponent<KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core.ProceduralAnimationComponent>();
            if (_casComponent == null) _casComponent = GetComponentInChildren<KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core.ProceduralAnimationComponent>();
            if (_casComponent == null) _casComponent = GetComponentInParent<KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core.ProceduralAnimationComponent>();

            if (_casComponent != null)
            {
                Debug.Log("[TensionController] Found CAS Component. Looking for TwoBoneIK modifier...");
                FindModifier();
            }
            else
            {
                Debug.LogWarning("[TensionController] CAS Component not found!");
            }

            EnsureRightHandTarget();
        }

        private void EnsureRightHandTarget()
        {
            if (rightHandTarget != null) return;

            var existing = transform.Find("ChainHandTarget");
            if (existing != null)
            {
                rightHandTarget = existing;
                return;
            }

            var sceneTarget = GameObject.Find("ChainHandTarget");
            if (sceneTarget != null)
            {
                rightHandTarget = sceneTarget.transform;
                return;
            }

            // Create under player root (not under a bone)
            var go = new GameObject("ChainHandTarget");
            Transform playerRoot = transform.root;
            go.transform.SetParent(playerRoot);
            go.transform.localPosition = new Vector3(0.5f, 1.2f, 0.3f);
            rightHandTarget = go.transform;
            Debug.Log("[TensionController] Created ChainHandTarget under player root");
        }

        private void FindModifier()
        {
            if (_casComponent == null) return;

            try
            {
                var settingsField = _casComponent.GetType().GetField("proceduralSettings",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (settingsField == null)
                {
                    Debug.LogError("[TensionController] Could not find 'proceduralSettings' field!");
                    return;
                }

                var settingsObj = settingsField.GetValue(_casComponent);
                if (settingsObj == null)
                {
                    Debug.LogError("[TensionController] 'proceduralSettings' is null! Assign a ProceduralSettings asset.");
                    return;
                }

                var modifiersField = settingsObj.GetType().GetField("modifiers",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (modifiersField == null)
                {
                    Debug.LogError("[TensionController] Could not find 'modifiers' field!");
                    return;
                }

                var settingsList = modifiersField.GetValue(settingsObj) as System.Collections.IList;
                if (settingsList == null)
                {
                    Debug.LogError("[TensionController] Modifiers list is null!");
                    return;
                }

                Debug.Log($"[TensionController] Searching {settingsList.Count} modifiers...");

                foreach (var setting in settingsList)
                {
                    if (setting == null) continue;
                    if (!(setting is UnityEngine.Object obj)) continue;

                    string objName = obj.name.ToLower();
                    string typeName = setting.GetType().Name;

                    bool isMatch = typeName == "TwoBoneIkSettings" &&
                                   (objName.Contains("chainhandik") ||
                                    objName.Contains("two bone ik") ||
                                    objName.Contains("hand ik"));

                    if (isMatch)
                    {
                        _chainIkSettings = setting as KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK.TwoBoneIkSettings;

                        _alphaField = _chainIkSettings.GetType().GetField("alpha",
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.Instance);

                        var ikTargetField = _chainIkSettings.GetType().GetField("ikTargetTransform",
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.Instance);

                        if (ikTargetField != null)
                        {
                            _ikTargetBindableProperty = ikTargetField.GetValue(_chainIkSettings);
                            if (_ikTargetBindableProperty != null)
                            {
                                _setDefaultValueMethod = _ikTargetBindableProperty.GetType().GetMethod("SetDefaultValue");

                                if (_setDefaultValueMethod != null && rightHandTarget != null)
                                {
                                    _setDefaultValueMethod.Invoke(_ikTargetBindableProperty, new object[] { rightHandTarget });
                                    Debug.Log($"[TensionController] Set ikTargetTransform.defaultValue to {rightHandTarget.name}");
                                }
                            }
                        }

                        _ikInitialized = true;
                        Debug.Log($"[TensionController] Found and initialized TwoBoneIK modifier: '{obj.name}'");
                        return;
                    }
                }

                Debug.LogWarning("[TensionController] No matching TwoBoneIK modifier found. " +
                    "Run Tools/Kinetic Tension/Setup Chain Hand IK");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[TensionController] Reflection error: {e.Message}\n{e.StackTrace}");
            }
        }

        private void UpdateIK()
        {
            float targetWeight = (_isPulling && _currentChain != null) ? 1f : 0f;
            _currentIKWeight = Mathf.Lerp(_currentIKWeight, targetWeight, Time.deltaTime * ikBlendSpeed);

            if (_isPulling && _currentChain != null && rightHandTarget != null)
            {
                var grabTransform = _currentChain.GetGrabPointTransform();
                if (grabTransform != null)
                {
                    rightHandTarget.position = grabTransform.position;
                    rightHandTarget.rotation = grabTransform.rotation;
                }
            }

            if (_chainIkSettings != null && _ikInitialized)
            {
                try
                {
                    if (_alphaField != null)
                    {
                        _alphaField.SetValue(_chainIkSettings, _currentIKWeight);
                    }

                    if (_setDefaultValueMethod != null && rightHandTarget != null && _currentIKWeight > 0.01f)
                    {
                        _setDefaultValueMethod.Invoke(_ikTargetBindableProperty, new object[] { rightHandTarget });
                    }
                }
                catch (System.Exception)
                {
                    // Silently ignore to prevent spam
                }
            }
            else if (!_ikInitialized && Time.frameCount % 120 == 0)
            {
                FindModifier();
            }
        }

        /// <summary>
        /// Unity's built-in IK callback - fallback if KINEMATION IK is unavailable.
        /// Requires an Animator with IK Pass enabled on the animation layer.
        /// </summary>
        private void OnAnimatorIK(int layerIndex)
        {
            if (playerAnimator == null) return;
            if (!_isPulling || _currentChain == null || rightHandTarget == null) return;
            if (_currentIKWeight < 0.01f) return;

            playerAnimator.SetIKPositionWeight(AvatarIKGoal.RightHand, _currentIKWeight);
            playerAnimator.SetIKRotationWeight(AvatarIKGoal.RightHand, _currentIKWeight * 0.5f);
            playerAnimator.SetIKPosition(AvatarIKGoal.RightHand, rightHandTarget.position);
            playerAnimator.SetIKRotation(AvatarIKGoal.RightHand, rightHandTarget.rotation);

            playerAnimator.SetLookAtWeight(_currentIKWeight * 0.3f);
            playerAnimator.SetLookAtPosition(rightHandTarget.position);
        }

        #endregion

        #region Visual Feedback

        private void UpdateLean()
        {
            if (!_isPulling || _currentChain == null) return;

            float tension = _currentChain.CurrentProgress;
            Vector3 toChain = (_currentChain.GetGrabPosition() - transform.position).normalized;

            float leanAmount = tension * maxLeanAngle;
            // Apply lean via animation layer
        }

        private void TriggerCameraShake(float intensity, float duration)
        {
            _shakeIntensity = Mathf.Max(_shakeIntensity, intensity);
            _shakeDuration = Mathf.Max(_shakeDuration, duration);
        }

        private void UpdateCameraShake()
        {
            if (cameraTransform == null) return;

            if (_shakeDuration > 0)
            {
                _shakeDuration -= Time.deltaTime;

                Vector3 shakeOffset = new Vector3(
                    Random.Range(-1f, 1f) * _shakeIntensity,
                    Random.Range(-1f, 1f) * _shakeIntensity,
                    0f
                );

                cameraTransform.localPosition = _originalCameraPos + shakeOffset;

                _shakeIntensity *= 0.95f;
            }
            else
            {
                cameraTransform.localPosition = Vector3.Lerp(
                    cameraTransform.localPosition,
                    _originalCameraPos,
                    Time.deltaTime * 10f
                );
            }
        }

        /// <summary>
        /// Gamepad haptic feedback based on chain tension.
        /// </summary>
        private void UpdateHaptics()
        {
            var gamepad = Gamepad.current;
            if (gamepad == null) return;

            if (_isPulling && _currentChain != null)
            {
                float tension = _currentChain.CurrentProgress;

                float lowFreq = tension * 0.4f;
                float highFreq = tension * tension * 0.6f;

                if (tension > 0.95f)
                {
                    float pulse = Mathf.Sin(Time.time * 20f) * 0.3f + 0.7f;
                    highFreq *= pulse;
                }

                gamepad.SetMotorSpeeds(lowFreq, highFreq);
            }
            else
            {
                gamepad.SetMotorSpeeds(0f, 0f);
            }
        }

        #endregion

        #region Public API

        /// <summary>Currently pulling a chain.</summary>
        public bool IsCurrentlyPulling => _isPulling;

        /// <summary>Current chain being pulled.</summary>
        public ChainInteractable CurrentChain => _currentChain;

        /// <summary>Nearest chain within detection radius.</summary>
        public ChainInteractable NearbyChain => _nearbyChain;

        /// <summary>Was the chain held during the last recording session.</summary>
        public bool WasHoldingDuringRecording => _wasHoldingDuringRecording;

        /// <summary>Chain progress value at the moment recording ended.</summary>
        public float ProgressAtRecordingEnd => _progressAtRecordingEnd;

        #endregion

        #region Gizmos

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, detectionRadius);

            if (_nearbyChain != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, _nearbyChain.GetGrabPosition());
                Gizmos.DrawWireSphere(_nearbyChain.GetGrabPosition(), 0.2f);
            }

            if (_currentChain != null && _isPulling)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position + Vector3.up, _currentChain.GetGrabPosition());
            }
        }

        #endregion
    }
}
