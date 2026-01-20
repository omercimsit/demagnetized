using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Demagnetized.Cinematic
{
    /// <summary>
    /// Controls HDRP Water Surface to chase the player through corridors.
    /// Attach this to a GameObject with WaterSurface component.
    /// </summary>
    [RequireComponent(typeof(WaterSurface))]
    public class HDRPWaterChaseController : MonoBehaviour
    {
        #region Serialized Fields

        [Header("=== TARGET ===")]
        [SerializeField] private Transform _targetToChase;
        [Tooltip("Automatically find player by tag if not assigned")]
        [SerializeField] private string _playerTag = "Player";

        [Header("=== MOVEMENT ===")]
        [SerializeField] private float _chaseSpeed = 8f;
        [SerializeField] private float _minDistanceFromTarget = 5f;
        [SerializeField] private float _maxDistanceFromTarget = 15f;
        [SerializeField] private float _accelerationWhenFar = 2f;
        [SerializeField] private AnimationCurve _speedCurve = AnimationCurve.EaseInOut(0, 0.5f, 1, 1.5f);

        [Header("=== WAVE INTENSITY ===")]
        [Tooltip("Increase wave amplitude as water gets closer to player")]
        [SerializeField] private bool _dynamicWaves = true;
        [SerializeField] private float _baseAmplitude = 0.3f;
        [SerializeField] private float _maxAmplitude = 1.5f;
        [SerializeField] private float _baseChoppiness = 0.5f;
        [SerializeField] private float _maxChoppiness = 2f;

        [Header("=== CORRIDOR CONSTRAINTS ===")]
        [SerializeField] private bool _constrainToCorridor = true;
        [SerializeField] private float _corridorMinX = -10f;
        [SerializeField] private float _corridorMaxX = 10f;
        [SerializeField] private float _corridorFloorY = 0f;

        [Header("=== RISING WATER ===")]
        [Tooltip("Water rises as it chases")]
        [SerializeField] private bool _enableRising = true;
        [SerializeField] private float _minHeight = 1f;
        [SerializeField] private float _maxHeight = 4f;
        [SerializeField] private float _riseSpeed = 0.5f;

        [Header("=== SURGE EVENTS ===")]
        [SerializeField] private bool _enableRandomSurges = true;
        [SerializeField] private float _surgeInterval = 5f;
        [SerializeField] private float _surgeSpeedMultiplier = 2f;
        [SerializeField] private float _surgeDuration = 1f;

        [Header("=== AUDIO ===")]
        [SerializeField] private AudioSource _rushAudioSource;
        [SerializeField] private AudioClip _rushLoopClip;
        [SerializeField] private float _rushVolume = 0.8f;
        [SerializeField] private AnimationCurve _volumeByDistance = AnimationCurve.EaseInOut(0, 0.3f, 1, 1f);

        [Header("=== DEBUG ===")]
        [SerializeField] private bool _showDebugGizmos = true;

        #endregion

        #region Private Fields

        private WaterSurface _waterSurface;
        private float _currentSpeed;
        private float _distanceToTarget;
        private float _currentHeight;
        private float _lastSurgeTime;
        private bool _isSurging;

        // Cache original water settings
        private float _originalAmplitude;
        private float _originalChoppiness;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _waterSurface = GetComponent<WaterSurface>();

            if (_waterSurface != null)
            {
                // Cache original settings
                _originalAmplitude = _waterSurface.largeWindSpeed;
                _originalChoppiness = _waterSurface.largeChaos;
            }
        }

        private void Start()
        {
            // Find player if not assigned
            if (_targetToChase == null)
            {
                var player = GameObject.FindGameObjectWithTag(_playerTag);
                if (player != null)
                {
                    _targetToChase = player.transform;
                }
                else
                {
                    Debug.LogWarning($"[HDRPWaterChase] No target found with tag '{_playerTag}'");
                }
            }

            _currentSpeed = _chaseSpeed;
            _currentHeight = _minHeight;

            // Initialize audio
            if (_rushAudioSource != null && _rushLoopClip != null)
            {
                _rushAudioSource.clip = _rushLoopClip;
                _rushAudioSource.loop = true;
                _rushAudioSource.volume = _rushVolume;
                _rushAudioSource.Play();
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
        }

        private void Update()
        {
            if (_targetToChase == null || _waterSurface == null) return;

            UpdateMovement();
            UpdateWaveIntensity();
            UpdateHeight();
            UpdateAudio();

            if (_enableRandomSurges)
            {
                CheckForSurge();
            }
        }

        #endregion

        #region Movement

        private void UpdateMovement()
        {
            Vector3 targetPos = _targetToChase.position;
            Vector3 myPos = transform.position;

            // Calculate distance (Z axis for corridor chase)
            _distanceToTarget = targetPos.z - myPos.z;

            // Dynamic speed based on distance
            float normalizedDist = Mathf.InverseLerp(_minDistanceFromTarget, _maxDistanceFromTarget, _distanceToTarget);
            float speedMultiplier = _speedCurve.Evaluate(normalizedDist);

            if (_distanceToTarget > _maxDistanceFromTarget)
            {
                // Too far, accelerate
                _currentSpeed = Mathf.Lerp(_currentSpeed, _chaseSpeed * _accelerationWhenFar, Time.deltaTime * 2f);
            }
            else if (_distanceToTarget < _minDistanceFromTarget)
            {
                // Too close, slow down
                _currentSpeed = Mathf.Lerp(_currentSpeed, _chaseSpeed * 0.5f, Time.deltaTime * 3f);
            }
            else
            {
                _currentSpeed = Mathf.Lerp(_currentSpeed, _chaseSpeed * speedMultiplier, Time.deltaTime);
            }

            // Apply surge bonus
            if (_isSurging)
            {
                _currentSpeed *= _surgeSpeedMultiplier;
            }

            // Move forward
            Vector3 velocity = Vector3.forward * _currentSpeed;
            transform.position += velocity * Time.deltaTime;

            // Constrain to corridor
            if (_constrainToCorridor)
            {
                Vector3 pos = transform.position;
                float centerX = (_corridorMinX + _corridorMaxX) / 2f;
                pos.x = centerX; // Center water in corridor
                pos.y = _corridorFloorY + _currentHeight;
                transform.position = pos;
            }
        }

        #endregion

        #region Wave Control

        private void UpdateWaveIntensity()
        {
            if (!_dynamicWaves || _waterSurface == null) return;

            // Intensity based on proximity to player
            float intensity = 1f - Mathf.Clamp01(_distanceToTarget / _maxDistanceFromTarget);

            // Boost during surge
            if (_isSurging)
            {
                intensity = Mathf.Min(1f, intensity + 0.3f);
            }

            // Apply to water surface
            float amplitude = Mathf.Lerp(_baseAmplitude, _maxAmplitude, intensity);
            float choppiness = Mathf.Lerp(_baseChoppiness, _maxChoppiness, intensity);

            _waterSurface.largeWindSpeed = amplitude;
            _waterSurface.largeChaos = choppiness;
        }

        #endregion

        #region Height Control

        private void UpdateHeight()
        {
            if (!_enableRising) return;

            // Gradually rise as we chase
            float targetHeight = Mathf.Lerp(_minHeight, _maxHeight,
                1f - Mathf.Clamp01(_distanceToTarget / _maxDistanceFromTarget));

            _currentHeight = Mathf.Lerp(_currentHeight, targetHeight, Time.deltaTime * _riseSpeed);
        }

        #endregion

        #region Surge System

        private void CheckForSurge()
        {
            if (_isSurging) return;

            if (Time.time - _lastSurgeTime > _surgeInterval)
            {
                // Random chance for surge
                if (Random.value > 0.7f)
                {
                    StartCoroutine(SurgeCoroutine());
                }
                _lastSurgeTime = Time.time;
            }
        }

        private System.Collections.IEnumerator SurgeCoroutine()
        {
            _isSurging = true;

            yield return new WaitForSeconds(_surgeDuration);

            _isSurging = false;
        }

        /// <summary>
        /// Trigger a manual surge
        /// </summary>
        public void TriggerSurge(float duration = -1f)
        {
            if (duration < 0) duration = _surgeDuration;
            StartCoroutine(ManualSurgeCoroutine(duration));
        }

        private System.Collections.IEnumerator ManualSurgeCoroutine(float duration)
        {
            _isSurging = true;
            yield return new WaitForSeconds(duration);
            _isSurging = false;
        }

        #endregion

        #region Audio

        private void UpdateAudio()
        {
            if (_rushAudioSource == null) return;

            // Volume based on distance to player
            float normalizedDistance = Mathf.InverseLerp(_minDistanceFromTarget, _maxDistanceFromTarget, _distanceToTarget);
            float volume = _volumeByDistance.Evaluate(1f - normalizedDistance) * _rushVolume;
            _rushAudioSource.volume = volume;

            // Pitch variation based on speed
            float pitchMod = Mathf.Lerp(0.9f, 1.1f, _currentSpeed / (_chaseSpeed * _accelerationWhenFar));
            _rushAudioSource.pitch = pitchMod;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Get current distance to target
        /// </summary>
        public float GetDistanceToTarget() => _distanceToTarget;

        /// <summary>
        /// Get current speed
        /// </summary>
        public float GetCurrentSpeed() => _currentSpeed;

        /// <summary>
        /// Get current water height
        /// </summary>
        public float GetCurrentHeight() => _currentHeight;

        /// <summary>
        /// Is currently surging?
        /// </summary>
        public bool IsSurging() => _isSurging;

        /// <summary>
        /// Set the target to chase
        /// </summary>
        public void SetTarget(Transform target)
        {
            _targetToChase = target;
        }

        /// <summary>
        /// Pause/Resume the chase
        /// </summary>
        public void SetPaused(bool paused)
        {
            enabled = !paused;
        }

        /// <summary>
        /// Teleport water to position
        /// </summary>
        public void TeleportTo(Vector3 position)
        {
            transform.position = position;
        }

        /// <summary>
        /// Reset to initial state
        /// </summary>
        public void Reset()
        {
            _currentHeight = _minHeight;
            _currentSpeed = _chaseSpeed;
            _isSurging = false;

            if (_waterSurface != null)
            {
                _waterSurface.largeWindSpeed = _originalAmplitude;
                _waterSurface.largeChaos = _originalChoppiness;
            }
        }

        #endregion

        #region Debug

        private void OnDrawGizmosSelected()
        {
            if (!_showDebugGizmos) return;

            // Draw corridor bounds
            if (_constrainToCorridor)
            {
                Gizmos.color = Color.cyan;
                Vector3 center = new Vector3((_corridorMinX + _corridorMaxX) / 2f, _corridorFloorY + 2f, transform.position.z);
                Vector3 size = new Vector3(_corridorMaxX - _corridorMinX, 4f, 40f);
                Gizmos.DrawWireCube(center, size);
            }

            // Draw chase target
            if (_targetToChase != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position, _targetToChase.position);
                Gizmos.DrawWireSphere(_targetToChase.position, 0.5f);

                // Distance indicator
                Gizmos.color = _distanceToTarget < _minDistanceFromTarget ? Color.red :
                              (_distanceToTarget > _maxDistanceFromTarget ? Color.green : Color.yellow);
                Gizmos.DrawWireSphere(transform.position + Vector3.forward * _distanceToTarget, 0.3f);
            }

            // Draw height range
            Gizmos.color = Color.blue;
            Vector3 minHeightPos = transform.position;
            minHeightPos.y = _corridorFloorY + _minHeight;
            Vector3 maxHeightPos = transform.position;
            maxHeightPos.y = _corridorFloorY + _maxHeight;
            Gizmos.DrawLine(minHeightPos, maxHeightPos);
        }

        #endregion
    }
}
