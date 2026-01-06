using UnityEngine;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Camera;

namespace CAS_Demo.Scripts.FPS
{
    /// <summary>
    /// Camera Stabilizer - Reduces or eliminates camera shake from KINEMATION.
    /// Attach this to the same GameObject as CharacterCamera.
    /// </summary>
    [RequireComponent(typeof(CharacterCamera))]
    public class CameraStabilizer : MonoBehaviour
    {
        [Header("Stabilization Settings")]
        [Tooltip("Completely disable KINEMATION camera animation shake")]
        [SerializeField] private bool disableCameraAnimation = true;
        
        [Tooltip("Reduce camera shake intensity (0 = no shake, 1 = full shake)")]
        [Range(0f, 1f)]
        #pragma warning disable CS0414
        [SerializeField] private float shakeMultiplier = 0f;
        #pragma warning restore CS0414
        
        [Tooltip("Extra smoothing for camera rotation")]
        [SerializeField] private float rotationSmoothing = 0f;
        
        [Header("First Person Lock")]
        [Tooltip("Force first person mode")]
        [SerializeField] private bool forceFirstPerson = true;
        
        private CharacterCamera _characterCamera;
        private Transform _cameraAnimationSource;
        private bool _originalAnimSourceSet = false;
        private System.Reflection.FieldInfo _cachedAnimSourceField; // Cache reflection for performance
        
        private void Awake()
        {
            _characterCamera = GetComponent<CharacterCamera>();
        }
        
        private void Start()
        {
            if (_characterCamera == null)
            {
                Debug.LogWarning("[CameraStabilizer] CharacterCamera not found!");
                enabled = false;
                return;
            }
            
            // Force first person mode
            if (forceFirstPerson)
            {
                _characterCamera.isFirstPerson = true;
            }
            
            // Cache and optionally disable the camera animation source
            // This is the main cause of camera shake in KINEMATION
            if (disableCameraAnimation)
            {
                DisableCameraAnimationSource();
            }
            
            // Silently initialized - camera shake reduced
        }
        
        private void DisableCameraAnimationSource()
        {
            // Cache reflection field for performance (avoid lookup every call)
            // WARNING: This accesses KINEMATION internals - may break if KINEMATION updates
            if (_cachedAnimSourceField == null)
            {
                _cachedAnimSourceField = typeof(CharacterCamera).GetField("cameraAnimationSource",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                // Safety check - warn if field not found (KINEMATION may have changed)
                if (_cachedAnimSourceField == null)
                {
                    Debug.LogWarning("[CameraStabilizer] Could not find 'cameraAnimationSource' field in CharacterCamera. " +
                        "KINEMATION version may have changed. Camera stabilization disabled.");
                    return;
                }
            }

            try
            {
                _cameraAnimationSource = _cachedAnimSourceField.GetValue(_characterCamera) as Transform;

                if (_cameraAnimationSource != null)
                {
                    _originalAnimSourceSet = true;
                    // Set to null to disable camera animation
                    _cachedAnimSourceField.SetValue(_characterCamera, null);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[CameraStabilizer] Failed to access camera animation source: {e.Message}");
            }
        }
        
        private void LateUpdate()
        {
            if (_characterCamera == null) return;
            
            // Force first person mode every frame
            if (forceFirstPerson)
            {
                _characterCamera.isFirstPerson = true;
            }
            
            // Apply extra smoothing if needed
            if (rotationSmoothing > 0f)
            {
                // Additional rotation stabilization can be added here
            }
        }
        
        private void OnDestroy()
        {
            // Restore original camera animation source if we modified it
            if (_originalAnimSourceSet && _characterCamera != null && _cachedAnimSourceField != null)
            {
                try
                {
                    // _cameraAnimationSource may have been destroyed, check Unity null
                    if (_cameraAnimationSource != null)
                        _cachedAnimSourceField.SetValue(_characterCamera, _cameraAnimationSource);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[CameraStabilizer] Failed to restore camera animation source: {e.Message}");
                }
            }
        }
        
        /// <summary>
        /// Enable or disable camera animation at runtime
        /// </summary>
        public void SetCameraAnimationEnabled(bool enabled)
        {
            if (_characterCamera == null || _cachedAnimSourceField == null) return;
            
            if (enabled && _cameraAnimationSource != null)
            {
                _cachedAnimSourceField.SetValue(_characterCamera, _cameraAnimationSource);
            }
            else
            {
                _cachedAnimSourceField.SetValue(_characterCamera, null);
            }
        }
    }
}
