using UnityEngine;
using CloneGame.Audio;

namespace CloneGame.FPS
{
    /// <summary>
    /// Advanced footstep system with surface detection.
    /// Supports walking, running, and jump sounds based on surface type.
    /// Uses raycast + tag/PhysicMaterial for surface detection.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class AdvancedFootstepSystem : MonoBehaviour
    {
        [Header("Database Reference")]
        [SerializeField] private FootstepDatabase _footstepDatabase;

        [Header("Step Settings")]
        [SerializeField] private float _walkStepInterval = 0.45f;
        [SerializeField] private float _runStepInterval = 0.3f;
        [SerializeField] private float _runSpeedThreshold = 4.5f;
        [SerializeField] private float _minTimeBetweenSteps = 0.2f; // Prevent sound stacking

        [Header("Audio Settings")]
        [SerializeField] [Range(0f, 1f)] private float _masterVolume = 0.5f;
        [SerializeField] private bool _use3DAudio = true;
        [SerializeField] private float _spatialBlend = 1f;

        [Header("Surface Detection")]
        [SerializeField] private float _raycastDistance = 1.5f;
        [SerializeField] private LayerMask _groundMask = ~0;
        [SerializeField] private bool _lockSurfaceType = false;
        [SerializeField] private SurfaceType _defaultSurface = SurfaceType.Concrete;

        [Header("Audio Source Settings")]
        [SerializeField] private float _minDistance = 1f;
        [SerializeField] private float _maxDistance = 20f;

        // Components
        private CharacterController _controller;
        private AudioSource _audioSource;

        // State
        private float _stepTimer;
        private bool _wasGrounded;
        private SurfaceType _currentSurface = SurfaceType.Default;
        private float _lastFootstepTime;
        private bool _wasMoving = false;

        // Cache
        private RaycastHit _groundHit;
        private SurfaceFootstepData _currentSurfaceData;
        private float _lastSurfaceCheckTime;

        // Events for clone recording
        public event System.Action<SurfaceType, bool> OnFootstep; // isRunning
        public event System.Action<SurfaceType> OnJumpLand;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();

            // Setup audio source
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }

            ConfigureAudioSource();
        }

        private void Start()
        {
            // Auto-find FootstepDatabase if not assigned
            if (_footstepDatabase == null)
            {
                _footstepDatabase = Resources.Load<FootstepDatabase>("FootstepDatabase");

                // Try finding in project
                if (_footstepDatabase == null)
                {
#if UNITY_EDITOR
                    string[] guids = UnityEditor.AssetDatabase.FindAssets("t:FootstepDatabase");
                    if (guids.Length > 0)
                    {
                        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                        _footstepDatabase = UnityEditor.AssetDatabase.LoadAssetAtPath<FootstepDatabase>(path);
                    }
#endif
                }
            }

            if (_footstepDatabase != null)
            {
                _footstepDatabase.Initialize();
                // Silently initialized - no spam
            }
            // Only warn if footsteps won't work
            // else { Debug.LogWarning("[AdvancedFootstepSystem] No FootstepDatabase found!"); }
        }

        private void ConfigureAudioSource()
        {
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.spatialBlend = _use3DAudio ? _spatialBlend : 0f;
            _audioSource.minDistance = _minDistance;
            _audioSource.maxDistance = _maxDistance;
            _audioSource.rolloffMode = AudioRolloffMode.Linear;
            _audioSource.outputAudioMixerGroup = null; // Can be set to SFX mixer
        }

        private void Update()
        {
            if (_controller == null || !_controller.enabled) return;

            bool isGrounded = _controller.isGrounded;
            Vector3 horizontalVelocity = _controller.velocity;
            horizontalVelocity.y = 0;
            float speed = horizontalVelocity.magnitude;

            // Detect current surface (throttled to 10Hz)
            if (isGrounded && Time.time - _lastSurfaceCheckTime > 0.1f)
            {
                _lastSurfaceCheckTime = Time.time;
                DetectSurface();
            }

            // Step logic
            bool isMoving = isGrounded && speed > 0.1f;

            if (isMoving)
            {
                bool isRunning = speed > _runSpeedThreshold;
                float stepInterval = isRunning ? _runStepInterval : _walkStepInterval;

                _stepTimer += Time.deltaTime;

                if (_stepTimer >= stepInterval)
                {
                    PlayFootstep(isRunning);
                    _stepTimer = 0f;
                }

                _wasMoving = true;
            }
            else
            {
                // STOP audio when player stops moving
                if (_wasMoving && _audioSource != null && _audioSource.isPlaying)
                {
                    _audioSource.Stop();
                }

                // Reset timer when not moving
                _stepTimer = Mathf.Min(_stepTimer, _walkStepInterval * 0.5f);
                _wasMoving = false;
            }

            // Landing detection
            if (isGrounded && !_wasGrounded)
            {
                float verticalSpeed = Mathf.Abs(_controller.velocity.y);
                if (verticalSpeed > 2f)
                {
                    PlayJumpLand(verticalSpeed);
                }
            }

            _wasGrounded = isGrounded;
        }

        private void DetectSurface()
        {
            // If surface is locked, always use default
            if (_lockSurfaceType)
            {
                if (_currentSurface != _defaultSurface)
                {
                    _currentSurface = _defaultSurface;
                    _currentSurfaceData = _footstepDatabase?.GetSurfaceData(_currentSurface);
                }
                return;
            }

            Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;

            if (Physics.Raycast(rayOrigin, Vector3.down, out _groundHit, _raycastDistance, _groundMask))
            {
                SurfaceType detectedSurface = GetSurfaceType(_groundHit);

                // Only change surface if we detect a NON-default surface
                // This prevents random switching when detection fails
                if (detectedSurface != SurfaceType.Default && detectedSurface != _currentSurface)
                {
                    _currentSurface = detectedSurface;
                    _currentSurfaceData = _footstepDatabase?.GetSurfaceData(_currentSurface);
                }
                // If detected default, use the configured default surface
                else if (detectedSurface == SurfaceType.Default && _currentSurface == SurfaceType.Default)
                {
                    _currentSurface = _defaultSurface;
                    _currentSurfaceData = _footstepDatabase?.GetSurfaceData(_currentSurface);
                }
            }
        }

        private SurfaceType GetSurfaceType(RaycastHit hit)
        {
            // Method 1: Check tag
            string tag = hit.collider.tag;
            SurfaceType tagSurface = TagToSurface(tag);
            if (tagSurface != SurfaceType.Default)
                return tagSurface;

            // Method 2: Check PhysicMaterial name
            if (hit.collider is MeshCollider meshCollider)
            {
                if (meshCollider.sharedMaterial != null)
                {
                    return PhysicMaterialToSurface(meshCollider.sharedMaterial.name);
                }
            }
            else if (hit.collider.material != null)
            {
                return PhysicMaterialToSurface(hit.collider.material.name);
            }

            // Method 3: Check GameObject name for hints
            string objName = hit.collider.gameObject.name.ToLower();
            return NameToSurface(objName);
        }

        private SurfaceType TagToSurface(string tag)
        {
            switch (tag.ToLower())
            {
                case "concrete":
                case "floor":
                    return SurfaceType.Concrete;
                case "metal":
                    return SurfaceType.Metal;
                case "grass":
                    return SurfaceType.Grass;
                case "water":
                    return SurfaceType.Water;
                case "mud":
                    return SurfaceType.Mud;
                case "earth":
                case "dirt":
                    return SurfaceType.EarthGround;
                case "ice":
                case "snow":
                    return SurfaceType.IceAndSnow;
                case "gravel":
                    return SurfaceType.Gravel;
                case "wood":
                    return SurfaceType.Wood;
                default:
                    return SurfaceType.Default;
            }
        }

        private SurfaceType PhysicMaterialToSurface(string materialName)
        {
            string lowerName = materialName.ToLower();

            if (lowerName.Contains("concrete") || lowerName.Contains("stone"))
                return SurfaceType.Concrete;
            if (lowerName.Contains("metal"))
                return SurfaceType.Metal;
            if (lowerName.Contains("grass"))
                return SurfaceType.Grass;
            if (lowerName.Contains("water"))
                return SurfaceType.Water;
            if (lowerName.Contains("mud"))
                return SurfaceType.Mud;
            if (lowerName.Contains("earth") || lowerName.Contains("dirt"))
                return SurfaceType.EarthGround;
            if (lowerName.Contains("ice") || lowerName.Contains("snow"))
                return SurfaceType.IceAndSnow;
            if (lowerName.Contains("gravel"))
                return SurfaceType.Gravel;
            if (lowerName.Contains("wood"))
                return SurfaceType.Wood;

            return SurfaceType.Default;
        }

        private SurfaceType NameToSurface(string name)
        {
            if (name.Contains("concrete") || name.Contains("floor") || name.Contains("tile"))
                return SurfaceType.Concrete;
            if (name.Contains("metal") || name.Contains("steel") || name.Contains("iron"))
                return SurfaceType.Metal;
            if (name.Contains("grass") || name.Contains("lawn"))
                return SurfaceType.Grass;
            if (name.Contains("water") || name.Contains("puddle"))
                return SurfaceType.Water;
            if (name.Contains("mud") || name.Contains("swamp"))
                return SurfaceType.Mud;
            if (name.Contains("earth") || name.Contains("dirt") || name.Contains("ground") || name.Contains("soil"))
                return SurfaceType.EarthGround;
            if (name.Contains("ice") || name.Contains("snow") || name.Contains("frost"))
                return SurfaceType.IceAndSnow;
            if (name.Contains("gravel") || name.Contains("pebble"))
                return SurfaceType.Gravel;
            if (name.Contains("wood") || name.Contains("plank") || name.Contains("board"))
                return SurfaceType.Wood;

            return SurfaceType.Default;
        }

        private void PlayFootstep(bool isRunning)
        {
            if (_footstepDatabase == null || _audioSource == null) return;

            // Prevent sound stacking - ensure minimum time between steps
            if (Time.time - _lastFootstepTime < _minTimeBetweenSteps) return;

            _currentSurfaceData = _footstepDatabase.GetSurfaceData(_currentSurface);
            if (_currentSurfaceData == null) return;

            AudioClip clip = isRunning
                ? _currentSurfaceData.GetRandomRunningClip()
                : _currentSurfaceData.GetRandomWalkingClip();

            if (clip == null) return;

            // Stop any currently playing footstep to prevent overlap
            if (_audioSource.isPlaying)
            {
                _audioSource.Stop();
            }

            // Randomize pitch
            _audioSource.pitch = Random.Range(_currentSurfaceData.minPitch, _currentSurfaceData.maxPitch);

            float volume = _masterVolume * _currentSurfaceData.volumeMultiplier;
            _audioSource.PlayOneShot(clip, volume);

            _lastFootstepTime = Time.time;

            // Fire event for clone recording
            OnFootstep?.Invoke(_currentSurface, isRunning);
        }

        private void PlayJumpLand(float impactSpeed)
        {
            if (_footstepDatabase == null || _audioSource == null) return;

            _currentSurfaceData = _footstepDatabase.GetSurfaceData(_currentSurface);

            AudioClip clip = _currentSurfaceData?.GetRandomJumpClip();
            if (clip == null)
            {
                clip = _footstepDatabase.GetGenericJumpClip();
            }

            if (clip == null) return;

            // Scale volume by impact
            float volumeScale = Mathf.Clamp(impactSpeed / 10f, 0.5f, 1.5f);
            _audioSource.pitch = Random.Range(0.9f, 1.1f);
            _audioSource.PlayOneShot(clip, _masterVolume * volumeScale);

            // Fire event
            OnJumpLand?.Invoke(_currentSurface);
        }

        /// <summary>
        /// Manually play footstep (for clone playback)
        /// </summary>
        public void PlayFootstepManual(SurfaceType surface, bool isRunning, float volume = 1f)
        {
            if (_footstepDatabase == null || _audioSource == null) return;

            var surfaceData = _footstepDatabase.GetSurfaceData(surface);
            if (surfaceData == null) return;

            AudioClip clip = isRunning
                ? surfaceData.GetRandomRunningClip()
                : surfaceData.GetRandomWalkingClip();

            if (clip == null) return;

            _audioSource.pitch = Random.Range(surfaceData.minPitch, surfaceData.maxPitch);
            _audioSource.PlayOneShot(clip, _masterVolume * surfaceData.volumeMultiplier * volume);
        }

        /// <summary>
        /// Play jump sound manually (for clone playback)
        /// </summary>
        public void PlayJumpManual(SurfaceType surface, float volume = 1f)
        {
            if (_footstepDatabase == null || _audioSource == null) return;

            var surfaceData = _footstepDatabase.GetSurfaceData(surface);
            AudioClip clip = surfaceData?.GetRandomJumpClip() ?? _footstepDatabase.GetGenericJumpClip();

            if (clip == null) return;

            _audioSource.pitch = Random.Range(0.9f, 1.1f);
            _audioSource.PlayOneShot(clip, _masterVolume * volume);
        }

        public SurfaceType CurrentSurface => _currentSurface;

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 origin = transform.position + Vector3.up * 0.1f;
            Gizmos.DrawLine(origin, origin + Vector3.down * _raycastDistance);
        }
#endif
    }
}
