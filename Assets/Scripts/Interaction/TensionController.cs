using UnityEngine;
using UnityEngine.InputSystem;
using CloneSystem;

namespace Interaction.KineticTension
{
    // [T] = grab / release chain
    // [R] = start clone recording (handled by AAACloneSystem)
    // If you're holding the chain while recording, the clone will hold it too during playback.
    //
    // KINEMATION: ChainHandIK modifier binds to IKTargetTransform for runtime IK targeting.
    public class TensionController : MonoBehaviour
    {
        [Header("Detection")]
        [Tooltip("Radius to detect nearby chains")]
        [SerializeField] private float detectionRadius = 3f;

        [Tooltip("Layer mask for chain detection")]
        [SerializeField] private LayerMask interactableLayer = -1;

        [Tooltip("Auto-grab chain when recording starts near chain")]
        [SerializeField] private bool autoGrabOnRecording = false;

        [Header("Input")]
        [Tooltip("Key to grab/release chain")]
        // TODO: migrate grabKey to InputActionReference so it respects rebinding
        [SerializeField] private KeyCode grabKey = KeyCode.T;

        [Header("IK Settings")]
        [Tooltip("Right hand IK target (created at runtime if null)")]
        [SerializeField] private Transform rightHandTarget;

        [Tooltip("Left hand IK target")]
        [SerializeField] private Transform leftHandTarget;

        [Tooltip("IK weight blend speed")]
        [SerializeField] private float ikBlendSpeed = 5f;

        [Header("Player Feedback")]
        [Tooltip("How much the player leans back when pulling")]
        [SerializeField] private float maxLeanAngle = 15f;

        [Tooltip("Camera shake intensity at max tension")]
        [SerializeField] private float maxCameraShake = 0.1f;

        [Header("References")]
        [SerializeField] private Animator playerAnimator;
        [SerializeField] private Transform cameraTransform;

        // KINEMATION binding - ChainHandIK modifier should bind to TensionController.IKTargetTransform
        public Transform IKTargetTransform => _isPulling && rightHandTarget != null ? rightHandTarget : null;

        // 0-1 weight for the chain IK, ChainHandIK can bind to this
        public float ChainIKWeight => _currentIKWeight;

        private ChainInteractable _currentChain;
        private ChainInteractable _nearbyChain;
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
                Debug.Log("[TensionController] Connected to CloneSystem");
            }
            else
            {
                Debug.LogWarning("[TensionController] CloneSystem not found!");
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
                UpdatePulling();
        }

        private void HandleGrabInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            bool tPressed = keyboard.tKey.wasPressedThisFrame;

            if (tPressed)
            {
                Debug.Log("[TensionController] T pressed");

                if (_isPulling)
                {
                    StopPulling();
                    Debug.Log("[TensionController] chain released");
                }
                else if (_nearbyChain != null && _nearbyChain.CanInteract(transform))
                {
                    StartPulling(_nearbyChain);
                    Debug.Log("[TensionController] chain grabbed");
                }
                else
                {
                    Debug.Log($"[TensionController] nothing to grab (nearbyChain={_nearbyChain})");
                }
            }
        }

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
            Debug.Log($"[TensionController] phase: {oldPhase} -> {newPhase}");

            if (newPhase == AAACloneSystem.Phase.Recording && autoGrabOnRecording)
            {
                if (_nearbyChain != null && _nearbyChain.CanInteract(transform))
                    StartPulling(_nearbyChain);
            }
            else if (oldPhase == AAACloneSystem.Phase.Recording && newPhase != AAACloneSystem.Phase.Recording)
            {
                if (_isPulling)
                {
                    _wasHoldingDuringRecording = true;
                    if (_currentChain != null)
                        _progressAtRecordingEnd = _currentChain.CurrentProgress;

                    StopPulling();
                }
            }
        }

        private void OnRecordingStarted()
        {
            Debug.Log("[TensionController] recording started (GameEvents)");

            // phase change handles this normally, this is just a safety net
            if (autoGrabOnRecording && _nearbyChain != null && !_isPulling)
            {
                if (_nearbyChain.CanInteract(transform))
                    StartPulling(_nearbyChain);
            }
        }

        private void OnRecordingStopped()
        {
            Debug.Log("[TensionController] recording stopped");

            if (_isPulling && _currentChain != null)
            {
                _wasHoldingDuringRecording = true;
                _progressAtRecordingEnd = _currentChain.CurrentProgress;
                StopPulling();
            }
        }

        private void OnPlaybackStarted()
        {
            Debug.Log("[TensionController] playback started");

            if (_wasHoldingDuringRecording && _currentChain != null)
            {
                _currentChain.CloneHold();
                Debug.Log($"[TensionController] clone holding chain at {_progressAtRecordingEnd:P0}");
            }
        }

        private void OnPlaybackEnded()
        {
            Debug.Log("[TensionController] playback ended");

            if (_currentChain != null)
                _currentChain.CloneRelease();

            _wasHoldingDuringRecording = false;
        }

        // cache refreshed every 0.3s so we're not calling FindObjectsByType every frame
        private ChainInteractable[] _cachedChains;
        private float _lastChainCacheTime;
        private const float CHAIN_CACHE_INTERVAL = 0.3f;

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

        // call this when new chains are spawned at runtime
        public void RefreshChainCache()
        {
            _cachedChains = null;
            _lastChainCacheTime = 0;
        }

        private void StartPulling(ChainInteractable chain)
        {
            if (chain == null) return;
            if (_isPulling) return;

            _currentChain = chain;
            _isPulling = true;
            _currentChain.StartPull(transform, true);

            _currentChain.OnMaxTensionReached += OnMaxTension;
            _currentChain.OnProgressChanged += OnProgressChanged;

            // NOTE: KINEMATION animator doesn't have IsPulling/TensionAmount params
            // leaving out Animator.SetBool calls to avoid console spam

            Debug.Log($"[TensionController] pulling {chain.name}");
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

            _shakeIntensity = 0f;

            Debug.Log("[TensionController] stopped pulling");
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

            // S = pull toward you (moving back relative to chain)
            if (keyboard.sKey.isPressed)
            {
                pullInput = 1f;
            }
            else if (moveY != 0f)
            {
                Vector3 moveDir = transform.forward * moveY;
                float movingAwayDot = Vector3.Dot(moveDir, -toChain);
                if (movingAwayDot > 0.3f)
                    pullInput = movingAwayDot;
            }

            if (pullInput > 0.01f)
                _currentChain.ApplyPullInput(pullInput);
        }

        private void OnMaxTension()
        {
            TriggerCameraShake(maxCameraShake, 0.5f);
            Debug.Log("[TensionController] max tension reached - need clone to help!");
        }

        private void OnProgressChanged(float progress)
        {
            // Animator "TensionAmount" doesn't exist in the KINEMATION controller, skip it
            _shakeIntensity = progress * maxCameraShake * 0.3f;
        }

        // KINEMATION reflection fields - fragile but necessary until we have a proper binding API
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
                Debug.Log("[TensionController] found CAS component, searching for TwoBoneIK...");
                FindModifier();
            }
            else
            {
                Debug.LogWarning("[TensionController] CAS component not found");
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

            // nothing found, create one under player root
            var go = new GameObject("ChainHandTarget");
            Transform playerRoot = transform.root;
            go.transform.SetParent(playerRoot);
            go.transform.localPosition = new Vector3(0.5f, 1.2f, 0.3f);
            rightHandTarget = go.transform;
            Debug.Log("[TensionController] created ChainHandTarget under player root");
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
                    Debug.LogError("[TensionController] 'proceduralSettings' field not found");
                    return;
                }

                var settingsObj = settingsField.GetValue(_casComponent);
                if (settingsObj == null)
                {
                    Debug.LogError("[TensionController] 'proceduralSettings' is null - assign a PA asset");
                    return;
                }

                var modifiersField = settingsObj.GetType().GetField("modifiers",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (modifiersField == null)
                {
                    Debug.LogError("[TensionController] 'modifiers' field not found");
                    return;
                }

                var settingsList = modifiersField.GetValue(settingsObj) as System.Collections.IList;
                if (settingsList == null)
                {
                    Debug.LogError("[TensionController] modifiers list is null");
                    return;
                }

                Debug.Log($"[TensionController] checking {settingsList.Count} modifiers...");

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
                                    Debug.Log($"[TensionController] ikTargetTransform bound to {rightHandTarget.name}");
                                }
                            }
                        }

                        _ikInitialized = true;
                        Debug.Log($"[TensionController] TwoBoneIK modifier found: '{obj.name}'");
                        return;
                    }
                }

                // TODO: auto-create the modifier if missing instead of just warning
                Debug.LogWarning("[TensionController] no matching TwoBoneIK modifier found. " +
                    "Run Tools/Kinetic Tension/Setup Chain Hand IK");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[TensionController] reflection error: {e.Message}\n{e.StackTrace}");
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
                        _alphaField.SetValue(_chainIkSettings, _currentIKWeight);

                    if (_setDefaultValueMethod != null && rightHandTarget != null && _currentIKWeight > 0.01f)
                        _setDefaultValueMethod.Invoke(_ikTargetBindableProperty, new object[] { rightHandTarget });
                }
                catch (System.Exception)
                {
                    // swallow to avoid log spam every frame
                }
            }
            else if (!_ikInitialized && Time.frameCount % 120 == 0)
            {
                // retry periodically in case PA wasn't ready at Start
                FindModifier();
            }
        }

        // fallback for when KINEMATION IK isn't set up - needs IK Pass enabled on animator layer
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

        private void UpdateLean()
        {
            if (!_isPulling || _currentChain == null) return;

            float tension = _currentChain.CurrentProgress;
            Vector3 toChain = (_currentChain.GetGrabPosition() - transform.position).normalized;

            float leanAmount = tension * maxLeanAngle;
            // FIXME: lean isn't actually applied anywhere yet, need an animation layer for this
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

        private void UpdateHaptics()
        {
            var gamepad = Gamepad.current;
            if (gamepad == null) return;

            if (_isPulling && _currentChain != null)
            {
                float tension = _currentChain.CurrentProgress;

                float lowFreq = tension * 0.4f;
                float highFreq = tension * tension * 0.6f;

                // pulse at near-max tension for extra drama
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

        public bool IsCurrentlyPulling => _isPulling;
        public ChainInteractable CurrentChain => _currentChain;
        public ChainInteractable NearbyChain => _nearbyChain;
        public bool WasHoldingDuringRecording => _wasHoldingDuringRecording;
        public float ProgressAtRecordingEnd => _progressAtRecordingEnd;

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
    }
}
