using UnityEngine;

namespace CAS_Demo.Scripts.FPS
{
    /// <summary>
    /// Camera Effects System - DISABLED BY DEFAULT
    /// Head bob effects have been disabled to fix camera shaking.
    /// Enable manually if you want subtle camera movement.
    /// </summary>
    public class HeadBob : MonoBehaviour
    {
        [Header("=== MASTER SWITCH ===")]
        [Tooltip("Enable for realistic camera movement")]
        [SerializeField] private bool enableAllEffects = true;  // ENABLED BY DEFAULT
        
        [Header("Individual Toggles")]
        [SerializeField] private bool enableHeadBob = true;
        [SerializeField] private bool enableTilt = true;
        [SerializeField] private bool enableImpact = true;
        
        [Header("Bob Settings (Premium Feel)")]
        [SerializeField] private float walkBobSpeed = 8f;
        [SerializeField] private float walkBobAmount = 0.05f;    // Increased for better feel
        [SerializeField] private float sprintBobAmount = 0.08f;
        #pragma warning disable CS0414
        [SerializeField] private float idleBobAmount = 0.015f;
        #pragma warning restore CS0414
        
        [Header("Impact Settings")]
        [SerializeField] private float landImpactAmount = 0.15f; // More impact
        [SerializeField] private float impactRecoverySpeed = 10f;
        
        [Header("Tilt Settings")]
        [SerializeField] private float tiltAmount = 0.2f;
        
        [Header("Smoothing")]
        [SerializeField] private float smoothing = 25f;
        [SerializeField] private float movementThreshold = 0.2f;
        
        // Internal
        private float _timer;
        private Vector3 _defaultLocalPos;
        private float _impactOffsetY;
        private float _currentTilt;
        private Vector3 _smoothedVelocity;
        private Vector3 _currentOffset;
        
        private Transform _cameraTransform;
        private CharacterController _controller;
        private bool _wasGrounded;
        private bool _isInitialized;
        
        private void Start()
        {
            // If all effects are disabled, skip initialization
            if (!enableAllEffects)
            {
                Debug.Log("[HeadBob] All effects DISABLED - no camera shake");
                enabled = false;
                return;
            }
            
            InitializeCamera();
        }
        
        private void InitializeCamera()
        {
            _cameraTransform = Camera.main?.transform;
            
            if (_cameraTransform == null)
                _cameraTransform = GetComponentInChildren<Camera>()?.transform;
            
            _controller = GetComponentInParent<CharacterController>();
            
            if (_cameraTransform != null && _controller != null)
            {
                _defaultLocalPos = _cameraTransform.localPosition;
                _isInitialized = true;
            }
            else
            {
                enabled = false;
            }
            
            _wasGrounded = true;
        }
        
        private void LateUpdate()
        {
            // Early exit if disabled or during slow-mo (prevents jittery camera during clone playback)
            if (!enableAllEffects || !_isInitialized) return;
            if (Time.timeScale < 0.5f) return; // Skip during slow-mo for smooth experience
            
            float dt = Time.unscaledDeltaTime;
            dt = Mathf.Min(dt, 0.033f); // Cap at 30fps minimum
            
            // Smooth velocity
            if (_controller != null)
            {
                Vector3 vel = _controller.velocity;
                vel.y = 0;
                _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, vel, dt * 8f);
            }
            
            Vector3 targetOffset = Vector3.zero;
            
            // Head bob (if enabled)
            if (enableHeadBob)
            {
                float speed = _smoothedVelocity.magnitude;
                if (speed > movementThreshold && _controller.isGrounded)
                {
                    float bobAmount = speed > 4f ? sprintBobAmount : walkBobAmount;
                    _timer += dt * walkBobSpeed;
                    targetOffset.x = Mathf.Sin(_timer) * bobAmount;
                    targetOffset.y = Mathf.Sin(_timer * 2f) * bobAmount * 0.5f;
                }
            }
            
            // Impact (if enabled)
            if (enableImpact)
            {
                bool isGrounded = _controller.isGrounded;
                if (isGrounded && !_wasGrounded && _controller.velocity.y < -2f)
                {
                    _impactOffsetY -= landImpactAmount;
                }
                _wasGrounded = isGrounded;
                
                _impactOffsetY = Mathf.Lerp(_impactOffsetY, 0f, dt * impactRecoverySpeed);
                targetOffset.y += _impactOffsetY;
            }
            
            // Apply position
            _currentOffset = Vector3.Lerp(_currentOffset, targetOffset, dt * smoothing);
            _cameraTransform.localPosition = Vector3.Lerp(
                _cameraTransform.localPosition,
                _defaultLocalPos + _currentOffset,
                dt * smoothing
            );
            
            // Tilt (if enabled)
            if (enableTilt)
            {
                float sideways = transform.InverseTransformDirection(_smoothedVelocity).x;
                float targetTilt = Mathf.Abs(sideways) > movementThreshold 
                    ? Mathf.Clamp(-sideways * tiltAmount * 0.1f, -tiltAmount, tiltAmount) 
                    : 0f;
                _currentTilt = Mathf.Lerp(_currentTilt, targetTilt, dt * 8f);
                
                Vector3 euler = _cameraTransform.localEulerAngles;
                _cameraTransform.localRotation = Quaternion.Euler(euler.x, euler.y, _currentTilt);
            }
        }
        
        /// <summary>
        /// Enable or disable all camera effects at runtime
        /// </summary>
        public void SetEnabled(bool enable)
        {
            enableAllEffects = enable;
            
            if (enable && !_isInitialized)
            {
                InitializeCamera();
            }
            else if (!enable)
            {
                // Reset camera position
                if (_cameraTransform != null)
                {
                    _cameraTransform.localPosition = _defaultLocalPos;
                }
                _currentOffset = Vector3.zero;
                _impactOffsetY = 0f;
                _currentTilt = 0f;
            }
        }
    }
}
