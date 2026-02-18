using UnityEngine;

namespace CAS_Demo.Scripts.FPS
{
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(AudioSource))]
    public class FootstepSystem : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float stepDistance = 1.8f;
        [SerializeField] private float runStepMultiplier = 0.7f; // running plays steps more frequently
        [SerializeField] [Range(0f, 1f)] private float volume = 0.4f;

        [Header("Sounds")]
        [SerializeField] private AudioClip[] footstepSounds;
        [SerializeField] private AudioClip landSound;
        [SerializeField] private AudioClip jumpSound;

        private CharacterController _controller;
        private AudioSource _audioSource;
        private float _distanceTraveled;
        private bool _wasGrounded;

        private void Start()
        {
            _controller = GetComponent<CharacterController>();
            _audioSource = GetComponent<AudioSource>();

            if (_audioSource)
            {
                _audioSource.spatialBlend = 1.0f;
                _audioSource.playOnAwake = false;
            }
        }

        private void Update()
        {
            if (_controller == null || !_controller.enabled) return;

            bool isGrounded = _controller.isGrounded;
            // ignore vertical component when calculating step distance
            Vector3 horizontalVelocity = _controller.velocity;
            horizontalVelocity.y = 0;
            float speed = horizontalVelocity.magnitude;

            if (isGrounded && speed > 0.1f)
            {
                float stepThreshold = stepDistance;
                if (speed > 4.5f) stepThreshold *= runStepMultiplier;

                _distanceTraveled += speed * Time.deltaTime;

                if (_distanceTraveled > stepThreshold)
                {
                    PlayFootstep();
                    _distanceTraveled = 0f;
                }
            }
            else
            {
                // partial reset so the first step after stopping isn't instant
                _distanceTraveled = Mathf.Min(_distanceTraveled, stepDistance * 0.5f);
            }

            // landing thud
            if (isGrounded && !_wasGrounded && _controller.velocity.y < -3f)
            {
                if (landSound && _audioSource != null) _audioSource.PlayOneShot(landSound, volume * 1.5f);
                else PlayFootstep();
            }

            _wasGrounded = isGrounded;
        }

        private void PlayFootstep()
        {
            if (footstepSounds == null || footstepSounds.Length == 0 || _audioSource == null) return;

            AudioClip clip = footstepSounds[Random.Range(0, footstepSounds.Length)];
            if (clip)
            {
                _audioSource.pitch = Random.Range(0.9f, 1.1f);
                _audioSource.PlayOneShot(clip, volume);
            }
        }
    }
}
