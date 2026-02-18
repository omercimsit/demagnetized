using UnityEngine;

namespace CloneGame.Puzzle
{
    // Player grabs the wheel with E, then uses A/D (or mouse X) to rotate it.
    // While grabbed, player position is locked so they don't walk away mid-crank.
    public class WheelController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LightPivotController _lightPivot;
        [SerializeField] private Transform _wheelVisual;

        [Header("Interaction Settings")]
        [SerializeField] private float _interactionDistance = 2f;
        [SerializeField] private KeyCode _interactKey = KeyCode.E;
        [SerializeField] private KeyCode _rotateLeftKey = KeyCode.A;
        [SerializeField] private KeyCode _rotateRightKey = KeyCode.D;

        [Header("Wheel Settings")]
        [SerializeField] private float _wheelRotationMultiplier = 3f;

        private Transform _player;
        private CharacterController _characterController;
        private bool _isGrabbed = false;
        private float _wheelRotation = 0f;
        private Vector3 _lockedPosition;

        // other scripts can check this to block player input while using the wheel
        public static bool IsPlayerLockedByWheel { get; private set; }

        private void Start()
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                _player = playerObj.transform;
                _characterController = playerObj.GetComponent<CharacterController>();
            }
        }

        private void Update()
        {
            if (_player == null || _lightPivot == null) return;

            float distance = Vector3.Distance(transform.position, _player.position);
            bool isInRange = distance <= _interactionDistance;

            if (isInRange && Input.GetKeyDown(_interactKey))
            {
                _isGrabbed = !_isGrabbed;
                IsPlayerLockedByWheel = _isGrabbed;

                if (_isGrabbed)
                    _lockedPosition = _player.position;

                Debug.Log(_isGrabbed ? "[Wheel] Grabbed" : "[Wheel] Released");
            }

            // auto-release if player somehow gets out of range
            if (_isGrabbed && !isInRange)
            {
                _isGrabbed = false;
                IsPlayerLockedByWheel = false;
                Debug.Log("[Wheel] Released (out of range)");
            }

            if (_isGrabbed)
            {
                // keep player pinned in place
                if (_characterController != null)
                {
                    _characterController.enabled = false;
                    _player.position = _lockedPosition;
                    _characterController.enabled = true;
                }

                HandleRotationInput();
            }
        }

        private void HandleRotationInput()
        {
            float rotationInput = 0f;

            if (Input.GetKey(_rotateLeftKey)) rotationInput = -1f;
            else if (Input.GetKey(_rotateRightKey)) rotationInput = 1f;

            // TODO: might want to scale mouse sensitivity separately
            rotationInput += Input.GetAxis("Mouse X") * 0.5f;

            if (Mathf.Abs(rotationInput) > 0.01f)
            {
                _lightPivot.Rotate(rotationInput);

                _wheelRotation += rotationInput * _wheelRotationMultiplier * Time.deltaTime * 100f;
                if (_wheelVisual != null)
                    _wheelVisual.localRotation = Quaternion.Euler(0f, 0f, _wheelRotation);
            }
        }

        public bool IsGrabbed => _isGrabbed;

        private void OnDisable()
        {
            // make sure player doesn't stay frozen if this gets disabled mid-grab
            if (_isGrabbed)
            {
                _isGrabbed = false;
                IsPlayerLockedByWheel = false;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, _interactionDistance);
        }
    }
}
