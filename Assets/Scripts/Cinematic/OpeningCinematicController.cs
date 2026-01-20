using System.Collections;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Unity.Cinemachine;

namespace Demagnetized.Cinematic
{
    /// <summary>
    /// Controls the opening cinematic sequence in the sewer corridor.
    /// 5-camera timed sequence with tentacle chase, fade effects, and scene transition.
    /// </summary>
    public class OpeningCinematicController : MonoBehaviour
    {
        [Header("Timeline")]
        [SerializeField] private PlayableDirector _playableDirector;

        [Header("Cameras")]
        [SerializeField] private Camera _cinematicCamera;
        [SerializeField] private CinemachineBrain _cinemachineBrain;
        [SerializeField] private CinemachineCamera _vcamDollyFront;
        [SerializeField] private CinemachineCamera _vcamChaseWide;
        [SerializeField] private CinemachineCamera _vcamLookBack;
        [SerializeField] private CinemachineCamera _vcamChaseTight;
        [SerializeField] private CinemachineCamera _vcamCatch;

        [Header("Dolly Front Camera")]
        [SerializeField] private float _dollyForwardSpeed = 0.2f;
        [SerializeField] private float _dollyRotationSpeed = 60f;
        [SerializeField] private float _startRoll = 180f;

        [Header("Characters")]
        [SerializeField] private Transform _arthurCinematic;
        [SerializeField] private Transform _tentaclesParent;
        [SerializeField] private Animator _arthurAnimator;
        [SerializeField] private GameObject _floodWaveObject;

        [Header("Fade")]
        [SerializeField] private CanvasGroup _fadeCanvasGroup;
        [SerializeField] private float _fadeInDuration = 2f;
        [SerializeField] private float _fadeOutDuration = 3f;

        [Header("Movement")]
        [Tooltip("If true, Arthur's movement is handled externally (e.g. AnimationTester). Controller only tracks position.")]
        [SerializeField] private bool _externalMovement = true;
        [SerializeField] private float _arthurRunSpeed = 5f;
        [SerializeField] private float _tentacleChaseSpeed = 6f;
        [SerializeField] private Vector3 _arthurRunDirection = Vector3.back;

        [Header("Scene Transition")]
        [SerializeField] private string _nextSceneName = "";
        [SerializeField] private float _cinematicDuration = 18f;
        [SerializeField] private bool _reEnablePlayerOnEnd = true;

        [Header("Player Reference")]
        [SerializeField] private GameObject _playerObject;

        [Header("Camera Blend Durations")]
        [SerializeField] private float _cutBlendTime = 0f;
        [SerializeField] private float _smoothBlendTime = 1.5f;

        // ── Timing markers (seconds) ──
        private const float FADE_IN_DURATION = 2f;
        private const float DOLLY_FRONT_END = 5f;
        private const float CHASE_WIDE_END = 8f;
        private const float LOOK_BACK_END = 10f;
        private const float CHASE_TIGHT_END = 13f;
        // 13+ = VCam_Catch until end

        private const float TENTACLES_VISIBLE_START = 4f;
        private const float TENTACLES_CATCH_START = 13f;
        private const float FADE_OUT_START = 15f;

        // ── Runtime state ──
        private float _elapsedTime;
        private bool _isPlaying;
        private bool _isCaught;
        private bool _isFadingOut;
        private CinemachineCamera _currentActiveCamera;

        private Vector3 _arthurStartPosition;
        private Vector3 _tentaclesStartPosition;
        private float _currentRoll;

        private void Start()
        {
            Initialize();
            StartCinematic();
        }

        private void Initialize()
        {
            if (_arthurCinematic != null)
                _arthurStartPosition = _arthurCinematic.position;
            if (_tentaclesParent != null)
            {
                _tentaclesStartPosition = _tentaclesParent.position;
                _tentaclesParent.gameObject.SetActive(false);
            }

            if (_floodWaveObject != null)
                _floodWaveObject.SetActive(false);

            if (_playerObject != null)
                _playerObject.SetActive(false);

            if (_fadeCanvasGroup != null)
                _fadeCanvasGroup.alpha = 1f;

            // Start with cut transitions, we'll switch to smooth for dramatic moments
            SetBrainBlend(CinemachineBlendDefinition.Styles.Cut, _cutBlendTime);

            SetCameraPriorities(_vcamDollyFront);

            if (_vcamDollyFront != null)
            {
                _currentRoll = _startRoll;
                var euler = _vcamDollyFront.transform.eulerAngles;
                _vcamDollyFront.transform.eulerAngles = new Vector3(euler.x, euler.y, _startRoll);
            }

            // Disable player camera audio listener
            if (_playerObject != null)
            {
                var playerCam = _playerObject.GetComponentInChildren<Camera>();
                if (playerCam != null && playerCam.TryGetComponent<AudioListener>(out var listener))
                    listener.enabled = false;
            }

            // Arthur animation auto-plays from AnimatorController default state
            if (_arthurAnimator != null)
            {
                _arthurAnimator.applyRootMotion = false;
                _arthurAnimator.speed = 1f;
            }

            Debug.Log("[Cinematic] Initialized. Arthur=" + (_arthurCinematic != null) +
                       ", Tentacles=" + (_tentaclesParent != null) +
                       ", Cameras=" + CountActiveCameras());
        }

        public void StartCinematic()
        {
            _isPlaying = true;
            _elapsedTime = 0f;
            _isCaught = false;
            _isFadingOut = false;

            if (_playableDirector != null)
                _playableDirector.Play();

            StartCoroutine(FadeIn());
        }

        private void Update()
        {
            if (!_isPlaying) return;

            // Skip input
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Space))
            {
                SkipCinematic();
                return;
            }

            _elapsedTime += Time.deltaTime;

            UpdateCameraSwitching();
            UpdateDollyCameraMovement();
            UpdateCharacterMovement();
            UpdateTentacles();

            if (_elapsedTime >= _cinematicDuration)
            {
                EndCinematic();
                return;
            }

            if (_elapsedTime >= FADE_OUT_START && !_isFadingOut)
            {
                _isFadingOut = true;
                StartCoroutine(FadeOut());
            }
        }

        // ── Camera Switching (5-camera sequence) ──

        private void UpdateCameraSwitching()
        {
            CinemachineCamera target;

            if (_elapsedTime < DOLLY_FRONT_END)
            {
                // Shot 1: Dolly front - upside-down opening, rolling to straight
                target = _vcamDollyFront;
            }
            else if (_elapsedTime < CHASE_WIDE_END)
            {
                // Shot 2: Wide chase behind - establish corridor, see tentacles approaching
                target = _vcamChaseWide;
            }
            else if (_elapsedTime < LOOK_BACK_END)
            {
                // Shot 3: Look back - Arthur's POV looking back at tentacles
                target = _vcamLookBack;
            }
            else if (_elapsedTime < CHASE_TIGHT_END)
            {
                // Shot 4: Tight chase behind - tension building, close to Arthur
                target = _vcamChaseTight;
            }
            else
            {
                // Shot 5: Catch camera - high angle dramatic, tentacles catching
                target = _vcamCatch;
            }

            if (target != null && target != _currentActiveCamera)
            {
                // Use smooth blend for dramatic transitions, cut for others
                bool useSmoothBlend = (target == _vcamLookBack || target == _vcamCatch);
                if (useSmoothBlend)
                    SetBrainBlend(CinemachineBlendDefinition.Styles.EaseInOut, _smoothBlendTime);
                else
                    SetBrainBlend(CinemachineBlendDefinition.Styles.Cut, _cutBlendTime);

                SetCameraPriorities(target);
                _currentActiveCamera = target;
                Debug.Log($"[Cinematic] Camera switch: {target.name} at t={_elapsedTime:F1}s");
            }
        }

        private void UpdateDollyCameraMovement()
        {
            if (_vcamDollyFront == null || _elapsedTime >= DOLLY_FRONT_END) return;

            // Move camera forward toward character
            _vcamDollyFront.transform.position += Vector3.forward * _dollyForwardSpeed * Time.deltaTime;

            // Roll correction (upside-down to straight)
            if (_currentRoll > 0f)
            {
                _currentRoll -= _dollyRotationSpeed * Time.deltaTime;
                _currentRoll = Mathf.Max(0f, _currentRoll);
                var euler = _vcamDollyFront.transform.eulerAngles;
                _vcamDollyFront.transform.eulerAngles = new Vector3(euler.x, euler.y, _currentRoll);
            }
        }

        // ── Character Movement ──

        private void UpdateCharacterMovement()
        {
            if (_arthurCinematic == null || _isCaught) return;

            // If AnimationTester handles movement, we only stop it on catch
            if (_externalMovement) return;

            float speed = _arthurRunSpeed;

            // Slow down as catch approaches
            if (_elapsedTime >= TENTACLES_CATCH_START)
            {
                float t = (_elapsedTime - TENTACLES_CATCH_START) / 2f;
                speed *= Mathf.Lerp(1f, 0f, t);
            }

            Vector3 moveDir = new Vector3(_arthurRunDirection.x, 0f, _arthurRunDirection.z).normalized;
            _arthurCinematic.position += moveDir * speed * Time.deltaTime;

            SnapToGround(_arthurCinematic);
        }

        private void SnapToGround(Transform target)
        {
            Vector3 rayOrigin = target.position + Vector3.up * 2f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 10f))
            {
                Vector3 pos = target.position;
                pos.y = hit.point.y;
                target.position = pos;
            }
        }

        // ── Tentacles ──

        private void UpdateTentacles()
        {
            if (_tentaclesParent == null) return;

            // Enable tentacles and flood at the right time
            if (_elapsedTime >= TENTACLES_VISIBLE_START && !_tentaclesParent.gameObject.activeSelf)
            {
                _tentaclesParent.gameObject.SetActive(true);
                if (_floodWaveObject != null)
                    _floodWaveObject.SetActive(true);
                Debug.Log("[Cinematic] Tentacles and flood activated!");
            }

            if (_elapsedTime < TENTACLES_VISIBLE_START) return;

            // Chase speed increases over time
            float timeInChase = _elapsedTime - TENTACLES_VISIBLE_START;
            float chaseMultiplier = 1f + timeInChase * 0.15f;
            float speed = _tentacleChaseSpeed * chaseMultiplier;

            if (_arthurCinematic != null)
            {
                Vector3 direction = (_arthurCinematic.position - _tentaclesParent.position).normalized;
                _tentaclesParent.position += direction * speed * Time.deltaTime;

                float distance = Vector3.Distance(_tentaclesParent.position, _arthurCinematic.position);
                if (distance < 2f && !_isCaught)
                {
                    _isCaught = true;
                    OnArthurCaught();
                }
            }
        }

        private void OnArthurCaught()
        {
            Debug.Log("[Cinematic] Arthur caught by tentacles!");

            if (_arthurAnimator != null)
                _arthurAnimator.speed = 0f;

            // Stop AnimationTester movement if present
            if (_externalMovement && _arthurCinematic != null)
            {
                var animTester = _arthurCinematic.GetComponent<AnimationTester>();
                if (animTester != null)
                    animTester.enabled = false;
            }
        }

        // ── Camera Utilities ──

        private void SetCameraPriorities(CinemachineCamera activeCamera)
        {
            SetCameraPriority(_vcamDollyFront, _vcamDollyFront == activeCamera);
            SetCameraPriority(_vcamChaseWide, _vcamChaseWide == activeCamera);
            SetCameraPriority(_vcamChaseTight, _vcamChaseTight == activeCamera);
            SetCameraPriority(_vcamLookBack, _vcamLookBack == activeCamera);
            SetCameraPriority(_vcamCatch, _vcamCatch == activeCamera);
        }

        private void SetCameraPriority(CinemachineCamera camera, bool isActive)
        {
            if (camera == null) return;
            camera.Priority = new PrioritySettings { Enabled = true, Value = isActive ? 10 : 0 };
        }

        private void SetBrainBlend(CinemachineBlendDefinition.Styles style, float time)
        {
            if (_cinemachineBrain != null)
                _cinemachineBrain.DefaultBlend = new CinemachineBlendDefinition(style, time);
        }

        private int CountActiveCameras()
        {
            int count = 0;
            if (_vcamDollyFront != null) count++;
            if (_vcamChaseWide != null) count++;
            if (_vcamChaseTight != null) count++;
            if (_vcamLookBack != null) count++;
            if (_vcamCatch != null) count++;
            return count;
        }

        // ── Fade Effects ──

        private IEnumerator FadeIn()
        {
            if (_fadeCanvasGroup == null) yield break;

            float elapsed = 0f;
            float startAlpha = _fadeCanvasGroup.alpha;

            while (elapsed < _fadeInDuration)
            {
                elapsed += Time.deltaTime;
                _fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / _fadeInDuration);
                yield return null;
            }
            _fadeCanvasGroup.alpha = 0f;
        }

        private IEnumerator FadeOut()
        {
            if (_fadeCanvasGroup == null) yield break;

            float elapsed = 0f;
            float startAlpha = _fadeCanvasGroup.alpha;

            while (elapsed < _fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                _fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, elapsed / _fadeOutDuration);
                yield return null;
            }
            _fadeCanvasGroup.alpha = 1f;
        }

        // ── Lifecycle ──

        private void EndCinematic()
        {
            _isPlaying = false;

            Debug.Log("[Cinematic] Opening cinematic complete.");

            if (_playableDirector != null)
                _playableDirector.Stop();

            // Hide cinematic Arthur
            if (_arthurCinematic != null)
                _arthurCinematic.gameObject.SetActive(false);

            // Re-enable player
            if (_reEnablePlayerOnEnd && _playerObject != null)
            {
                _playerObject.SetActive(true);
                // Re-enable player camera audio listener
                var playerCam = _playerObject.GetComponentInChildren<Camera>();
                if (playerCam != null && playerCam.TryGetComponent<AudioListener>(out var listener))
                    listener.enabled = true;
            }

            // Load next scene if specified
            if (!string.IsNullOrEmpty(_nextSceneName))
            {
                // Check if scene exists in build settings before loading
                bool sceneExists = false;
                for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                {
                    string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
                    string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
                    if (sceneName == _nextSceneName)
                    {
                        sceneExists = true;
                        break;
                    }
                }

                if (sceneExists)
                    SceneManager.LoadScene(_nextSceneName);
                else
                    Debug.LogWarning($"[Cinematic] Scene '{_nextSceneName}' not found in build settings. Staying in current scene.");
            }
        }

        public void SkipCinematic()
        {
            StopAllCoroutines();

            if (_fadeCanvasGroup != null)
                _fadeCanvasGroup.alpha = 1f;

            EndCinematic();
        }

        private void OnDisable()
        {
            if (_playerObject != null)
                _playerObject.SetActive(true);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_playableDirector == null)
                _playableDirector = GetComponent<PlayableDirector>();
        }

        [ContextMenu("Test Cinematic")]
        private void TestCinematic()
        {
            if (Application.isPlaying)
            {
                _elapsedTime = 0f;
                StartCinematic();
            }
        }

        [ContextMenu("Reset Cinematic")]
        private void ResetCinematic()
        {
            if (Application.isPlaying)
            {
                StopAllCoroutines();
                _isPlaying = false;
                _isCaught = false;
                _isFadingOut = false;
                _elapsedTime = 0f;

                if (_arthurCinematic != null)
                    _arthurCinematic.position = _arthurStartPosition;
                if (_tentaclesParent != null)
                {
                    _tentaclesParent.position = _tentaclesStartPosition;
                    _tentaclesParent.gameObject.SetActive(false);
                }
                if (_fadeCanvasGroup != null)
                    _fadeCanvasGroup.alpha = 0f;
                if (_arthurAnimator != null)
                    _arthurAnimator.speed = 1f;

                Debug.Log("[Cinematic] Reset complete.");
            }
        }
#endif
    }
}
