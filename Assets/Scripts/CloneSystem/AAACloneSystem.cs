using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.Collections;
using System.Collections.Generic;
using CloneGame.Audio;
using CloneGame.FPS;
using CloneGame.Core;
using CAS_Demo.Scripts.FPS;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;

namespace CloneSystem
{
    /// <summary>
    /// AAA Clone System v3.0 - SUPERHOT Edition (Performance Optimized)
    /// Features: Object pooling, Catmull-Rom splines, keyframe compression, LOD system
    /// Optimizations: Zero-GC bone capture, fixed-rate recording, distance-based LOD
    /// </summary>
    public class AAACloneSystem : MonoBehaviour
    {
        public static AAACloneSystem Instance { get; private set; }
        
        #region Clone Types
        
        public enum CloneType
        {
            Routine,  // Slow motion ability
            Fear,     // Future: enemies scared
            Brave     // Future: damage boost
        }
        
        [Serializable]
        public class CloneTypeData
        {
            public string displayName;
            public string description;
            public Color color;
            public Color darkColor;
            
            // NEW: Gameplay-affecting properties
            public float timeScaleMultiplier = 1f;    // 1.0 = normal, 0.5 = half speed
            public float distortionStrength = 0f;     // Visual distortion effect
            public bool affectsEnemyAI = false;       // Fear: enemies react
            public bool isActive = true;              // Can this clone type be used?
        }
        
        private readonly CloneTypeData[] _cloneTypes = new CloneTypeData[]
        {
            new CloneTypeData {
                displayName = "clone_routine",
                description = "clone_routine_desc",
                color = new Color(0.2f, 0.6f, 1f, 1f),
                darkColor = new Color(0.1f, 0.3f, 0.5f, 1f),
                timeScaleMultiplier = 0.1f,  // Full slow-mo when idle
                distortionStrength = 0f,
                affectsEnemyAI = false,
                isActive = true
            },
            new CloneTypeData {
                displayName = "clone_fear",
                description = "clone_fear_desc",
                color = new Color(0.7f, 0.2f, 1f, 1f),
                darkColor = new Color(0.35f, 0.1f, 0.5f, 1f),
                timeScaleMultiplier = 0.5f,  // Partial slow-mo always active
                distortionStrength = 0.3f,   // Visual warping
                affectsEnemyAI = true,       // Enemies flee
                isActive = false  // DEACTIVATED - Future use
            },
            new CloneTypeData {
                displayName = "clone_brave",
                description = "clone_brave_desc",
                color = new Color(1f, 0.4f, 0.1f, 1f),
                darkColor = new Color(0.5f, 0.2f, 0.05f, 1f),
                timeScaleMultiplier = 1.2f,  // Faster than normal
                distortionStrength = 0.1f,   // Subtle heat effect
                affectsEnemyAI = false,
                isActive = false  // DEACTIVATED - Future use
            }
        };
        
        #endregion
        
        #region Settings
        
        [Header("Clone Settings")]
        [SerializeField] private float maxRecordDuration = 5f;
        [SerializeField] private float rewindSpeed = 3f;
        [SerializeField] private int recordingFPS = 60; // Fixed recording rate for consistent playback

        [Header("AAA Optimization")]
        [SerializeField] private bool useKeyframeCompression = true;
        [SerializeField] private float positionThreshold = 0.001f; // Minimum position change to record
        [SerializeField] private float rotationThreshold = 0.1f;   // Minimum rotation change (degrees)

        [Header("Clone LOD")]
        [SerializeField] private bool useLOD = true;
        [SerializeField] private float lodDistance1 = 10f;  // Distance for LOD 1 (reduced updates)
        [SerializeField] private float lodDistance2 = 25f;  // Distance for LOD 2 (minimal updates)
        
        [Header("SUPERHOT Time (Only during Playback)")]
        [SerializeField] private float slowMotionTimeScale = 0.1f;
        [SerializeField] private float normalTimeScale = 1f;
        [SerializeField] private float timeTransitionSpeed = 10f;
        [SerializeField] private float movementThreshold = 0.05f;
        
        [Header("Input")]
        [SerializeField] private KeyCode recordKey = KeyCode.R;
        [SerializeField] private KeyCode playbackKey = KeyCode.Space;
        [SerializeField] private KeyCode selectionKey = KeyCode.Tab;
        
        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = false; // Disabled by default - enable for testing

        [Header("Footstep Audio")]
        [SerializeField] private FootstepDatabase _footstepDatabase;
        [SerializeField] private bool enableCloneFootsteps = true;
        #pragma warning disable CS0414
        [SerializeField] [Range(0f, 1f)] private float cloneFootstepVolume = 0.4f;
        #pragma warning restore CS0414
        
        #endregion
        
        #region State
        
        public enum Phase { Idle, Recording, Rewinding, Review, Playback }
        private Phase _currentPhase = Phase.Idle;
        public Phase CurrentPhase => _currentPhase;
        
        // Selected clone type
        private int _selectedIndex = 0;
        public CloneType SelectedCloneType => (CloneType)_selectedIndex;
        public int SelectedIndex => _selectedIndex;
        
        // Selection UI
        private bool _selectionOpen = false;
        public bool IsSelectionOpen => _selectionOpen;
        
        // Public accessors for UI
        public CloneTypeData[] CloneTypes => _cloneTypes;
        public bool IsClonesPaused => _clonesPaused;
        public int RecordingCount => _recordings.Count;
        
        public bool HasRecording(CloneType type) => _recordings.ContainsKey(type);
        
        public void SetSelectedIndex(int index)
        {
            if (index >= 0 && index < 3 && index != _selectedIndex)
            {
                _selectedIndex = index;
                OnSelectionChanged?.Invoke();
            }
        }
        
        // UI callback event
        public event Action OnSelectionChanged;
        
        // Recordings
        private Dictionary<CloneType, Recording> _recordings = new Dictionary<CloneType, Recording>();
        private Recording _currentRecording;
        private Vector3 _recordStartPos;
        private Quaternion _recordStartRot;
        
        // Player references
        private Transform _playerRoot;
        private Transform _playerModel;
        private Animator _playerAnimator;
        private CharacterController _characterController;
        private MonoBehaviour _movementController;
        private PlayerInput _playerInput;
        private Transform[] _playerBones;
        
        // SUPERHOT time
        private float _targetTimeScale = 1f;
        private Vector3 _lastPlayerPos;
        private bool _isPlayerMoving;
        
        // Clones - pre-pooled
        private List<CloneInstance> _clonePool = new List<CloneInstance>();
        private List<CloneInstance> _activeClones = new List<CloneInstance>();
        private bool _clonesPaused = false;
        private bool _poolReady = false;

        // Footstep recording
        private AdvancedFootstepSystem _playerFootstepSystem;
        private float _recordingStartTime;
        
        // Events
        public event Action<Phase> OnPhaseChanged;
        
        #endregion
        
        #region Data Structures
        
        [Serializable]
        public class Frame
        {
            public float time;
            public Vector3 position;
            public Quaternion rotation;
            public BoneData[] bones;
        }
        
        [Serializable]
        public struct BoneData
        {
            public Vector3 localPos;
            public Quaternion localRot;
        }
        
        public class Recording
        {
            public CloneType type;
            public List<Frame> frames = new List<Frame>();
            public List<FootstepEvent> footstepEvents = new List<FootstepEvent>();
            public float duration => frames.Count > 0 ? frames[frames.Count - 1].time : 0f;
            
            // Pre-allocated buffers to avoid GC allocation every frame
            private Frame _cachedFrame;
            private BoneData[] _boneBuffer;
            
            // Smoothing state for AAA-quality natural movement
            private Vector3 _smoothedPos;
            private Quaternion _smoothedRot;
            private Vector3 _posVelocity;
            private float _lastT = -1f;
            
            /// <summary>
            /// AAA-Quality frame interpolation with Catmull-Rom spline and smoothdamp
            /// Produces natural, organic motion instead of robotic linear interpolation
            /// </summary>
            public Frame GetFrameAtTime(float t)
            {
                if (frames.Count == 0) return null;
                if (t <= 0) return frames[0];
                if (t >= duration) return frames[frames.Count - 1];
                
                // Find frame indices for Catmull-Rom (needs 4 points: p0, p1, p2, p3)
                int frameIndex = 0;
                for (int i = 0; i < frames.Count - 1; i++)
                {
                    if (frames[i + 1].time > t)
                    {
                        frameIndex = i;
                        break;
                    }
                }
                
                // Get 4 control points for Catmull-Rom
                int p0 = Mathf.Max(0, frameIndex - 1);
                int p1 = frameIndex;
                int p2 = Mathf.Min(frames.Count - 1, frameIndex + 1);
                int p3 = Mathf.Min(frames.Count - 1, frameIndex + 2);
                
                // Calculate local t between p1 and p2
                float segmentDuration = frames[p2].time - frames[p1].time;
                float localT = segmentDuration > 0.001f ? (t - frames[p1].time) / segmentDuration : 0f;
                
                // Apply easing for more natural motion
                localT = SmoothStepEasing(localT);
                
                // Reuse cached frame to avoid allocation
                if (_cachedFrame == null)
                    _cachedFrame = new Frame();
                
                _cachedFrame.time = t;
                
                // Catmull-Rom spline interpolation for position
                _cachedFrame.position = CatmullRom(
                    frames[p0].position,
                    frames[p1].position,
                    frames[p2].position,
                    frames[p3].position,
                    localT
                );
                
                // SLERP with extra smoothing for rotation
                Quaternion baseRot = Quaternion.Slerp(frames[p1].rotation, frames[p2].rotation, localT);
                _cachedFrame.rotation = Quaternion.Slerp(_smoothedRot, baseRot, 0.2f); // Extra damping
                _smoothedRot = _cachedFrame.rotation;
                
                // Apply SmoothDamp for buttery smooth motion
                if (_lastT >= 0 && Mathf.Abs(t - _lastT) < 0.1f)
                {
                    _cachedFrame.position = Vector3.SmoothDamp(
                        _smoothedPos, 
                        _cachedFrame.position, 
                        ref _posVelocity, 
                        0.03f, // Smoothing time - lower = snappier
                        float.MaxValue,
                        Time.unscaledDeltaTime
                    );
                }
                _smoothedPos = _cachedFrame.position;
                _lastT = t;
                
                // Bones with enhanced smoothing
                _cachedFrame.bones = InterpolateBonesSmooth(
                    frames[p1].bones, 
                    frames[p2].bones, 
                    localT
                );
                
                return _cachedFrame;
            }
            
            /// <summary>
            /// Catmull-Rom spline for natural curved motion paths
            /// </summary>
            private Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
            {
                float t2 = t * t;
                float t3 = t2 * t;
                
                return 0.5f * (
                    (2f * p1) +
                    (-p0 + p2) * t +
                    (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                    (-p0 + 3f * p1 - 3f * p2 + p3) * t3
                );
            }
            
            /// <summary>
            /// SmoothStep easing for organic acceleration/deceleration
            /// </summary>
            private float SmoothStepEasing(float t)
            {
                // Hermite smoothstep: 3t² - 2t³
                return t * t * (3f - 2f * t);
            }
            
            /// <summary>
            /// Enhanced bone interpolation with extra smoothing per bone
            /// </summary>
            private BoneData[] InterpolateBonesSmooth(BoneData[] a, BoneData[] b, float t)
            {
                if (a == null || b == null) return a ?? b;
                if (a.Length != b.Length) return t < 0.5f ? a : b;
                
                // Allocate buffer only once, reuse thereafter
                if (_boneBuffer == null || _boneBuffer.Length != a.Length)
                    _boneBuffer = new BoneData[a.Length];
                
                // Apply smoothstep to get natural motion
                float smoothT = SmoothStepEasing(t);
                
                for (int i = 0; i < a.Length; i++)
                {
                    _boneBuffer[i].localPos = Vector3.Lerp(a[i].localPos, b[i].localPos, smoothT);
                    _boneBuffer[i].localRot = Quaternion.Slerp(a[i].localRot, b[i].localRot, smoothT);
                }
                return _boneBuffer;
            }
        }
        
        private class CloneInstance
        {
            public CloneType type;
            public GameObject gameObject;
            public Transform transform;
            public Transform[] bones;
            public Animator animator;
            public Recording recording;
            public float playbackTime;
            public bool isPlaying;
            public Frame currentFrame;

            // LOD state
            public float distanceToPlayer;
            public int currentLOD; // 0 = full quality, 1 = reduced, 2 = minimal
            public int boneUpdateSkip; // Skip bone updates based on LOD

            // Footstep audio
            public CloneFootstepPlayer footstepPlayer;
            public int nextFootstepIndex; // Track which footstep to play next
        }
        
        #endregion
        
        #region Lifecycle
        
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // Persist across scene loads
            if (transform.parent != null) transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }
        
        private void Start()
        {
            StartCoroutine(Initialize());
        }
        
        private IEnumerator Initialize()
        {
            Log("Initializing v2.1...");
            yield return null;
            
            // Find player
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) player = GameObject.Find(GameConstants.PLAYER_CHARACTER_NAME);
            
            if (player != null)
            {
                _playerModel = player.transform;
                _playerRoot = player.transform;

                // Find animator - check root first, then children
                _playerAnimator = player.GetComponent<Animator>();
                if (_playerAnimator == null)
                {
                    _playerAnimator = player.GetComponentInChildren<Animator>();
                    if (_playerAnimator != null)
                    {
                        _playerModel = _playerAnimator.transform;
                        Log($"Animator found on child: {_playerAnimator.gameObject.name}");
                    }
                }

                // Find root with CharacterController
                Transform current = player.transform;
                while (current != null)
                {
                    var cc = current.GetComponent<CharacterController>();
                    if (cc != null)
                    {
                        _playerRoot = current;
                        _characterController = cc;
                        break;
                    }
                    current = current.parent;
                }

                // Find movement controller (type-safe)
                _movementController = _playerRoot.GetComponentInChildren<FPSExampleController>();

                // Find PlayerInput
                _playerInput = _playerRoot.GetComponent<PlayerInput>();
                if (_playerInput == null)
                    _playerInput = _playerRoot.GetComponentInChildren<PlayerInput>();

                // Cache player bones - try humanoid first, fallback to all transforms
                if (_playerAnimator != null)
                {
                    var boneList = new List<Transform>();

                    // Try humanoid bones first
                    if (_playerAnimator.avatar != null && _playerAnimator.avatar.isHuman)
                    {
                        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                        {
                            var bone = _playerAnimator.GetBoneTransform((HumanBodyBones)i);
                            if (bone != null) boneList.Add(bone);
                        }
                        Log($"Humanoid bones found: {boneList.Count}");
                    }

                    // Fallback: get all child transforms with "bone" or common bone names
                    if (boneList.Count == 0)
                    {
                        var allTransforms = _playerModel.GetComponentsInChildren<Transform>();
                        foreach (var t in allTransforms)
                        {
                            string nameLower = t.name.ToLower();
                            if (nameLower.Contains("bone") || nameLower.Contains("spine") ||
                                nameLower.Contains("hips") || nameLower.Contains("arm") ||
                                nameLower.Contains("leg") || nameLower.Contains("head") ||
                                nameLower.Contains("neck") || nameLower.Contains("hand") ||
                                nameLower.Contains("foot") || nameLower.Contains("shoulder"))
                            {
                                boneList.Add(t);
                            }
                        }
                        Log($"Generic bones found by name: {boneList.Count}");
                    }

                    // Last fallback: just get all transforms
                    if (boneList.Count == 0)
                    {
                        boneList.AddRange(_playerModel.GetComponentsInChildren<Transform>());
                        Log($"All transforms as bones: {boneList.Count}");
                    }

                    _playerBones = boneList.ToArray();
                }
                else
                {
                    Debug.LogWarning("[AAAClone] No Animator found on player!");
                }
                
                _lastPlayerPos = _playerRoot.position;

                // Setup footstep recording
                _playerFootstepSystem = _playerRoot.GetComponent<AdvancedFootstepSystem>();
                if (_playerFootstepSystem == null)
                    _playerFootstepSystem = _playerRoot.GetComponentInChildren<AdvancedFootstepSystem>();

                if (_playerFootstepSystem != null && enableCloneFootsteps)
                {
                    _playerFootstepSystem.OnFootstep += OnPlayerFootstep;
                    _playerFootstepSystem.OnJumpLand += OnPlayerJumpLand;
                    Log("Player footstep system connected for recording");
                }

                Log($"Player found: {_playerRoot.name}, Bones: {_playerBones?.Length ?? 0}");

                // Auto-find FootstepDatabase if not assigned
                if (_footstepDatabase == null && enableCloneFootsteps)
                {
#if UNITY_EDITOR
                    string[] guids = UnityEditor.AssetDatabase.FindAssets("t:FootstepDatabase");
                    if (guids.Length > 0)
                    {
                        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                        _footstepDatabase = UnityEditor.AssetDatabase.LoadAssetAtPath<FootstepDatabase>(path);
                        Log($"Auto-loaded FootstepDatabase from: {path}");
                    }
#endif
                }

                // Pre-pool clones (spread across frames to avoid stutter)
                yield return StartCoroutine(PrePoolClones());
            }
            else
            {
                Debug.LogError("[AAAClone] Player not found!");
            }
        }
        
        private IEnumerator PrePoolClones()
        {
            Log("Pre-pooling clones...");

            for (int i = 0; i < 3; i++)
            {
                // Only pool active clone types to save memory
                if (!_cloneTypes[i].isActive)
                {
                    Log($"Skipping inactive clone type: {_cloneTypes[i].displayName}");
                    continue;
                }

                // Create clone with delayed component removal to avoid fidA != fidB error
                yield return StartCoroutine(CreateCloneInstanceAsync((CloneType)i, (clone) =>
                {
                    if (clone != null)
                    {
                        clone.gameObject.SetActive(false);
                        _clonePool.Add(clone);
                    }
                }));
            }

            _poolReady = true;
            Log($"Pool ready: {_clonePool.Count} active clones");
        }
        
        private IEnumerator CreateCloneInstanceAsync(CloneType type, System.Action<CloneInstance> onComplete)
        {
            if (_playerModel == null)
            {
                onComplete?.Invoke(null);
                yield break;
            }
            
            // CRITICAL FIX for 'fidA != fidB' error:
            // Disable cameras on player BEFORE cloning to prevent HDRP ID conflicts
            var playerCameras = _playerModel.GetComponentsInChildren<Camera>(true);
            var cameraStates = new bool[playerCameras.Length];
            for (int i = 0; i < playerCameras.Length; i++)
            {
                cameraStates[i] = playerCameras[i].gameObject.activeSelf;
                playerCameras[i].gameObject.SetActive(false);
            }

            // Disable KINEMATION components on player BEFORE cloning to prevent
            // IK initialization errors on clone's Awake() (NullRef in TwoBoneIkJob/StepModifierJob)
            // Disable KINEMATION + animation components using type-safe checks
            var playerCASComponents = _playerModel.GetComponentsInChildren<MonoBehaviour>(true);
            var casStates = new System.Collections.Generic.Dictionary<MonoBehaviour, bool>();
            foreach (var comp in playerCASComponents)
            {
                if (comp == null) continue;
                if (comp is CharacterAnimationComponent ||
                    comp is ProceduralAnimationComponent ||
                    comp is LayeredBlendingComponent ||
                    comp is RecoilAnimation ||
                    comp.GetType().Name == "CharacterCamera")
                {
                    casStates[comp] = comp.enabled;
                    comp.enabled = false;
                }
            }

            // Wait a frame to ensure deactivation is processed
            yield return null;

            var cloneGO = Instantiate(_playerModel.gameObject);
            cloneGO.name = $"Clone_{_cloneTypes[(int)type].displayName}";
            cloneGO.transform.SetParent(transform);

            // Restore player cameras immediately after clone
            for (int i = 0; i < playerCameras.Length; i++)
            {
                if (playerCameras[i] != null)
                    playerCameras[i].gameObject.SetActive(cameraStates[i]);
            }

            // Restore player KINEMATION components immediately after clone
            foreach (var kvp in casStates)
            {
                if (kvp.Key != null)
                    kvp.Key.enabled = kvp.Value;
            }
            
            // CRITICAL: Wait frames before removing components to avoid 'fidA != fidB' HDRP bug
            yield return null;
            yield return null;
            
            // Remove unwanted components safely - now cameras are already inactive in clone
            RemoveUnwantedComponentsSafe(cloneGO);
            
            // Wait frames to let Unity's internal cleanup complete
            yield return null;
            yield return null;
            
            // Find animator - might be on root or child
            var animator = cloneGO.GetComponent<Animator>();
            if (animator == null)
            {
                animator = cloneGO.GetComponentInChildren<Animator>();
            }

            // Keep animator disabled during pooling - we control bones manually
            if (animator != null)
            {
                animator.enabled = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            // Cache bones - must match player bone order!
            Transform[] bones = null;
            var boneList = new List<Transform>();

            if (animator != null)
            {
                // Try humanoid bones first
                if (animator.avatar != null && animator.avatar.isHuman)
                {
                    for (int j = 0; j < (int)HumanBodyBones.LastBone; j++)
                    {
                        var bone = animator.GetBoneTransform((HumanBodyBones)j);
                        if (bone != null) boneList.Add(bone);
                    }
                }

                // Fallback: match player bone search pattern
                if (boneList.Count == 0)
                {
                    var allTransforms = cloneGO.GetComponentsInChildren<Transform>();
                    foreach (var t in allTransforms)
                    {
                        string nameLower = t.name.ToLower();
                        if (nameLower.Contains("bone") || nameLower.Contains("spine") ||
                            nameLower.Contains("hips") || nameLower.Contains("arm") ||
                            nameLower.Contains("leg") || nameLower.Contains("head") ||
                            nameLower.Contains("neck") || nameLower.Contains("hand") ||
                            nameLower.Contains("foot") || nameLower.Contains("shoulder"))
                        {
                            boneList.Add(t);
                        }
                    }
                }

                // Last fallback
                if (boneList.Count == 0)
                {
                    boneList.AddRange(cloneGO.GetComponentsInChildren<Transform>());
                }
            }

            bones = boneList.ToArray();
            Log($"Clone {type} bones cached: {bones.Length}");
            
            // Apply visuals
            ApplyCloneVisuals(cloneGO, type);
            
            // Add CloneMarker for identification by portal system
            var marker = cloneGO.GetComponent<CloneMarker>();
            if (marker == null)
            {
                marker = cloneGO.AddComponent<CloneMarker>();
            }
            marker.cloneType = type;

            // Add CapsuleCollider for sensor raycast detection (shadow casting)
            var existingCollider = cloneGO.GetComponent<CapsuleCollider>();
            if (existingCollider == null)
            {
                var collider = cloneGO.AddComponent<CapsuleCollider>();
                collider.height = 1.8f;
                collider.radius = 0.35f;
                collider.center = new Vector3(0f, 0.9f, 0f);
                collider.isTrigger = false; // Solid collider for raycast shadow detection
            }
            else
            {
                existingCollider.isTrigger = false; // Ensure existing collider is solid
            }

            // Add Rigidbody as kinematic so clone doesn't fall or collide physically
            var rb = cloneGO.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = cloneGO.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;
            rb.useGravity = false;

            // Ensure clone casts shadows for light sensor interaction
            var cloneRenderers = cloneGO.GetComponentsInChildren<Renderer>(true);
            foreach (var rend in cloneRenderers)
            {
                if (rend != null)
                {
                    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    rend.receiveShadows = true;
                }
            }

            // Set clone layer to Default (0) to ensure raycasts can detect it
            // This is important for PuzzleSensor shadow detection
            SetLayerRecursively(cloneGO, 0); // Default layer

            // Add CloneFootstepPlayer for audio during playback
            CloneFootstepPlayer footstepPlayer = null;
            if (enableCloneFootsteps && _footstepDatabase != null)
            {
                footstepPlayer = cloneGO.AddComponent<CloneFootstepPlayer>();
                footstepPlayer.Initialize(_footstepDatabase);
                Log($"Clone {type} footstep player added");
            }

            onComplete?.Invoke(new CloneInstance
            {
                type = type,
                gameObject = cloneGO,
                transform = cloneGO.transform,
                bones = bones,
                animator = animator,
                isPlaying = false,
                footstepPlayer = footstepPlayer,
                nextFootstepIndex = 0
            });
        }
        
        private void Update()
        {
            HandleInput();
            UpdateSuperhotTime();
            UpdateClonePositions();
        }
        
        private void LateUpdate()
        {
            ApplyCloneBones();
        }
        
        // UI rendering moved to CloneSystemUI.cs for modularity
        // Add CloneSystemUI component to the same GameObject
        
        private void OnDisable()
        {
            // Always restore time scale
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
        }
        
        private void OnDestroy()
        {
            // Unsubscribe from footstep events
            if (_playerFootstepSystem != null)
            {
                _playerFootstepSystem.OnFootstep -= OnPlayerFootstep;
                _playerFootstepSystem.OnJumpLand -= OnPlayerJumpLand;
            }

            // Clean up clone materials to prevent memory leaks
            foreach (var clone in _clonePool)
            {
                if (clone?.gameObject != null)
                {
                    var renderers = clone.gameObject.GetComponentsInChildren<Renderer>();
                    foreach (var renderer in renderers)
                    {
                        if (renderer != null)
                        {
                            foreach (var mat in renderer.sharedMaterials)
                            {
                                if (mat != null && mat.name.StartsWith("Clone_"))
                                {
                                    Destroy(mat);
                                }
                            }
                        }
                    }
                    Destroy(clone.gameObject);
                }
            }
            _clonePool.Clear();
            _activeClones.Clear();
            
            // UI cleanup moved to CloneSystemUI.cs
            
            if (Instance == this) Instance = null;
        }
        
        #endregion
        
        #region Input
        
        private void HandleInput()
        {
            // Selection UI toggle
            if (Input.GetKeyDown(selectionKey) && (_currentPhase == Phase.Idle || _currentPhase == Phase.Review))
            {
                _selectionOpen = !_selectionOpen;
                SetInputLocked(_selectionOpen);
            }
            
            // Close selection on any recording action
            if (_selectionOpen)
            {
                // Navigation: A/D keys AND arrow keys
                // Only allow selection of active clone types
                int prevIndex = _selectedIndex;
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
                {
                    // Find previous active clone type
                    int newIndex = (_selectedIndex + 2) % 3;
                    for (int i = 0; i < 3; i++)
                    {
                        if (_cloneTypes[newIndex].isActive) break;
                        newIndex = (newIndex + 2) % 3;
                    }
                    _selectedIndex = newIndex;
                }
                if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
                {
                    // Find next active clone type
                    int newIndex = (_selectedIndex + 1) % 3;
                    for (int i = 0; i < 3; i++)
                    {
                        if (_cloneTypes[newIndex].isActive) break;
                        newIndex = (newIndex + 1) % 3;
                    }
                    _selectedIndex = newIndex;
                }
                // Only allow number keys for active types
                if (Input.GetKeyDown(KeyCode.Alpha1) && _cloneTypes[0].isActive) _selectedIndex = 0;
                if (Input.GetKeyDown(KeyCode.Alpha2) && _cloneTypes[1].isActive) _selectedIndex = 1;
                if (Input.GetKeyDown(KeyCode.Alpha3) && _cloneTypes[2].isActive) _selectedIndex = 2;
                
                // Notify UI of selection change for animation
                if (prevIndex != _selectedIndex) OnSelectionChanged?.Invoke();
                
                // Enter or Space to confirm and close
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
                {
                    _selectionOpen = false;
                    SetInputLocked(false);
                }
                
                return; // Don't process other inputs while selection is open
            }
            
            // Record key
            if (Input.GetKeyDown(recordKey))
            {
                if (_currentPhase == Phase.Idle || _currentPhase == Phase.Review)
                {
                    if (!_recordings.ContainsKey(SelectedCloneType))
                    {
                        StartRecording();
                    }
                    else
                    {
                        Log($"{SelectedCloneType} already recorded! Use TAB to select another.");
                    }
                }
                else if (_currentPhase == Phase.Recording)
                {
                    StopRecording();
                }
            }
            
            // Space key - ONLY handle in Review and Playback phases
            // During Recording/Rewinding/Idle - let the game handle SPACE (jump)
            if (_currentPhase == Phase.Review || _currentPhase == Phase.Playback)
            {
                if (Input.GetKeyDown(playbackKey) && !_selectionOpen)
                {
                    if (_currentPhase == Phase.Review && _recordings.Count > 0)
                    {
                        StartPlayback();
                    }
                    else if (_currentPhase == Phase.Playback)
                    {
                        _clonesPaused = !_clonesPaused;

                        // Stop footstep audio when paused
                        if (_clonesPaused)
                        {
                            foreach (var clone in _activeClones)
                            {
                                if (clone.footstepPlayer != null)
                                {
                                    clone.footstepPlayer.StopAudio();
                                }
                            }
                        }

                        Log(_clonesPaused ? "PAUSED" : "RESUMED");
                    }
                }
            }
        }
        
        private void SetInputLocked(bool locked)
        {
            // Lock/unlock cursor
            Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = locked;
            
            // Disable/enable player input
            if (_playerInput != null)
            {
                if (locked) _playerInput.DeactivateInput();
                else _playerInput.ActivateInput();
            }
            
            if (_movementController != null)
            {
                _movementController.enabled = !locked;
            }
        }
        
        #endregion
        
        #region SUPERHOT Time
        
        private void UpdateSuperhotTime()
        {
            // CRITICAL: Don't touch timeScale if game is paused (menu is open)
            if (Menu.Pause.PauseMenuState.Instance != null && Menu.Pause.PauseMenuState.Instance.IsPaused)
            {
                return; // Let PauseMenuManager control Time.timeScale
            }
            
            // CRITICAL: Force normal time in ALL phases except Playback
            if (_currentPhase != Phase.Playback || _clonesPaused)
            {
                // ALWAYS force time to 1.0 outside of active playback
                Time.timeScale = 1f;
                Time.fixedDeltaTime = 0.02f;
                _targetTimeScale = 1f;
                return;
            }
            
            // Calculate time scale based on active clone types
            float baseTimeScale = normalTimeScale;
            float minSlowMo = slowMotionTimeScale;
            
            // Check each active clone type and apply its effect
            foreach (CloneType cloneType in System.Enum.GetValues(typeof(CloneType)))
            {
                if (!_recordings.ContainsKey(cloneType)) continue;
                
                var typeData = _cloneTypes[(int)cloneType];
                
                switch (cloneType)
                {
                    case CloneType.Routine:
                        // ROUTINE: SUPERHOT-style - slow when idle, normal when moving
                        Vector3 currentPos = _playerRoot != null ? _playerRoot.position : Vector3.zero;
                        float movementSpeed = (currentPos - _lastPlayerPos).magnitude / Mathf.Max(0.001f, Time.unscaledDeltaTime);
                        _lastPlayerPos = currentPos;
                        
                        bool hasKeyInput = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || 
                                           Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D) ||
                                           Input.GetMouseButton(0) || Input.GetMouseButton(1);
                        
                        float mouseMove = Mathf.Abs(Input.GetAxis("Mouse X")) + Mathf.Abs(Input.GetAxis("Mouse Y"));
                        
                        _isPlayerMoving = movementSpeed > movementThreshold || hasKeyInput || mouseMove > 0.1f;
                        _targetTimeScale = _isPlayerMoving ? normalTimeScale : typeData.timeScaleMultiplier;
                        break;
                        
                    case CloneType.Fear:
                        // FEAR: Always partial slow-mo, stacks with other effects
                        _targetTimeScale = Mathf.Min(_targetTimeScale, typeData.timeScaleMultiplier);
                        break;
                        
                    case CloneType.Brave:
                        // BRAVE: Speed boost - multiplies current time scale
                        _targetTimeScale = Mathf.Min(_targetTimeScale * typeData.timeScaleMultiplier, 1.5f);
                        break;
                }
            }
            
            // Fallback if no recordings
            if (_recordings.Count == 0)
            {
                _targetTimeScale = normalTimeScale;
            }
            
            // Smooth transition
            float newScale = Mathf.Lerp(Time.timeScale, _targetTimeScale, Time.unscaledDeltaTime * timeTransitionSpeed);
            Time.timeScale = Mathf.Clamp(newScale, 0.05f, 1.5f); // Allow up to 1.5x for Brave
            Time.fixedDeltaTime = 0.02f * Time.timeScale;
        }
        
        #endregion
        
        #region Recording
        
        private void StartRecording()
        {
            _currentRecording = new Recording { type = SelectedCloneType };
            _recordStartPos = _playerRoot.position;
            _recordStartRot = _playerRoot.rotation;
            _recordingStartTime = Time.unscaledTime;

            SetPhase(Phase.Recording);
            StartCoroutine(RecordingLoop());

            Log($"Recording {SelectedCloneType}...");
        }
        
        private IEnumerator RecordingLoop()
        {
            float startTime = Time.unscaledTime;
            float recordInterval = 1f / recordingFPS;
            float accumulatedTime = 0f;

            // For keyframe compression
            Vector3 lastPos = _playerRoot.position;
            Quaternion lastRot = _playerRoot.rotation;
            float lastKeyframeTime = 0f;
            const float forceKeyframeInterval = 0.5f; // Force keyframe every 0.5 seconds

            while (_currentPhase == Phase.Recording)
            {
                float elapsed = Time.unscaledTime - startTime;

                if (elapsed >= maxRecordDuration)
                {
                    StopRecording();
                    yield break;
                }

                accumulatedTime += Time.unscaledDeltaTime;
                if (accumulatedTime >= recordInterval)
                {
                    accumulatedTime -= recordInterval;

                    Vector3 currentPos = _playerRoot.position;
                    Quaternion currentRot = _playerRoot.rotation;

                    // Keyframe compression: only record if significant change or forced interval
                    bool shouldRecord = true;
                    if (useKeyframeCompression && _currentRecording.frames.Count > 0)
                    {
                        float posDelta = Vector3.Distance(currentPos, lastPos);
                        float rotDelta = Quaternion.Angle(currentRot, lastRot);
                        float timeSinceLastKeyframe = elapsed - lastKeyframeTime;

                        // Skip frame if no significant change (unless forced interval)
                        shouldRecord = posDelta > positionThreshold ||
                                      rotDelta > rotationThreshold ||
                                      timeSinceLastKeyframe >= forceKeyframeInterval;
                    }

                    if (shouldRecord)
                    {
                        var frame = new Frame
                        {
                            time = elapsed,
                            position = currentPos,
                            rotation = currentRot,
                            bones = CaptureBones()
                        };

                        _currentRecording.frames.Add(frame);
                        lastPos = currentPos;
                        lastRot = currentRot;
                        lastKeyframeTime = elapsed;
                    }
                }

                yield return null;
            }
        }
        
        // Pre-allocated buffer pool for bone capture (eliminates GC allocation)
        private BoneData[] _boneDataBuffer;
        private Queue<BoneData[]> _boneDataPool = new Queue<BoneData[]>();
        private const int BONE_POOL_SIZE = 500; // Pre-allocate for ~5 seconds at 100fps
        private bool _bonePoolInitialized = false;

        private void InitializeBonePool()
        {
            if (_bonePoolInitialized || _playerBones == null) return;

            for (int i = 0; i < BONE_POOL_SIZE; i++)
            {
                _boneDataPool.Enqueue(new BoneData[_playerBones.Length]);
            }
            _bonePoolInitialized = true;
            Log($"Bone pool initialized: {BONE_POOL_SIZE} buffers");
        }

        private BoneData[] GetBoneBufferFromPool()
        {
            if (_boneDataPool.Count > 0)
                return _boneDataPool.Dequeue();

            // Fallback allocation if pool exhausted
            return new BoneData[_playerBones?.Length ?? 0];
        }

        private void ReturnBoneBufferToPool(BoneData[] buffer)
        {
            if (buffer != null && _boneDataPool.Count < BONE_POOL_SIZE * 2)
                _boneDataPool.Enqueue(buffer);
        }

        private BoneData[] CaptureBones()
        {
            if (_playerBones == null) return null;

            // Initialize pool on first use
            if (!_bonePoolInitialized) InitializeBonePool();

            // Get buffer from pool instead of allocating new
            var result = GetBoneBufferFromPool();
            if (result.Length != _playerBones.Length)
                result = new BoneData[_playerBones.Length];

            for (int i = 0; i < _playerBones.Length; i++)
            {
                if (_playerBones[i] != null)
                {
                    result[i].localPos = _playerBones[i].localPosition;
                    result[i].localRot = _playerBones[i].localRotation;
                }
            }

            return result;
        }

        private void OnPlayerFootstep(SurfaceType surface, bool isRunning)
        {
            if (_currentPhase != Phase.Recording || _currentRecording == null) return;

            float eventTime = Time.unscaledTime - _recordingStartTime;
            _currentRecording.footstepEvents.Add(new FootstepEvent(eventTime, surface, isRunning, false));
        }

        private void OnPlayerJumpLand(SurfaceType surface)
        {
            if (_currentPhase != Phase.Recording || _currentRecording == null) return;

            float eventTime = Time.unscaledTime - _recordingStartTime;
            _currentRecording.footstepEvents.Add(new FootstepEvent(eventTime, surface, false, true));
        }

        private void StopRecording()
        {
            if (_currentRecording != null && _currentRecording.frames.Count > 0)
            {
                _recordings[SelectedCloneType] = _currentRecording;
                Log($"{SelectedCloneType} saved: {_currentRecording.frames.Count} frames, {_currentRecording.footstepEvents?.Count ?? 0} footsteps, {_currentRecording.duration:F1}s");
            }
            
            _currentRecording = null;
            SetPhase(Phase.Rewinding);
            StartCoroutine(RewindSequence());
        }
        
        #endregion
        
        #region Rewind
        
        private IEnumerator RewindSequence()
        {
            Log("Rewinding...");
            
            // CRITICAL: Ensure normal time during rewind
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
            
            var recording = _recordings[SelectedCloneType];
            float rewindDuration = recording.duration / rewindSpeed;
            float elapsed = 0f;
            
            SetMovementEnabled(false);
            if (_characterController != null) _characterController.enabled = false;
            
            while (elapsed < rewindDuration && _currentPhase == Phase.Rewinding)
            {
                float t = elapsed / rewindDuration;
                float frameTime = recording.duration * (1f - t);
                var frame = recording.GetFrameAtTime(frameTime);
                
                if (frame != null)
                {
                    _playerRoot.position = frame.position;
                    _playerRoot.rotation = frame.rotation;
                }
                
                elapsed += Time.unscaledDeltaTime; // Use unscaled!
                yield return null;
            }
            
            // Final position
            _playerRoot.position = _recordStartPos;
            _playerRoot.rotation = _recordStartRot;
            Physics.SyncTransforms();
            
            if (_characterController != null) _characterController.enabled = true;
            
            yield return new WaitForSecondsRealtime(0.1f);
            
            // Soft reset animator - Do NOT use Rebind() with KINEMATION!
            // Rebind() destroys TransformStreamHandles used by the playable graph.
            if (_playerAnimator != null)
            {
                _playerAnimator.enabled = false;
                _playerAnimator.enabled = true;
                _playerAnimator.Update(0f);
            }
            
            SetMovementEnabled(true);
            
            Log("Rewind complete");
            SetPhase(Phase.Review);
        }
        
        private void SetMovementEnabled(bool enabled)
        {
            if (_movementController != null)
                _movementController.enabled = enabled;
            
            if (_playerInput != null)
            {
                if (enabled) _playerInput.ActivateInput();
                else _playerInput.DeactivateInput();
            }
        }
        
        #endregion
        
        #region Playback
        
        private void StartPlayback()
        {
            if (!_poolReady)
            {
                Log("Clone pool not ready!");
                return;
            }

            SetPhase(Phase.Playback);
            _clonesPaused = false;
            _lastPlayerPos = _playerRoot.position;
            _debugBoneLogOnce = false; // Reset debug flag for new playback
            
            // Activate clones from pool
            int activated = 0;
            foreach (var kvp in _recordings)
            {
                var clone = GetCloneFromPool(kvp.Key);
                if (clone != null)
                {
                    clone.recording = kvp.Value;
                    clone.playbackTime = 0f;
                    clone.isPlaying = true;

                    // Set initial position
                    if (kvp.Value.frames.Count > 0)
                    {
                        clone.transform.position = kvp.Value.frames[0].position;
                        clone.transform.rotation = kvp.Value.frames[0].rotation;
                    }

                    // Keep animator DISABLED - we control bones manually
                    // Enabling animator with speed=0 still interferes with bone transforms
                    if (clone.animator != null)
                    {
                        clone.animator.enabled = false;
                    }

                    // Force initial bone pose from first frame
                    if (kvp.Value.frames.Count > 0 && clone.bones != null)
                    {
                        var firstFrame = kvp.Value.frames[0];
                        if (firstFrame.bones != null)
                        {
                            int boneCount = Mathf.Min(clone.bones.Length, firstFrame.bones.Length);
                            for (int b = 0; b < boneCount; b++)
                            {
                                if (clone.bones[b] != null)
                                {
                                    clone.bones[b].localPosition = firstFrame.bones[b].localPos;
                                    clone.bones[b].localRotation = firstFrame.bones[b].localRot;
                                }
                            }
                        }
                    }

                    // Reset footstep playback index
                    clone.nextFootstepIndex = 0;

                    clone.gameObject.SetActive(true);
                    _activeClones.Add(clone);
                    activated++;
                }
            }
            
            Log($"Playback: {activated} clones");
        }
        
        private CloneInstance GetCloneFromPool(CloneType type)
        {
            foreach (var clone in _clonePool)
            {
                if (clone.type == type)
                    return clone;
            }
            return null;
        }
        
        private void RemoveUnwantedComponentsSafe(GameObject go)
        {
            // CRITICAL FIX for fidA != fidB:
            // First, find and destroy any GameObjects that contain Camera components
            // This is safer than trying to remove individual components from HDRP
            var cameras = go.GetComponentsInChildren<Camera>(true);
            foreach (var cam in cameras)
            {
                if (cam != null && cam.gameObject != go)
                {
                    // Destroy the entire camera GameObject to avoid HDRP internal ID issues
                    try
                    {
                        Destroy(cam.gameObject);
                        Log($"Destroyed camera child: {cam.gameObject.name}");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[AAAClone] Could not destroy camera GO: {e.Message}");
                    }
                }
            }
            
            // IMPORTANT: Order matters! Remove dependent components BEFORE their dependencies
            // HDAdditionalCameraData depends on Camera, so remove it first
            var typesToRemove = new string[]
            {
                // HDRP components that depend on Camera - must be removed first
                "HDAdditionalCameraData", "UniversalAdditionalCameraData",
                // Now safe to remove Camera and AudioListener
                "CharacterController", "PlayerInput", "Camera", "AudioListener",
                "CharacterExampleController", "FPSExampleController",
                "CharacterAnimationComponent", "ProceduralAnimationComponent",
                "CharacterCamera", "RecoilAnimation",
                // Additional KINEMATION components that may cause issues
                "FPSMovementSettings", "InputManager", "PlayablesController"
            };
            
            // Collect ALL components to destroy first
            var toDestroy = new List<Component>();
            
            foreach (var typeName in typesToRemove)
            {
                var components = go.GetComponentsInChildren<Component>(true);
                foreach (var comp in components)
                {
                    if (comp != null && comp.GetType().Name.Contains(typeName) && !toDestroy.Contains(comp))
                    {
                        toDestroy.Add(comp);
                    }
                }
            }
            
            // Destroy using Destroy() instead of DestroyImmediate() to avoid fidA != fidB error
            // Use try-catch to handle any HDRP internal errors gracefully
            foreach (var comp in toDestroy)
            {
                if (comp != null)
                {
                    try
                    {
                        // For HDRP camera-related components, set inactive first
                        if (comp.GetType().Name.Contains("Camera"))
                        {
                            comp.gameObject.SetActive(false);
                        }
                        Destroy(comp);
                    }
                    catch (System.Exception e)
                    {
                        // Suppress HDRP internal errors - they don't affect functionality
                        if (!e.Message.Contains("fidA"))
                        {
                            Debug.LogWarning($"[AAAClone] Could not destroy {comp.GetType().Name}: {e.Message}");
                        }
                    }
                }
            }
        }
        
        private void ApplyCloneVisuals(GameObject go, CloneType type)
        {
            var typeData = _cloneTypes[(int)type];
            var renderers = go.GetComponentsInChildren<Renderer>();

            // Find HDRP Lit shader
            Shader hdrpLit = Shader.Find("HDRP/Lit");
            if (hdrpLit == null)
            {
                hdrpLit = Shader.Find("Universal Render Pipeline/Lit");
            }
            if (hdrpLit == null)
            {
                hdrpLit = Shader.Find("Standard");
            }

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;

                // Create new materials for each renderer
                var originalMats = renderer.sharedMaterials;
                var newMats = new Material[originalMats.Length];

                for (int i = 0; i < originalMats.Length; i++)
                {
                    Material mat;

                    if (originalMats[i] != null)
                    {
                        mat = new Material(originalMats[i]);
                    }
                    else if (hdrpLit != null)
                    {
                        mat = new Material(hdrpLit);
                    }
                    else
                    {
                        continue;
                    }

                    mat.name = $"Clone_{type}_Mat_{i}";

                    // === OPAQUE with STRONG EMISSION (Works in HDRP!) ===

                    // Keep it OPAQUE (more reliable in HDRP)
                    if (mat.HasProperty("_SurfaceType"))
                        mat.SetFloat("_SurfaceType", 0); // Opaque

                    // Dark base color (so emission stands out)
                    Color darkBase = typeData.darkColor;
                    darkBase.a = 1f;

                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", darkBase);
                    if (mat.HasProperty("_Color"))
                        mat.SetColor("_Color", darkBase);

                    // STRONG EMISSION - This is what makes the clone visible!
                    if (mat.HasProperty("_EmissiveColor"))
                    {
                        // Bright emission color
                        Color emissive = typeData.color * 2.5f; // Bright glow
                        mat.SetColor("_EmissiveColor", emissive);
                        mat.EnableKeyword("_EMISSION");

                        // HDRP specific emission settings
                        if (mat.HasProperty("_EmissiveIntensity"))
                            mat.SetFloat("_EmissiveIntensity", 3f);
                        if (mat.HasProperty("_EmissiveExposureWeight"))
                            mat.SetFloat("_EmissiveExposureWeight", 0.5f);
                        if (mat.HasProperty("_UseEmissiveIntensity"))
                            mat.SetFloat("_UseEmissiveIntensity", 1f);
                    }

                    // Metallic/Smoothness for stylized look
                    if (mat.HasProperty("_Metallic"))
                        mat.SetFloat("_Metallic", 0.8f);
                    if (mat.HasProperty("_Smoothness"))
                        mat.SetFloat("_Smoothness", 0.9f);

                    newMats[i] = mat;
                }

                renderer.sharedMaterials = newMats;

                // Also make sure renderer is enabled and visible
                renderer.enabled = true;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            Log($"Applied EMISSIVE visuals to clone: {type}");
        }
        
        private void UpdateClonePositions()
        {
            if (_currentPhase != Phase.Playback) return;
            
            bool anyPlaying = false;
            
            // CRITICAL: Clones should play at REAL TIME speed, not affected by slow-mo
            // This way, when player stops (slow-mo), clones continue at normal speed
            float dt = _clonesPaused ? 0 : Time.unscaledDeltaTime;
            
            foreach (var clone in _activeClones)
            {
                if (!clone.isPlaying) continue;
                
                clone.playbackTime += dt;
                
                if (clone.playbackTime >= clone.recording.duration)
                {
                    clone.isPlaying = false;
                    // Stop footstep audio when clone finishes
                    if (clone.footstepPlayer != null)
                    {
                        clone.footstepPlayer.StopAudio();
                    }
                    clone.gameObject.SetActive(false);
                    continue;
                }
                
                anyPlaying = true;
                
                clone.currentFrame = clone.recording.GetFrameAtTime(clone.playbackTime);
                
                if (clone.currentFrame != null)
                {
                    clone.transform.position = clone.currentFrame.position;
                    clone.transform.rotation = clone.currentFrame.rotation;
                }

                // Play footstep sounds
                if (enableCloneFootsteps && clone.footstepPlayer != null && clone.recording.footstepEvents != null)
                {
                    PlayCloneFootsteps(clone);
                }
            }

            if (!anyPlaying)
            {
                EndPlayback();
            }
        }
        
        private int _lodFrameCounter = 0;
        private bool _debugBoneLogOnce = false;

        private void ApplyCloneBones()
        {
            if (_currentPhase != Phase.Playback) return;

            _lodFrameCounter++;

            // Debug log once per playback
            if (!_debugBoneLogOnce && _activeClones.Count > 0)
            {
                var clone = _activeClones[0];
                Log($"ApplyCloneBones - Clone bones: {clone.bones?.Length ?? 0}, Frame bones: {clone.currentFrame?.bones?.Length ?? 0}, IsPlaying: {clone.isPlaying}");
                _debugBoneLogOnce = true;
            }

            foreach (var clone in _activeClones)
            {
                if (!clone.isPlaying || clone.currentFrame == null || clone.bones == null) continue;

                // Calculate LOD based on distance to player
                if (useLOD && _playerRoot != null)
                {
                    clone.distanceToPlayer = Vector3.Distance(clone.transform.position, _playerRoot.position);

                    if (clone.distanceToPlayer > lodDistance2)
                    {
                        clone.currentLOD = 2;
                        clone.boneUpdateSkip = 4; // Update every 4 frames
                    }
                    else if (clone.distanceToPlayer > lodDistance1)
                    {
                        clone.currentLOD = 1;
                        clone.boneUpdateSkip = 2; // Update every 2 frames
                    }
                    else
                    {
                        clone.currentLOD = 0;
                        clone.boneUpdateSkip = 1; // Update every frame
                    }

                    // Skip bone update based on LOD
                    if (_lodFrameCounter % clone.boneUpdateSkip != 0) continue;
                }

                var bones = clone.currentFrame.bones;
                if (bones == null) continue;

                int count = Mathf.Min(clone.bones.Length, bones.Length);

                // LOD 2: Only update major bones (every 4th bone)
                int step = clone.currentLOD == 2 ? 4 : (clone.currentLOD == 1 ? 2 : 1);

                for (int i = 0; i < count; i += step)
                {
                    if (clone.bones[i] != null)
                    {
                        clone.bones[i].localPosition = bones[i].localPos;
                        clone.bones[i].localRotation = bones[i].localRot;
                    }
                }

                // Always update spine and head for visual consistency
                if (step > 1)
                {
                    int[] criticalBones = { 0, 1, 2, 7, 8, 9, 10 }; // Hips, Spine, Chest, Neck, Head, etc.
                    foreach (int idx in criticalBones)
                    {
                        if (idx < count && clone.bones[idx] != null)
                        {
                            clone.bones[idx].localPosition = bones[idx].localPos;
                            clone.bones[idx].localRotation = bones[idx].localRot;
                        }
                    }
                }
            }
        }
        
        private void EndPlayback()
        {
            // Return bone buffers to pool before clearing recordings
            foreach (var kvp in _recordings)
            {
                var recording = kvp.Value;
                if (recording?.frames != null)
                {
                    foreach (var frame in recording.frames)
                    {
                        if (frame?.bones != null)
                        {
                            ReturnBoneBufferToPool(frame.bones);
                        }
                    }
                }
            }

            // Return clones to pool (don't destroy)
            foreach (var clone in _activeClones)
            {
                clone.isPlaying = false;
                clone.recording = null;
                clone.currentLOD = 0;
                clone.boneUpdateSkip = 1;
                clone.nextFootstepIndex = 0; // Reset footstep playback

                // Stop footstep audio
                if (clone.footstepPlayer != null)
                {
                    clone.footstepPlayer.StopAudio();
                }

                // Disable animator when returning to pool
                if (clone.animator != null)
                {
                    clone.animator.enabled = false;
                }

                clone.gameObject.SetActive(false);
            }
            _activeClones.Clear();
            _recordings.Clear();
            
            // Restore time
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
            
            SetPhase(Phase.Idle);
            Log("Playback complete");
        }
        
        #endregion
        
        // UI region moved to CloneSystemUI.cs for modularity and performance
        // See CloneSystemUI.cs for all OnGUI rendering code
        // To use: Add CloneSystemUI component to the same GameObject as AAACloneSystem
        
        #region Utilities
        
        private void SetPhase(Phase newPhase)
        {
            if (_currentPhase == newPhase) return;
            Log($"Phase: {_currentPhase} -> {newPhase}");
            Phase oldPhase = _currentPhase;
            _currentPhase = newPhase;
            OnPhaseChanged?.Invoke(newPhase);
            
            // Fire GameEvents for cross-system communication
            GameEvents.InvokeClonePhaseChanged((int)newPhase);
            
            // Fire specific events based on phase transitions
            switch (newPhase)
            {
                case Phase.Recording:
                    GameEvents.InvokeRecordingStarted();
                    break;
                case Phase.Rewinding:
                    if (oldPhase == Phase.Recording)
                        GameEvents.InvokeRecordingStopped();
                    GameEvents.InvokeRewindStarted();
                    break;
                case Phase.Review:
                    if (oldPhase == Phase.Rewinding)
                        GameEvents.InvokeRewindCompleted();
                    break;
                case Phase.Playback:
                    GameEvents.InvokePlaybackStarted();
                    GameEvents.InvokeSlowMotionStarted();
                    break;
                case Phase.Idle:
                    if (oldPhase == Phase.Playback)
                    {
                        GameEvents.InvokePlaybackEnded();
                        GameEvents.InvokeSlowMotionEnded();
                    }
                    break;
            }
        }
        
        private void Log(string msg)
        {
            if (showDebugLogs)
                Debug.Log($"[AAAClone] {msg}");
        }

        /// <summary>
        /// Play footstep sounds for a clone based on recorded events
        /// </summary>
        private void PlayCloneFootsteps(CloneInstance clone)
        {
            var events = clone.recording.footstepEvents;
            if (events == null || events.Count == 0) return;

            // Play all footstep events that should have played by now
            while (clone.nextFootstepIndex < events.Count)
            {
                var evt = events[clone.nextFootstepIndex];

                if (evt.time <= clone.playbackTime)
                {
                    // Play this footstep
                    if (evt.isJump)
                    {
                        clone.footstepPlayer.PlayJump(evt.surface);
                    }
                    else
                    {
                        clone.footstepPlayer.PlayFootstep(evt.surface, evt.isRunning);
                    }

                    clone.nextFootstepIndex++;
                }
                else
                {
                    // Future event, stop checking
                    break;
                }
            }
        }

        /// <summary>
        /// Recursively sets the layer for a GameObject and all its children.
        /// Important for raycast-based shadow detection in PuzzleSensor.
        /// </summary>
        private void SetLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null) return;
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        #endregion
    }
}
