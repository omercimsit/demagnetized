using UnityEngine;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Camera;

namespace CAS_Demo.Scripts.FPS
{
    // Kills KINEMATION camera shake by nulling out cameraAnimationSource via reflection.
    // Attach to the same GO as CharacterCamera.
    [RequireComponent(typeof(CharacterCamera))]
    public class CameraStabilizer : MonoBehaviour
    {
        [Header("Stabilization Settings")]
        [Tooltip("Completely disable KINEMATION camera animation shake")]
        [SerializeField] private bool disableCameraAnimation = true;

        [Tooltip("Reduce camera shake intensity (0 = no shake, 1 = full shake)")]
        [Range(0f, 1f)]
        [SerializeField] private float shakeMultiplier = 0f;

        [Tooltip("Extra smoothing for camera rotation")]
        [SerializeField] private float rotationSmoothing = 0f;

        [Header("First Person Lock")]
        [Tooltip("Force first person mode")]
        [SerializeField] private bool forceFirstPerson = true;

        private CharacterCamera _characterCamera;
        private Transform _cameraAnimationSource;
        private bool _originalAnimSourceSet = false;
        // cache the field so we don't do a full reflection lookup every call
        private System.Reflection.FieldInfo _cachedAnimSourceField;

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

            if (forceFirstPerson)
                _characterCamera.isFirstPerson = true;

            // the main cause of camera shake in KINEMATION is the animation source transform
            if (disableCameraAnimation)
                DisableCameraAnimationSource();
        }

        private void DisableCameraAnimationSource()
        {
            // WARNING: accesses KINEMATION internals - may break on KINEMATION updates
            if (_cachedAnimSourceField == null)
            {
                _cachedAnimSourceField = typeof(CharacterCamera).GetField("cameraAnimationSource",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

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

            if (forceFirstPerson)
                _characterCamera.isFirstPerson = true;

            // TODO: could add actual smoothing here if needed later
            if (rotationSmoothing > 0f)
            {
                // placeholder - leaving for future use
            }
        }

        private void OnDestroy()
        {
            if (_originalAnimSourceSet && _characterCamera != null && _cachedAnimSourceField != null)
            {
                try
                {
                    // _cameraAnimationSource could be destroyed already, check for Unity null
                    if (_cameraAnimationSource != null)
                        _cachedAnimSourceField.SetValue(_characterCamera, _cameraAnimationSource);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[CameraStabilizer] Failed to restore camera animation source: {e.Message}");
                }
            }
        }

        // toggle camera animation at runtime if needed
        public void SetCameraAnimationEnabled(bool enabled)
        {
            if (_characterCamera == null || _cachedAnimSourceField == null) return;

            if (enabled && _cameraAnimationSource != null)
                _cachedAnimSourceField.SetValue(_characterCamera, _cameraAnimationSource);
            else
                _cachedAnimSourceField.SetValue(_characterCamera, null);
        }
    }
}
