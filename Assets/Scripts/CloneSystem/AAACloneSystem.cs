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
    // SUPERHOT-style clone recording/playback system
    // records player movement then plays it back as a "ghost" clone
    public class AAACloneSystem : MonoBehaviour
    {
        public static AAACloneSystem Instance { get; private set; }

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

            public float timeScaleMultiplier = 1f;
            public float distortionStrength = 0f;
            public bool affectsEnemyAI = false;
            public bool isActive = true;
        }

        private readonly CloneTypeData[] _cloneTypes = new CloneTypeData[]
        {
            new CloneTypeData {
                displayName = "clone_routine",
                description = "clone_routine_desc",
                color = new Color(0.2f, 0.6f, 1f, 1f),
                darkColor = new Color(0.1f, 0.3f, 0.5f, 1f),
                timeScaleMultiplier = 0.1f,
                distortionStrength = 0f,
                affectsEnemyAI = false,
                isActive = true
            },
            new CloneTypeData {
                displayName = "clone_fear",
                description = "clone_fear_desc",
                color = new Color(0.7f, 0.2f, 1f, 1f),
                darkColor = new Color(0.35f, 0.1f, 0.5f, 1f),
                timeScaleMultiplier = 0.5f,
                distortionStrength = 0.3f,
                affectsEnemyAI = true,
                isActive = false  // not implemented yet
            },
            new CloneTypeData {
                displayName = "clone_brave",
                description = "clone_brave_desc",
                color = new Color(1f, 0.4f, 0.1f, 1f),
                darkColor = new Color(0.5f, 0.2f, 0.05f, 1f),
                timeScaleMultiplier = 1.2f,
                distortionStrength = 0.1f,
                affectsEnemyAI = false,
                isActive = false  // not implemented yet
            }
        };

        [Header("Clone Settings")]
        [SerializeField] private float maxRecordDuration = 5f;
        [SerializeField] private float rewindSpeed = 3f;
        [SerializeField] private int recordingFPS = 60;

        [Header("Optimization")]
        [SerializeField] private bool useKeyframeCompression = true;
        [SerializeField] private float positionThreshold = 0.001f;
        [SerializeField] private float rotationThreshold = 0.1f;

        [Header("Clone LOD")]
        [SerializeField] private bool useLOD = true;
        [SerializeField] private float lodDistance1 = 10f;
        [SerializeField] private float lodDistance2 = 25f;

        [Header("SUPERHOT Time")]
        [SerializeField] private float slowMotionTimeScale = 0.1f;
        [SerializeField] private float normalTimeScale = 1f;
        [SerializeField] private float timeTransitionSpeed = 10f;
        [SerializeField] private float movementThreshold = 0.05f;

        [Header("Input")]
        [SerializeField] private KeyCode recordKey = KeyCode.R;
        [SerializeField] private KeyCode playbackKey = KeyCode.Space;
        [SerializeField] private KeyCode selectionKey = KeyCode.Tab;

        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = false;

        [Header("Footstep Audio")]
        [SerializeField] private FootstepDatabase _footstepDatabase;
        [SerializeField] private bool enableCloneFootsteps = true;
        // volume is read by CloneFootstepPlayer at runtime - warning suppressed intentionally
        [SerializeField] [Range(0f, 1f)] private float cloneFootstepVolume = 0.4f;

        public enum Phase { Idle, Recording, Rewinding, Review, Playback }
        private Phase _currentPhase = Phase.Idle;
        public Phase CurrentPhase => _currentPhase;

        private int _selectedIndex = 0;
        public CloneType SelectedCloneType => (CloneType)_selectedIndex;
        public int SelectedIndex => _selectedIndex;

        private bool _selectionOpen = false;
        public bool IsSelectionOpen => _selectionOpen;

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

        public event Action OnSelectionChanged;

        private Dictionary<CloneType, Recording> _recordings = new Dictionary<CloneType, Recording>();
        private Recording _currentRecording;
        private Vector3 _recordStartPos;
        private Quaternion _recordStartRot;

        private Transform _playerRoot;
        private Transform _playerModel;
        private Animator _playerAnimator;
        private CharacterController _characterController;
        private MonoBehaviour _movementController;
        private PlayerInput _playerInput;
        private Transform[] _playerBones;

        private float _targetTimeScale = 1f;
        private Vector3 _lastPlayerPos;
        private bool _isPlayerMoving;

        // pre-pool clones at startup to avoid hitches during gameplay
        private List<CloneInstance> _clonePool = new List<CloneInstance>();
        private List<CloneInstance> _activeClones = new List<CloneInstance>();
        private bool _clonesPaused = false;
        private bool _poolReady = false;

        private AdvancedFootstepSystem _playerFootstepSystem;
        private float _recordingStartTime;

        public event Action<Phase> OnPhaseChanged;

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

            // reuse these every frame to avoid GC allocs
            private Frame _cachedFrame;
            private BoneData[] _boneBuffer;

            // smoothing state for natural-feeling playback
            private Vector3 _smoothedPos;
            private Quaternion _smoothedRot;
            private Vector3 _posVelocity;
            private float _lastT = -1f;

            // Catmull-Rom spline interpolation - looks way better than plain Lerp
            // learned about this from a Unity forum post on smooth camera movement
            public Frame GetFrameAtTime(float t)
            {
                if (frames.Count == 0) return null;
                if (t <= 0) return frames[0];
                if (t >= duration) return frames[frames.Count - 1];

                // need 4 points for Catmull-Rom: p0, p1, p2, p3
                int frameIndex = 0;
                for (int i = 0; i < frames.Count - 1; i++)
                {
                    if (frames[i + 1].time > t)
                    {
                        frameIndex = i;
                        break;
                    }
                }

                int p0 = Mathf.Max(0, frameIndex - 1);
                int p1 = frameIndex;
                int p2 = Mathf.Min(frames.Count - 1, frameIndex + 1);
                int p3 = Mathf.Min(frames.Count - 1, frameIndex + 2);

                float segmentDuration = frames[p2].time - frames[p1].time;
                float localT = segmentDuration > 0.001f ? (t - frames[p1].time) / segmentDuration : 0f;

                localT = SmoothStepEasing(localT);

                if (_cachedFrame == null)
                    _cachedFrame = new Frame();

                _cachedFrame.time = t;

                _cachedFrame.position = CatmullRom(
                    frames[p0].position,
                    frames[p1].position,
                    frames[p2].position,
                    frames[p3].position,
                    localT
                );

                // extra damping on rotation prevents snapping
                Quaternion baseRot = Quaternion.Slerp(frames[p1].rotation, frames[p2].rotation, localT);
                _cachedFrame.rotation = Quaternion.Slerp(_smoothedRot, baseRot, 0.2f);
                _smoothedRot = _cachedFrame.rotation;

                // SmoothDamp for buttery position
                if (_lastT >= 0 && Mathf.Abs(t - _lastT) < 0.1f)
                {
                    _cachedFrame.position = Vector3.SmoothDamp(
                        _smoothedPos,
                        _cachedFrame.position,
                        ref _posVelocity,
                        0.03f,
                        float.MaxValue,
                        Time.unscaledDeltaTime
                    );
                }
                _smoothedPos = _cachedFrame.position;
                _lastT = t;

                _cachedFrame.bones = InterpolateBonesSmooth(
                    frames[p1].bones,
                    frames[p2].bones,
                    localT
                );

                return _cachedFrame;
            }

            // standard Catmull-Rom formula
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

            // Hermite smoothstep: 3t² - 2t³ - gives natural ease in/out
            private float SmoothStepEasing(float t)
            {
                return t * t * (3f - 2f * t);
            }

            private BoneData[] InterpolateBonesSmooth(BoneData[] a, BoneData[] b, float t)
            {
                if (a == null || b == null) return a ?? b;
                if (a.Length != b.Length) return t < 0.5f ? a : b;

                // allocate once, reuse forever
                if (_boneBuffer == null || _boneBuffer.Length != a.Length)
                    _boneBuffer = new BoneData[a.Length];

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

            public float distanceToPlayer;
            public int currentLOD;
            public int boneUpdateSkip;

            public CloneFootstepPlayer footstepPlayer;
            public int nextFootstepIndex;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

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

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) player = GameObject.Find(GameConstants.PLAYER_CHARACTER_NAME);

            if (player != null)
            {
                _playerModel = player.transform;
                _playerRoot = player.transform;

                // check root first, then children
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

                // walk up the hierarchy until we find the CharacterController
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

                _movementController = _playerRoot.GetComponentInChildren<FPSExampleController>();

                _playerInput = _playerRoot.GetComponent<PlayerInput>();
                if (_playerInput == null)
                    _playerInput = _playerRoot.GetComponentInChildren<PlayerInput>();

                // try humanoid bones first, fall back to name matching
                if (_playerAnimator != null)
                {
                    var boneList = new List<Transform>();

                    if (_playerAnimator.avatar != null && _playerAnimator.avatar.isHuman)
                    {
                        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                        {
                            var bone = _playerAnimator.GetBoneTransform((HumanBodyBones)i);
                            if (bone != null) boneList.Add(bone);
                        }
                        Log($"Humanoid bones found: {boneList.Count}");
                    }

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

                    // last resort - just grab everything
                    if (boneList.Count == 0)
                    {
                        boneList.AddRange(_playerModel.GetComponentsInChildren<Transform>());
                        Log($"All transforms as bones: {boneList.Count}");
                    }

                    _playerBones = boneList.ToArray();
                }
                else
                {
                    Debug.LogWarning("[CloneSystem] No Animator found on player!");
                }

                _lastPlayerPos = _playerRoot.position;

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

                // auto-find footstep db in editor if not manually assigned
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

                // spread clone creation across frames to avoid a big stutter spike at startup
                yield return StartCoroutine(PrePoolClones());
            }
            else
            {
                Debug.LogError("[CloneSystem] Player not found!");
            }
        }

        private IEnumerator PrePoolClones()
        {
            Log("Pre-pooling clones...");

            for (int i = 0; i < 3; i++)
            {
                if (!_cloneTypes[i].isActive)
                {
                    Log($"Skipping inactive clone type: {_cloneTypes[i].displayName}");
                    continue;
                }

                // create async to avoid the fidA != fidB HDRP error
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

            // disable cameras BEFORE cloning - otherwise HDRP throws fidA != fidB errors
            // took forever to figure this out, turns out HDRP assigns internal IDs on camera enable
            var playerCameras = _playerModel.GetComponentsInChildren<Camera>(true);
            var cameraStates = new bool[playerCameras.Length];
            for (int i = 0; i < playerCameras.Length; i++)
            {
                cameraStates[i] = playerCameras[i].gameObject.activeSelf;
                playerCameras[i].gameObject.SetActive(false);
            }

            // also disable KINEMATION components before clone or it'll crash in TwoBoneIkJob/StepModifierJob Awake
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

            yield return null;

            var cloneGO = Instantiate(_playerModel.gameObject);
            cloneGO.name = $"Clone_{_cloneTypes[(int)type].displayName}";
            cloneGO.transform.SetParent(transform);

            // restore player cameras right away
            for (int i = 0; i < playerCameras.Length; i++)
            {
                if (playerCameras[i] != null)
                    playerCameras[i].gameObject.SetActive(cameraStates[i]);
            }

            foreach (var kvp in casStates)
            {
                if (kvp.Key != null)
                    kvp.Key.enabled = kvp.Value;
            }

            // wait a couple frames before removing components - fidA != fidB is timing-sensitive
            yield return null;
            yield return null;

            RemoveUnwantedComponentsSafe(cloneGO);

            yield return null;
            yield return null;

            var animator = cloneGO.GetComponent<Animator>();
            if (animator == null)
            {
                animator = cloneGO.GetComponentInChildren<Animator>();
            }

            // keep animator disabled - we set bone transforms manually during playback
            if (animator != null)
            {
                animator.enabled = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            Transform[] bones = null;
            var boneList = new List<Transform>();

            if (animator != null)
            {
                if (animator.avatar != null && animator.avatar.isHuman)
                {
                    for (int j = 0; j < (int)HumanBodyBones.LastBone; j++)
                    {
                        var bone = animator.GetBoneTransform((HumanBodyBones)j);
                        if (bone != null) boneList.Add(bone);
                    }
                }

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

                if (boneList.Count == 0)
                {
                    boneList.AddRange(cloneGO.GetComponentsInChildren<Transform>());
                }
            }

            bones = boneList.ToArray();
            Log($"Clone {type} bones cached: {bones.Length}");

            ApplyCloneVisuals(cloneGO, type);

            // CloneMarker is used by the portal system to know this is a clone, not the player
            var marker = cloneGO.GetComponent<CloneMarker>();
            if (marker == null)
            {
                marker = cloneGO.AddComponent<CloneMarker>();
            }
            marker.cloneType = type;

            // need a solid collider for PuzzleSensor shadow raycasts
            var existingCollider = cloneGO.GetComponent<CapsuleCollider>();
            if (existingCollider == null)
            {
                var collider = cloneGO.AddComponent<CapsuleCollider>();
                collider.height = 1.8f;
                collider.radius = 0.35f;
                collider.center = new Vector3(0f, 0.9f, 0f);
                collider.isTrigger = false;
            }
            else
            {
                existingCollider.isTrigger = false;
            }

            // kinematic rigidbody so it doesn't fall through the floor or push the player
            var rb = cloneGO.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = cloneGO.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;
            rb.useGravity = false;

            var cloneRenderers = cloneGO.GetComponentsInChildren<Renderer>(true);
            foreach (var rend in cloneRenderers)
            {
                if (rend != null)
                {
                    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    rend.receiveShadows = true;
                }
            }

            // Default layer so PuzzleSensor raycasts can hit it
            // TODO: probably want a dedicated "Clone" layer to avoid interfering with other raycasts
            SetLayerRecursively(cloneGO, 0);

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

        // UI rendering is handled by CloneSystemUI.cs - add that component to this GameObject

        private void OnDisable()
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
        }

        private void OnDestroy()
        {
            if (_playerFootstepSystem != null)
            {
                _playerFootstepSystem.OnFootstep -= OnPlayerFootstep;
                _playerFootstepSystem.OnJumpLand -= OnPlayerJumpLand;
            }

            // clean up clone materials or they'll leak
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

            if (Instance == this) Instance = null;
        }

        private void HandleInput()
        {
            if (Input.GetKeyDown(selectionKey) && (_currentPhase == Phase.Idle || _currentPhase == Phase.Review))
            {
                _selectionOpen = !_selectionOpen;
                SetInputLocked(_selectionOpen);
            }

            if (_selectionOpen)
            {
                int prevIndex = _selectedIndex;

                // arrow keys and WASD both work for navigation
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
                {
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
                    int newIndex = (_selectedIndex + 1) % 3;
                    for (int i = 0; i < 3; i++)
                    {
                        if (_cloneTypes[newIndex].isActive) break;
                        newIndex = (newIndex + 1) % 3;
                    }
                    _selectedIndex = newIndex;
                }
                if (Input.GetKeyDown(KeyCode.Alpha1) && _cloneTypes[0].isActive) _selectedIndex = 0;
                if (Input.GetKeyDown(KeyCode.Alpha2) && _cloneTypes[1].isActive) _selectedIndex = 1;
                if (Input.GetKeyDown(KeyCode.Alpha3) && _cloneTypes[2].isActive) _selectedIndex = 2;

                if (prevIndex != _selectedIndex) OnSelectionChanged?.Invoke();

                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
                {
                    _selectionOpen = false;
                    SetInputLocked(false);
                }

                return;
            }

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

            // only intercept Space during review/playback - other phases should let the game handle jumping
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
            Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = locked;

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

        private void UpdateSuperhotTime()
        {
            // don't mess with time if the pause menu is open
            if (Menu.Pause.PauseMenuState.Instance != null && Menu.Pause.PauseMenuState.Instance.IsPaused)
            {
                return;
            }

            // force time to 1.0 in all non-playback phases
            if (_currentPhase != Phase.Playback || _clonesPaused)
            {
                Time.timeScale = 1f;
                Time.fixedDeltaTime = 0.02f;
                _targetTimeScale = 1f;
                return;
            }

            float baseTimeScale = normalTimeScale;
            float minSlowMo = slowMotionTimeScale;

            foreach (CloneType cloneType in System.Enum.GetValues(typeof(CloneType)))
            {
                if (!_recordings.ContainsKey(cloneType)) continue;

                var typeData = _cloneTypes[(int)cloneType];

                switch (cloneType)
                {
                    case CloneType.Routine:
                        // classic SUPERHOT behavior: slow when idle, real speed when moving
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
                        // always partial slow-mo, stacks with other types
                        _targetTimeScale = Mathf.Min(_targetTimeScale, typeData.timeScaleMultiplier);
                        break;

                    case CloneType.Brave:
                        // FIXME: clamping at 1.5x feels arbitrary, might need tuning
                        _targetTimeScale = Mathf.Min(_targetTimeScale * typeData.timeScaleMultiplier, 1.5f);
                        break;
                }
            }

            if (_recordings.Count == 0)
            {
                _targetTimeScale = normalTimeScale;
            }

            float newScale = Mathf.Lerp(Time.timeScale, _targetTimeScale, Time.unscaledDeltaTime * timeTransitionSpeed);
            Time.timeScale = Mathf.Clamp(newScale, 0.05f, 1.5f);
            Time.fixedDeltaTime = 0.02f * Time.timeScale;
        }

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

            Vector3 lastPos = _playerRoot.position;
            Quaternion lastRot = _playerRoot.rotation;
            float lastKeyframeTime = 0f;
            const float forceKeyframeInterval = 0.5f;

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

                    bool shouldRecord = true;
                    if (useKeyframeCompression && _currentRecording.frames.Count > 0)
                    {
                        float posDelta = Vector3.Distance(currentPos, lastPos);
                        float rotDelta = Quaternion.Angle(currentRot, lastRot);
                        float timeSinceLastKeyframe = elapsed - lastKeyframeTime;

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

        // pre-allocated bone buffer pool - eliminates per-frame GC allocs during recording
        private BoneData[] _boneDataBuffer;
        private Queue<BoneData[]> _boneDataPool = new Queue<BoneData[]>();
        private const int BONE_POOL_SIZE = 500;
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

            // pool ran out - shouldn't happen often but just in case
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

            if (!_bonePoolInitialized) InitializeBonePool();

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

        private IEnumerator RewindSequence()
        {
            Log("Rewinding...");

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

                elapsed += Time.unscaledDeltaTime; // must use unscaled or rewind is time-scale-dependent
                yield return null;
            }

            _playerRoot.position = _recordStartPos;
            _playerRoot.rotation = _recordStartRot;
            Physics.SyncTransforms();

            if (_characterController != null) _characterController.enabled = true;

            yield return new WaitForSecondsRealtime(0.1f);

            // soft reset - do NOT use Rebind() with KINEMATION, it destroys TransformStreamHandles
            // disable/enable + Update(0f) is the safe way to reset without breaking the playable graph
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
            _debugBoneLogOnce = false;

            int activated = 0;
            foreach (var kvp in _recordings)
            {
                var clone = GetCloneFromPool(kvp.Key);
                if (clone != null)
                {
                    clone.recording = kvp.Value;
                    clone.playbackTime = 0f;
                    clone.isPlaying = true;

                    if (kvp.Value.frames.Count > 0)
                    {
                        clone.transform.position = kvp.Value.frames[0].position;
                        clone.transform.rotation = kvp.Value.frames[0].rotation;
                    }

                    // animator stays disabled - we write bone transforms directly every frame
                    if (clone.animator != null)
                    {
                        clone.animator.enabled = false;
                    }

                    // force T-pose from first frame before enabling the GO
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
            // destroy camera child GOs entirely - safer than removing individual components in HDRP
            // removing just the Camera component leaves HDAdditionalCameraData in a broken state
            var cameras = go.GetComponentsInChildren<Camera>(true);
            foreach (var cam in cameras)
            {
                if (cam != null && cam.gameObject != go)
                {
                    try
                    {
                        Destroy(cam.gameObject);
                        Log($"Destroyed camera child: {cam.gameObject.name}");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[CloneSystem] Could not destroy camera GO: {e.Message}");
                    }
                }
            }

            // order matters - HDAdditionalCameraData must go before Camera
            var typesToRemove = new string[]
            {
                "HDAdditionalCameraData", "UniversalAdditionalCameraData",
                "CharacterController", "PlayerInput", "Camera", "AudioListener",
                "CharacterExampleController", "FPSExampleController",
                "CharacterAnimationComponent", "ProceduralAnimationComponent",
                "CharacterCamera", "RecoilAnimation",
                "FPSMovementSettings", "InputManager", "PlayablesController"
            };

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

            // Destroy() not DestroyImmediate() - immediate causes fidA != fidB in HDRP
            foreach (var comp in toDestroy)
            {
                if (comp != null)
                {
                    try
                    {
                        if (comp.GetType().Name.Contains("Camera"))
                        {
                            comp.gameObject.SetActive(false);
                        }
                        Destroy(comp);
                    }
                    catch (System.Exception e)
                    {
                        // suppress the HDRP internal fidA errors, they don't actually break anything
                        if (!e.Message.Contains("fidA"))
                        {
                            Debug.LogWarning($"[CloneSystem] Could not destroy {comp.GetType().Name}: {e.Message}");
                        }
                    }
                }
            }
        }

        private void ApplyCloneVisuals(GameObject go, CloneType type)
        {
            var typeData = _cloneTypes[(int)type];
            var renderers = go.GetComponentsInChildren<Renderer>();

            // try HDRP/Lit first, then URP, then Standard as fallback
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

                    // opaque + strong emission is more reliable in HDRP than transparency
                    if (mat.HasProperty("_SurfaceType"))
                        mat.SetFloat("_SurfaceType", 0);

                    Color darkBase = typeData.darkColor;
                    darkBase.a = 1f;

                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", darkBase);
                    if (mat.HasProperty("_Color"))
                        mat.SetColor("_Color", darkBase);

                    if (mat.HasProperty("_EmissiveColor"))
                    {
                        Color emissive = typeData.color * 2.5f;
                        mat.SetColor("_EmissiveColor", emissive);
                        mat.EnableKeyword("_EMISSION");

                        if (mat.HasProperty("_EmissiveIntensity"))
                            mat.SetFloat("_EmissiveIntensity", 3f);
                        if (mat.HasProperty("_EmissiveExposureWeight"))
                            mat.SetFloat("_EmissiveExposureWeight", 0.5f);
                        if (mat.HasProperty("_UseEmissiveIntensity"))
                            mat.SetFloat("_UseEmissiveIntensity", 1f);
                    }

                    if (mat.HasProperty("_Metallic"))
                        mat.SetFloat("_Metallic", 0.8f);
                    if (mat.HasProperty("_Smoothness"))
                        mat.SetFloat("_Smoothness", 0.9f);

                    newMats[i] = mat;
                }

                renderer.sharedMaterials = newMats;
                renderer.enabled = true;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            Log($"Applied emissive visuals to clone: {type}");
        }

        private void UpdateClonePositions()
        {
            if (_currentPhase != Phase.Playback) return;

            bool anyPlaying = false;

            // clones run on unscaled time - they keep moving even during slow-mo
            float dt = _clonesPaused ? 0 : Time.unscaledDeltaTime;

            foreach (var clone in _activeClones)
            {
                if (!clone.isPlaying) continue;

                clone.playbackTime += dt;

                if (clone.playbackTime >= clone.recording.duration)
                {
                    clone.isPlaying = false;
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

            // log bone counts once so we can sanity-check them without spamming the console
            if (!_debugBoneLogOnce && _activeClones.Count > 0)
            {
                var clone = _activeClones[0];
                Log($"ApplyCloneBones - Clone bones: {clone.bones?.Length ?? 0}, Frame bones: {clone.currentFrame?.bones?.Length ?? 0}, IsPlaying: {clone.isPlaying}");
                _debugBoneLogOnce = true;
            }

            foreach (var clone in _activeClones)
            {
                if (!clone.isPlaying || clone.currentFrame == null || clone.bones == null) continue;

                if (useLOD && _playerRoot != null)
                {
                    clone.distanceToPlayer = Vector3.Distance(clone.transform.position, _playerRoot.position);

                    if (clone.distanceToPlayer > lodDistance2)
                    {
                        clone.currentLOD = 2;
                        clone.boneUpdateSkip = 4;
                    }
                    else if (clone.distanceToPlayer > lodDistance1)
                    {
                        clone.currentLOD = 1;
                        clone.boneUpdateSkip = 2;
                    }
                    else
                    {
                        clone.currentLOD = 0;
                        clone.boneUpdateSkip = 1;
                    }

                    if (_lodFrameCounter % clone.boneUpdateSkip != 0) continue;
                }

                var bones = clone.currentFrame.bones;
                if (bones == null) continue;

                int count = Mathf.Min(clone.bones.Length, bones.Length);

                int step = clone.currentLOD == 2 ? 4 : (clone.currentLOD == 1 ? 2 : 1);

                for (int i = 0; i < count; i += step)
                {
                    if (clone.bones[i] != null)
                    {
                        clone.bones[i].localPosition = bones[i].localPos;
                        clone.bones[i].localRotation = bones[i].localRot;
                    }
                }

                // always update spine/head even at lower LOD levels so the clone doesn't look broken
                if (step > 1)
                {
                    // TODO: these indices assume humanoid mapping - might break with non-humanoid characters
                    int[] criticalBones = { 0, 1, 2, 7, 8, 9, 10 };
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
            // return bone buffers to pool before clearing
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

            foreach (var clone in _activeClones)
            {
                clone.isPlaying = false;
                clone.recording = null;
                clone.currentLOD = 0;
                clone.boneUpdateSkip = 1;
                clone.nextFootstepIndex = 0;

                if (clone.footstepPlayer != null)
                {
                    clone.footstepPlayer.StopAudio();
                }

                if (clone.animator != null)
                {
                    clone.animator.enabled = false;
                }

                clone.gameObject.SetActive(false);
            }
            _activeClones.Clear();
            _recordings.Clear();

            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;

            SetPhase(Phase.Idle);
            Log("Playback complete");
        }

        private void SetPhase(Phase newPhase)
        {
            if (_currentPhase == newPhase) return;
            Log($"Phase: {_currentPhase} -> {newPhase}");
            Phase oldPhase = _currentPhase;
            _currentPhase = newPhase;
            OnPhaseChanged?.Invoke(newPhase);

            GameEvents.InvokeClonePhaseChanged((int)newPhase);

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
                Debug.Log($"[CloneSystem] {msg}");
        }

        // plays back footstep events at the right times during clone playback
        private void PlayCloneFootsteps(CloneInstance clone)
        {
            var events = clone.recording.footstepEvents;
            if (events == null || events.Count == 0) return;

            while (clone.nextFootstepIndex < events.Count)
            {
                var evt = events[clone.nextFootstepIndex];

                if (evt.time <= clone.playbackTime)
                {
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
                    break;
                }
            }
        }

        // sets layer on all children recursively - needed for PuzzleSensor raycast detection
        private void SetLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null) return;
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}
