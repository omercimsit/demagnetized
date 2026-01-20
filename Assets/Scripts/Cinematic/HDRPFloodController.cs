using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using System.Collections.Generic;

namespace Demagnetized.Cinematic
{
    /// <summary>
    /// HDRP Water System based realistic flood controller.
    /// Uses Water Deformers for dynamic wave simulation.
    /// </summary>
    public class HDRPFloodController : MonoBehaviour
    {
        #region Serialized Fields
        
        [Header("=== WATER SURFACE ===")]
        [SerializeField] private WaterSurface _waterSurface;
        [SerializeField] private float _baseWaterLevel = 0.15f;
        [SerializeField] private float _floodWaterLevel = 1.5f;
        
        [Header("=== WAVE DEFORMERS ===")]
        [Tooltip("Main flood wave deformer - creates the chasing wave")]
        [SerializeField] private WaterDeformer _mainWaveDeformer;
        [Tooltip("Left wall splash deformer")]
        [SerializeField] private WaterDeformer _leftWallDeformer;
        [Tooltip("Right wall splash deformer")]
        [SerializeField] private WaterDeformer _rightWallDeformer;
        
        [Header("=== TARGET ===")]
        [SerializeField] private Transform _target;
        [SerializeField] private float _followDistance = 8f;
        [SerializeField] private float _baseSpeed = 10f;
        [SerializeField] private float _catchUpSpeed = 15f;
        [SerializeField] private float _maxDistance = 20f;
        
        [Header("=== MAIN WAVE SETTINGS ===")]
        [SerializeField] private float _waveHeight = 3f;
        [SerializeField] private float _waveWidth = 18f;
        [SerializeField] private float _waveDepth = 6f;
        [SerializeField] private AnimationCurve _waveHeightBySpeed = AnimationCurve.EaseInOut(0, 0.5f, 1, 1.5f);
        
        [Header("=== WALL INTERACTION ===")]
        [SerializeField] private LayerMask _wallLayers = -1;
        [SerializeField] private float _wallDetectionRange = 2f;
        [SerializeField] private float _wallSplashMultiplier = 2f;
        [SerializeField] private float _wallSplashWidth = 3f;
        
        [Header("=== CORRIDOR BOUNDS ===")]
        [SerializeField] private float _corridorMinX = -170f;
        [SerializeField] private float _corridorMaxX = -150f;
        [SerializeField] private float _corridorMinZ = -50f;
        [SerializeField] private float _corridorMaxZ = 100f;
        
        [Header("=== WATER RISING ===")]
        [SerializeField] private bool _enableWaterRising = true;
        [SerializeField] private float _riseSpeed = 0.3f;
        [SerializeField] private float _riseStartDistance = 10f;
        
        [Header("=== CURRENT FLOW ===")]
        #pragma warning disable CS0414
        [SerializeField] private bool _enableCurrentFlow = true;
        #pragma warning restore CS0414
        [SerializeField] private Vector2 _flowDirection = new Vector2(0, 1);
        #pragma warning disable CS0414
        [SerializeField] private float _flowSpeed = 5f;
        #pragma warning restore CS0414
        
        [Header("=== AUDIO ===")]
        [SerializeField] private AudioSource _floodAudio;
        [SerializeField] private AnimationCurve _volumeByDistance = AnimationCurve.EaseInOut(0, 0.2f, 1, 1f);
        
        [Header("=== DEBUG ===")]
        [SerializeField] private bool _showDebug = true;
        
        #endregion
        
        #region Private Fields
        
        private float _currentWaterLevel;
        private float _currentSpeed;
        private Vector3 _wavePosition;
        private float _distanceToTarget;
        
        private float _leftWallProximity;
        private float _rightWallProximity;
        
        private bool _isActive = false;
        
        #endregion
        
        #region Unity Lifecycle

        private void OnDisable()
        {
            StopAllCoroutines();
        }

        private void Start()
        {
            Initialize();
        }

        private void Update()
        {
            if (!_isActive) return;
            
            UpdateWavePosition();
            UpdateWallDetection();
            UpdateDeformers();
            UpdateWaterLevel();
            UpdateAudio();
        }
        
        #endregion
        
        #region Initialization
        
        private void Initialize()
        {
            // Find target if not assigned
            if (_target == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) _target = player.transform;
            }
            
            // Find water surface if not assigned
            if (_waterSurface == null)
            {
                _waterSurface = FindFirstObjectByType<WaterSurface>();
            }
            
            // Initialize water level
            _currentWaterLevel = _baseWaterLevel;
            _currentSpeed = _baseSpeed;
            
            // Set initial wave position behind target
            if (_target != null)
            {
                _wavePosition = _target.position - Vector3.forward * _followDistance;
                _wavePosition.y = 0;
            }
            
            Debug.Log("[HDRPFlood] Initialized. Use StartFlood() to begin.");
        }
        
        #endregion
        
        #region Wave Movement
        
        private void UpdateWavePosition()
        {
            if (_target == null) return;
            
            Vector3 targetPos = _target.position;
            _distanceToTarget = targetPos.z - _wavePosition.z;
            
            // Dynamic speed based on distance
            float normalizedDist = Mathf.InverseLerp(_followDistance * 0.5f, _maxDistance, _distanceToTarget);
            float targetSpeed = Mathf.Lerp(_baseSpeed, _catchUpSpeed, normalizedDist);
            _currentSpeed = Mathf.Lerp(_currentSpeed, targetSpeed, Time.deltaTime * 2f);
            
            // Move wave forward
            _wavePosition.z += _currentSpeed * Time.deltaTime;
            
            // Keep X centered in corridor
            _wavePosition.x = ((_corridorMinX + _corridorMaxX) / 2f);
            
            // Clamp to corridor bounds
            _wavePosition.z = Mathf.Clamp(_wavePosition.z, _corridorMinZ, _corridorMaxZ);
        }
        
        #endregion
        
        #region Wall Detection
        
        private void UpdateWallDetection()
        {
            Vector3 leftRayOrigin = _wavePosition + Vector3.up * 1f;
            Vector3 rightRayOrigin = _wavePosition + Vector3.up * 1f;
            
            // Check left wall
            _leftWallProximity = 0f;
            if (Physics.Raycast(leftRayOrigin, Vector3.left, out RaycastHit leftHit, _wallDetectionRange, _wallLayers))
            {
                _leftWallProximity = 1f - (leftHit.distance / _wallDetectionRange);
            }
            
            // Check right wall
            _rightWallProximity = 0f;
            if (Physics.Raycast(rightRayOrigin, Vector3.right, out RaycastHit rightHit, _wallDetectionRange, _wallLayers))
            {
                _rightWallProximity = 1f - (rightHit.distance / _wallDetectionRange);
            }
        }
        
        #endregion
        
        #region Deformer Updates
        
        private void UpdateDeformers()
        {
            // Update main wave deformer
            if (_mainWaveDeformer != null)
            {
                _mainWaveDeformer.transform.position = _wavePosition;
                
                // Dynamic wave height based on speed
                float speedRatio = _currentSpeed / _catchUpSpeed;
                float heightMultiplier = _waveHeightBySpeed.Evaluate(speedRatio);
                
                // Adjust deformer amplitude
                _mainWaveDeformer.amplitude = _waveHeight * heightMultiplier;
                
                // Update region size
                Vector3 regionSize = new Vector3(_waveWidth, _waveHeight * 2f, _waveDepth);
                _mainWaveDeformer.regionSize = regionSize;
            }
            
            // Update left wall deformer
            if (_leftWallDeformer != null && _leftWallProximity > 0.1f)
            {
                Vector3 leftPos = _wavePosition;
                leftPos.x = _corridorMinX + 1f;
                _leftWallDeformer.transform.position = leftPos;
                
                _leftWallDeformer.amplitude = _waveHeight * _leftWallProximity * _wallSplashMultiplier;
                _leftWallDeformer.regionSize = new Vector3(_wallSplashWidth, _waveHeight * 3f, _waveDepth * 0.5f);
            }
            
            // Update right wall deformer
            if (_rightWallDeformer != null && _rightWallProximity > 0.1f)
            {
                Vector3 rightPos = _wavePosition;
                rightPos.x = _corridorMaxX - 1f;
                _rightWallDeformer.transform.position = rightPos;
                
                _rightWallDeformer.amplitude = _waveHeight * _rightWallProximity * _wallSplashMultiplier;
                _rightWallDeformer.regionSize = new Vector3(_wallSplashWidth, _waveHeight * 3f, _waveDepth * 0.5f);
            }
        }
        
        #endregion
        
        #region Water Level
        
        private void UpdateWaterLevel()
        {
            if (!_enableWaterRising) return;
            
            // Rise water level as flood approaches
            if (_distanceToTarget < _riseStartDistance)
            {
                float riseRatio = 1f - (_distanceToTarget / _riseStartDistance);
                float targetLevel = Mathf.Lerp(_baseWaterLevel, _floodWaterLevel, riseRatio);
                _currentWaterLevel = Mathf.MoveTowards(_currentWaterLevel, targetLevel, _riseSpeed * Time.deltaTime);
            }
            
            // Apply to water surface transform
            if (_waterSurface != null)
            {
                Vector3 pos = _waterSurface.transform.position;
                pos.y = _currentWaterLevel;
                _waterSurface.transform.position = pos;
            }
        }
        
        #endregion
        
        #region Audio
        
        private void UpdateAudio()
        {
            if (_floodAudio == null) return;
            
            float normalizedDist = Mathf.InverseLerp(_maxDistance, 0, _distanceToTarget);
            _floodAudio.volume = _volumeByDistance.Evaluate(normalizedDist);
            
            // Pitch based on speed
            _floodAudio.pitch = Mathf.Lerp(0.9f, 1.2f, _currentSpeed / _catchUpSpeed);
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Start the flood chase sequence
        /// </summary>
        public void StartFlood()
        {
            _isActive = true;
            
            if (_target != null)
            {
                _wavePosition = _target.position - Vector3.forward * _followDistance;
                _wavePosition.y = 0;
            }
            
            if (_floodAudio != null)
            {
                _floodAudio.Play();
            }
            
            Debug.Log("[HDRPFlood] Flood started!");
        }
        
        /// <summary>
        /// Stop the flood
        /// </summary>
        public void StopFlood()
        {
            _isActive = false;
            
            if (_floodAudio != null)
            {
                _floodAudio.Stop();
            }
        }
        
        /// <summary>
        /// Trigger a surge - temporary speed and height boost
        /// </summary>
        public void TriggerSurge(float duration = 2f, float speedMultiplier = 1.5f)
        {
            StartCoroutine(SurgeCoroutine(duration, speedMultiplier));
        }
        
        private System.Collections.IEnumerator SurgeCoroutine(float duration, float multiplier)
        {
            float originalSpeed = _baseSpeed;
            float originalHeight = _waveHeight;
            
            _baseSpeed *= multiplier;
            _waveHeight *= 1.5f;
            
            yield return new WaitForSeconds(duration);
            
            // Smooth return
            float elapsed = 0f;
            while (elapsed < 0.5f)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / 0.5f;
                _baseSpeed = Mathf.Lerp(_baseSpeed, originalSpeed, t);
                _waveHeight = Mathf.Lerp(_waveHeight, originalHeight, t);
                yield return null;
            }
            
            _baseSpeed = originalSpeed;
            _waveHeight = originalHeight;
        }
        
        /// <summary>
        /// Set target to chase
        /// </summary>
        public void SetTarget(Transform target)
        {
            _target = target;
        }
        
        /// <summary>
        /// Teleport flood to position
        /// </summary>
        public void TeleportTo(Vector3 position)
        {
            _wavePosition = position;
            _wavePosition.y = 0;
        }
        
        /// <summary>
        /// Get current distance to target
        /// </summary>
        public float GetDistanceToTarget() => _distanceToTarget;
        
        /// <summary>
        /// Get current water level
        /// </summary>
        public float GetWaterLevel() => _currentWaterLevel;
        
        /// <summary>
        /// Check if flood is active
        /// </summary>
        public bool IsActive => _isActive;
        
        #endregion
        
        #region Debug
        
        private void OnDrawGizmosSelected()
        {
            if (!_showDebug) return;
            
            // Draw wave position
            Gizmos.color = Color.blue;
            Gizmos.DrawWireCube(_wavePosition + Vector3.up * _waveHeight * 0.5f, 
                new Vector3(_waveWidth, _waveHeight, _waveDepth));
            
            // Draw corridor bounds
            Gizmos.color = Color.cyan;
            Vector3 corridorCenter = new Vector3(
                (_corridorMinX + _corridorMaxX) / 2f,
                1f,
                (_corridorMinZ + _corridorMaxZ) / 2f
            );
            Vector3 corridorSize = new Vector3(
                _corridorMaxX - _corridorMinX,
                2f,
                _corridorMaxZ - _corridorMinZ
            );
            Gizmos.DrawWireCube(corridorCenter, corridorSize);
            
            // Draw target connection
            if (_target != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(_wavePosition, _target.position);
            }
            
            // Draw wall detection rays
            Gizmos.color = Color.yellow;
            Vector3 rayOrigin = _wavePosition + Vector3.up;
            Gizmos.DrawRay(rayOrigin, Vector3.left * _wallDetectionRange);
            Gizmos.DrawRay(rayOrigin, Vector3.right * _wallDetectionRange);
        }
        
        #endregion
    }
}
