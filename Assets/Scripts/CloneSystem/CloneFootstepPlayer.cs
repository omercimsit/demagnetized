using UnityEngine;
using CloneGame.Audio;

namespace CloneSystem
{
    // plays footstep sounds during clone playback
    // has a ghostly/echo effect to make clones sound different from the player
    // TODO: might be worth pooling the echo coroutines if there are many clones
    [RequireComponent(typeof(AudioSource))]
    public class CloneFootstepPlayer : MonoBehaviour
    {
        [Header("Database")]
        [SerializeField] private FootstepDatabase _footstepDatabase;

        [Header("Clone Audio Settings")]
        [SerializeField] [Range(0f, 1f)] private float _volume = 0.4f;
        [SerializeField] private bool _useEchoEffect = true;
        [SerializeField] private float _echoDelay = 0.1f;
        [SerializeField] private float _echoDecay = 0.3f;

        [Header("Ghostly Effect")]
        [SerializeField] private bool _useGhostlyPitch = true;
        [SerializeField] private float _ghostlyPitchMin = 0.85f;
        [SerializeField] private float _ghostlyPitchMax = 0.95f;
        [SerializeField] private bool _useLowPassFilter = true;
        [SerializeField] private float _lowPassCutoff = 3500f;

        [Header("Surface Detection")]
        [SerializeField] private float _raycastDistance = 1.5f;
        [SerializeField] private LayerMask _groundMask = ~0;

        private AudioSource _audioSource;
        private AudioSource _echoSource;
        private AudioLowPassFilter _lowPassFilter;

        private SurfaceType _currentSurface = SurfaceType.Concrete;
        private float _lastFootstepTime;

        private void Awake()
        {
            SetupAudio();
        }

        private void SetupAudio()
        {
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }

            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.spatialBlend = 1f; // 3D sound
            _audioSource.minDistance = 1f;
            _audioSource.maxDistance = 25f;
            _audioSource.rolloffMode = AudioRolloffMode.Linear;

            if (_useEchoEffect)
            {
                GameObject echoObj = new GameObject("CloneEcho");
                echoObj.transform.SetParent(transform);
                echoObj.transform.localPosition = Vector3.zero;

                _echoSource = echoObj.AddComponent<AudioSource>();
                _echoSource.playOnAwake = false;
                _echoSource.loop = false;
                _echoSource.spatialBlend = 1f;
                _echoSource.minDistance = 1f;
                _echoSource.maxDistance = 30f;
                _echoSource.rolloffMode = AudioRolloffMode.Linear;
            }

            if (_useLowPassFilter)
            {
                _lowPassFilter = gameObject.AddComponent<AudioLowPassFilter>();
                _lowPassFilter.cutoffFrequency = _lowPassCutoff;
                _lowPassFilter.lowpassResonanceQ = 1f;
            }
        }

        // called by AAACloneSystem when setting up a clone
        public void Initialize(FootstepDatabase database)
        {
            _footstepDatabase = database;
            if (_footstepDatabase != null)
            {
                _footstepDatabase.Initialize();
            }
        }

        // auto-detects surface from position
        public void PlayFootstep(bool isRunning)
        {
            DetectSurface();
            PlayFootstep(_currentSurface, isRunning);
        }

        public void PlayFootstep(SurfaceType surface, bool isRunning)
        {
            if (_footstepDatabase == null || _audioSource == null) return;

            // simple spam guard
            if (Time.time - _lastFootstepTime < 0.1f) return;

            var surfaceData = _footstepDatabase.GetSurfaceData(surface);
            if (surfaceData == null) return;

            AudioClip clip = isRunning
                ? surfaceData.GetRandomRunningClip()
                : surfaceData.GetRandomWalkingClip();

            if (clip == null) return;

            float pitch = _useGhostlyPitch
                ? Random.Range(_ghostlyPitchMin, _ghostlyPitchMax)
                : Random.Range(surfaceData.minPitch, surfaceData.maxPitch);

            _audioSource.pitch = pitch;
            _audioSource.PlayOneShot(clip, _volume * surfaceData.volumeMultiplier);

            if (_useEchoEffect && _echoSource != null)
            {
                StartCoroutine(PlayEchoDelayed(clip, pitch, _volume * surfaceData.volumeMultiplier * _echoDecay));
            }

            _lastFootstepTime = Time.time;
        }

        public void PlayJump()
        {
            DetectSurface();
            PlayJump(_currentSurface);
        }

        public void PlayJump(SurfaceType surface)
        {
            if (_footstepDatabase == null || _audioSource == null) return;

            var surfaceData = _footstepDatabase.GetSurfaceData(surface);
            AudioClip clip = surfaceData?.GetRandomJumpClip() ?? _footstepDatabase.GetGenericJumpClip();

            if (clip == null) return;

            float pitch = _useGhostlyPitch
                ? Random.Range(_ghostlyPitchMin, _ghostlyPitchMax)
                : Random.Range(0.9f, 1.1f);

            _audioSource.pitch = pitch;
            _audioSource.PlayOneShot(clip, _volume);

            if (_useEchoEffect && _echoSource != null)
            {
                StartCoroutine(PlayEchoDelayed(clip, pitch, _volume * _echoDecay));
            }
        }

        private System.Collections.IEnumerator PlayEchoDelayed(AudioClip clip, float pitch, float volume)
        {
            yield return new WaitForSeconds(_echoDelay);

            if (_echoSource != null && clip != null)
            {
                _echoSource.pitch = pitch * 0.95f; // slightly lower pitch for the echo
                _echoSource.PlayOneShot(clip, volume);
            }
        }

        private void DetectSurface()
        {
            Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, _raycastDistance, _groundMask))
            {
                _currentSurface = GetSurfaceFromHit(hit);
            }
        }

        private SurfaceType GetSurfaceFromHit(RaycastHit hit)
        {
            // CompareTag is zero-alloc, check tags first
            var collider = hit.collider;
            if (collider.CompareTag("Concrete") || collider.CompareTag("Floor"))
                return SurfaceType.Concrete;
            if (collider.CompareTag("Metal"))
                return SurfaceType.Metal;
            if (collider.CompareTag("Grass"))
                return SurfaceType.Grass;
            if (collider.CompareTag("Water"))
                return SurfaceType.Water;
            if (collider.CompareTag("Mud"))
                return SurfaceType.Mud;
            if (collider.CompareTag("Earth") || collider.CompareTag("Dirt"))
                return SurfaceType.EarthGround;
            if (collider.CompareTag("Ice") || collider.CompareTag("Snow"))
                return SurfaceType.IceAndSnow;
            if (collider.CompareTag("Gravel"))
                return SurfaceType.Gravel;
            if (collider.CompareTag("Wood"))
                return SurfaceType.Wood;

            // fallback to name check - IndexOf avoids ToLower() allocation
            string name = collider.gameObject.name;
            if (name.IndexOf("metal", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("steel", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return SurfaceType.Metal;
            if (name.IndexOf("concrete", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("floor", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return SurfaceType.Concrete;
            if (name.IndexOf("grass", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return SurfaceType.Grass;
            if (name.IndexOf("wood", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return SurfaceType.Wood;

            return SurfaceType.Concrete; // default when nothing matches
        }

        public void SetVolume(float volume)
        {
            _volume = Mathf.Clamp01(volume);
        }

        public void SetEchoEnabled(bool enabled)
        {
            _useEchoEffect = enabled;
        }

        // stops everything immediately when the clone gets destroyed/reset
        public void StopAudio()
        {
            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Stop();
            }
            if (_echoSource != null && _echoSource.isPlaying)
            {
                _echoSource.Stop();
            }
            StopAllCoroutines();
        }

        private void OnDestroy()
        {
            if (_echoSource != null)
            {
                Destroy(_echoSource.gameObject);
            }
        }
    }

    // data recorded alongside position frames during clone recording
    [System.Serializable]
    public struct FootstepEvent
    {
        public float time;
        public SurfaceType surface;
        public bool isRunning;
        public bool isJump;

        public FootstepEvent(float t, SurfaceType s, bool running, bool jump = false)
        {
            time = t;
            surface = s;
            isRunning = running;
            isJump = jump;
        }
    }
}
