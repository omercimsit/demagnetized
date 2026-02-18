using UnityEngine;
using CloneGame.Audio;

namespace CloneGame.FPS
{
    // Surface-aware footstep system - detects ground type via tag, physic material, or name
    [RequireComponent(typeof(CharacterController))]
    public class AdvancedFootstepSystem : MonoBehaviour
    {
        [Header("Database Reference")]
        [SerializeField] private FootstepDatabase _footstepDatabase;

        [Header("Step Settings")]
        [SerializeField] private float _walkStepInterval = 0.45f;
        [SerializeField] private float _runStepInterval = 0.3f;
        [SerializeField] private float _runSpeedThreshold = 4.5f;
        [SerializeField] private float _minTimeBetweenSteps = 0.2f; // prevents stacking

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

        private CharacterController _controller;
        private AudioSource _audioSource;

        private float _stepTimer;
        private bool _wasGrounded;
        private SurfaceType _currentSurface = SurfaceType.Default;
        private float _lastFootstepTime;
        private bool _wasMoving = false;

        private RaycastHit _groundHit;
        private SurfaceFootstepData _currentSurfaceData;
        private float _lastSurfaceCheckTime;

        // used by clone system for recording
        public event System.Action<SurfaceType, bool> OnFootstep;
        public event System.Action<SurfaceType> OnJumpLand;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
                _audioSource = gameObject.AddComponent<AudioSource>();

            ConfigureAudioSource();
        }

        private void Start()
        {
            if (_footstepDatabase == null)
            {
                _footstepDatabase = Resources.Load<FootstepDatabase>("FootstepDatabase");

                // fallback: search project in editor
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
                _footstepDatabase.Initialize();

            // TODO: maybe warn if db is still null after all this
        }

        private void ConfigureAudioSource()
        {
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.spatialBlend = _use3DAudio ? _spatialBlend : 0f;
            _audioSource.minDistance = _minDistance;
            _audioSource.maxDistance = _maxDistance;
            _audioSource.rolloffMode = AudioRolloffMode.Linear;
            _audioSource.outputAudioMixerGroup = null;
        }

        private void Update()
        {
            if (_controller == null || !_controller.enabled) return;

            bool isGrounded = _controller.isGrounded;
            Vector3 horizontalVelocity = _controller.velocity;
            horizontalVelocity.y = 0;
            float speed = horizontalVelocity.magnitude;

            // throttle surface check to ~10Hz
            if (isGrounded && Time.time - _lastSurfaceCheckTime > 0.1f)
            {
                _lastSurfaceCheckTime = Time.time;
                DetectSurface();
            }

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
                if (_wasMoving && _audioSource != null && _audioSource.isPlaying)
                    _audioSource.Stop();

                _stepTimer = Mathf.Min(_stepTimer, _walkStepInterval * 0.5f);
                _wasMoving = false;
            }

            // landing
            if (isGrounded && !_wasGrounded)
            {
                float verticalSpeed = Mathf.Abs(_controller.velocity.y);
                if (verticalSpeed > 2f)
                    PlayJumpLand(verticalSpeed);
            }

            _wasGrounded = isGrounded;
        }

        private void DetectSurface()
        {
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

                // only switch if we got a real surface back (not Default)
                if (detectedSurface != SurfaceType.Default && detectedSurface != _currentSurface)
                {
                    _currentSurface = detectedSurface;
                    _currentSurfaceData = _footstepDatabase?.GetSurfaceData(_currentSurface);
                }
                else if (detectedSurface == SurfaceType.Default && _currentSurface == SurfaceType.Default)
                {
                    _currentSurface = _defaultSurface;
                    _currentSurfaceData = _footstepDatabase?.GetSurfaceData(_currentSurface);
                }
            }
        }

        private SurfaceType GetSurfaceType(RaycastHit hit)
        {
            // try tag first
            string tag = hit.collider.tag;
            SurfaceType tagSurface = TagToSurface(tag);
            if (tagSurface != SurfaceType.Default)
                return tagSurface;

            // then physic material
            if (hit.collider is MeshCollider meshCollider)
            {
                if (meshCollider.sharedMaterial != null)
                    return PhysicMaterialToSurface(meshCollider.sharedMaterial.name);
            }
            else if (hit.collider.material != null)
            {
                return PhysicMaterialToSurface(hit.collider.material.name);
            }

            // last resort: object name
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
            if (Time.time - _lastFootstepTime < _minTimeBetweenSteps) return;

            _currentSurfaceData = _footstepDatabase.GetSurfaceData(_currentSurface);
            if (_currentSurfaceData == null) return;

            AudioClip clip = isRunning
                ? _currentSurfaceData.GetRandomRunningClip()
                : _currentSurfaceData.GetRandomWalkingClip();

            if (clip == null) return;

            if (_audioSource.isPlaying)
                _audioSource.Stop();

            _audioSource.pitch = Random.Range(_currentSurfaceData.minPitch, _currentSurfaceData.maxPitch);

            float volume = _masterVolume * _currentSurfaceData.volumeMultiplier;
            _audioSource.PlayOneShot(clip, volume);

            _lastFootstepTime = Time.time;

            OnFootstep?.Invoke(_currentSurface, isRunning);
        }

        private void PlayJumpLand(float impactSpeed)
        {
            if (_footstepDatabase == null || _audioSource == null) return;

            _currentSurfaceData = _footstepDatabase.GetSurfaceData(_currentSurface);

            AudioClip clip = _currentSurfaceData?.GetRandomJumpClip();
            if (clip == null)
                clip = _footstepDatabase.GetGenericJumpClip();

            if (clip == null) return;

            float volumeScale = Mathf.Clamp(impactSpeed / 10f, 0.5f, 1.5f);
            _audioSource.pitch = Random.Range(0.9f, 1.1f);
            _audioSource.PlayOneShot(clip, _masterVolume * volumeScale);

            OnJumpLand?.Invoke(_currentSurface);
        }

        // called from clone playback system
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
