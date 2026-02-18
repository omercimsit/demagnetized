using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Demagnetized.Cinematic
{
    // Moves an HDRP WaterSurface along the Z axis to chase the player through a corridor.
    // Wave intensity and height increase as the water gets closer.
    [RequireComponent(typeof(WaterSurface))]
    public class HDRPWaterChaseController : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform _targetToChase;
        [Tooltip("Automatically find player by tag if not assigned")]
        [SerializeField] private string _playerTag = "Player";

        [Header("Movement")]
        [SerializeField] private float _chaseSpeed = 8f;
        [SerializeField] private float _minDistanceFromTarget = 5f;
        [SerializeField] private float _maxDistanceFromTarget = 15f;
        [SerializeField] private float _accelerationWhenFar = 2f;
        [SerializeField] private AnimationCurve _speedCurve = AnimationCurve.EaseInOut(0, 0.5f, 1, 1.5f);

        [Header("Wave Intensity")]
        [Tooltip("Increase wave amplitude as water gets closer to player")]
        [SerializeField] private bool _dynamicWaves = true;
        [SerializeField] private float _baseAmplitude = 0.3f;
        [SerializeField] private float _maxAmplitude = 1.5f;
        [SerializeField] private float _baseChoppiness = 0.5f;
        [SerializeField] private float _maxChoppiness = 2f;

        [Header("Corridor Constraints")]
        [SerializeField] private bool _constrainToCorridor = true;
        [SerializeField] private float _corridorMinX = -10f;
        [SerializeField] private float _corridorMaxX = 10f;
        [SerializeField] private float _corridorFloorY = 0f;

        [Header("Rising Water")]
        [Tooltip("Water rises as it chases")]
        [SerializeField] private bool _enableRising = true;
        [SerializeField] private float _minHeight = 1f;
        [SerializeField] private float _maxHeight = 4f;
        [SerializeField] private float _riseSpeed = 0.5f;

        [Header("Surge Events")]
        [SerializeField] private bool _enableRandomSurges = true;
        [SerializeField] private float _surgeInterval = 5f;
        [SerializeField] private float _surgeSpeedMultiplier = 2f;
        [SerializeField] private float _surgeDuration = 1f;

        [Header("Audio")]
        [SerializeField] private AudioSource _rushAudioSource;
        [SerializeField] private AudioClip _rushLoopClip;
        [SerializeField] private float _rushVolume = 0.8f;
        [SerializeField] private AnimationCurve _volumeByDistance = AnimationCurve.EaseInOut(0, 0.3f, 1, 1f);

        [Header("Debug")]
        [SerializeField] private bool _showDebugGizmos = true;

        private WaterSurface _waterSurface;
        private float _currentSpeed;
        private float _distanceToTarget;
        private float _currentHeight;
        private float _lastSurgeTime;
        private bool _isSurging;

        // save original wave settings so Reset() can restore them
        private float _originalAmplitude;
        private float _originalChoppiness;

        private void Awake()
        {
            _waterSurface = GetComponent<WaterSurface>();

            if (_waterSurface != null)
            {
                _originalAmplitude = _waterSurface.largeWindSpeed;
                _originalChoppiness = _waterSurface.largeChaos;
            }
        }

        private void Start()
        {
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
                CheckForSurge();
        }

        private void UpdateMovement()
        {
            Vector3 targetPos = _targetToChase.position;
            Vector3 myPos = transform.position;

            _distanceToTarget = targetPos.z - myPos.z;

            float normalizedDist = Mathf.InverseLerp(_minDistanceFromTarget, _maxDistanceFromTarget, _distanceToTarget);
            float speedMultiplier = _speedCurve.Evaluate(normalizedDist);

            if (_distanceToTarget > _maxDistanceFromTarget)
            {
                // fallen too far behind, catch up
                _currentSpeed = Mathf.Lerp(_currentSpeed, _chaseSpeed * _accelerationWhenFar, Time.deltaTime * 2f);
            }
            else if (_distanceToTarget < _minDistanceFromTarget)
            {
                // too close, ease off
                _currentSpeed = Mathf.Lerp(_currentSpeed, _chaseSpeed * 0.5f, Time.deltaTime * 3f);
            }
            else
            {
                _currentSpeed = Mathf.Lerp(_currentSpeed, _chaseSpeed * speedMultiplier, Time.deltaTime);
            }

            if (_isSurging)
                _currentSpeed *= _surgeSpeedMultiplier;

            Vector3 velocity = Vector3.forward * _currentSpeed;
            transform.position += velocity * Time.deltaTime;

            if (_constrainToCorridor)
            {
                Vector3 pos = transform.position;
                float centerX = (_corridorMinX + _corridorMaxX) / 2f;
                pos.x = centerX;
                pos.y = _corridorFloorY + _currentHeight;
                transform.position = pos;
            }
        }

        private void UpdateWaveIntensity()
        {
            if (!_dynamicWaves || _waterSurface == null) return;

            float intensity = 1f - Mathf.Clamp01(_distanceToTarget / _maxDistanceFromTarget);

            if (_isSurging)
                intensity = Mathf.Min(1f, intensity + 0.3f);

            float amplitude = Mathf.Lerp(_baseAmplitude, _maxAmplitude, intensity);
            float choppiness = Mathf.Lerp(_baseChoppiness, _maxChoppiness, intensity);

            _waterSurface.largeWindSpeed = amplitude;
            _waterSurface.largeChaos = choppiness;
        }

        private void UpdateHeight()
        {
            if (!_enableRising) return;

            float targetHeight = Mathf.Lerp(_minHeight, _maxHeight,
                1f - Mathf.Clamp01(_distanceToTarget / _maxDistanceFromTarget));

            _currentHeight = Mathf.Lerp(_currentHeight, targetHeight, Time.deltaTime * _riseSpeed);
        }

        private void CheckForSurge()
        {
            if (_isSurging) return;

            if (Time.time - _lastSurgeTime > _surgeInterval)
            {
                if (Random.value > 0.7f)
                    StartCoroutine(SurgeCoroutine());

                _lastSurgeTime = Time.time;
            }
        }

        private System.Collections.IEnumerator SurgeCoroutine()
        {
            _isSurging = true;
            yield return new WaitForSeconds(_surgeDuration);
            _isSurging = false;
        }

        // trigger a surge from script - duration defaults to inspector value
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

        private void UpdateAudio()
        {
            if (_rushAudioSource == null) return;

            float normalizedDistance = Mathf.InverseLerp(_minDistanceFromTarget, _maxDistanceFromTarget, _distanceToTarget);
            float volume = _volumeByDistance.Evaluate(1f - normalizedDistance) * _rushVolume;
            _rushAudioSource.volume = volume;

            float pitchMod = Mathf.Lerp(0.9f, 1.1f, _currentSpeed / (_chaseSpeed * _accelerationWhenFar));
            _rushAudioSource.pitch = pitchMod;
        }

        public float GetDistanceToTarget() => _distanceToTarget;
        public float GetCurrentSpeed() => _currentSpeed;
        public float GetCurrentHeight() => _currentHeight;
        public bool IsSurging() => _isSurging;

        public void SetTarget(Transform target)
        {
            _targetToChase = target;
        }

        public void SetPaused(bool paused)
        {
            enabled = !paused;
        }

        public void TeleportTo(Vector3 position)
        {
            transform.position = position;
        }

        // TODO: might want to also reset position here for level restarts
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

        private void OnDrawGizmosSelected()
        {
            if (!_showDebugGizmos) return;

            if (_constrainToCorridor)
            {
                Gizmos.color = Color.cyan;
                Vector3 center = new Vector3((_corridorMinX + _corridorMaxX) / 2f, _corridorFloorY + 2f, transform.position.z);
                Vector3 size = new Vector3(_corridorMaxX - _corridorMinX, 4f, 40f);
                Gizmos.DrawWireCube(center, size);
            }

            if (_targetToChase != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position, _targetToChase.position);
                Gizmos.DrawWireSphere(_targetToChase.position, 0.5f);

                Gizmos.color = _distanceToTarget < _minDistanceFromTarget ? Color.red :
                              (_distanceToTarget > _maxDistanceFromTarget ? Color.green : Color.yellow);
                Gizmos.DrawWireSphere(transform.position + Vector3.forward * _distanceToTarget, 0.3f);
            }

            Gizmos.color = Color.blue;
            Vector3 minHeightPos = transform.position;
            minHeightPos.y = _corridorFloorY + _minHeight;
            Vector3 maxHeightPos = transform.position;
            maxHeightPos.y = _corridorFloorY + _maxHeight;
            Gizmos.DrawLine(minHeightPos, maxHeightPos);
        }
    }
}
