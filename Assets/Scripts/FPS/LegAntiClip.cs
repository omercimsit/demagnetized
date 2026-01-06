using UnityEngine;

namespace CAS_Demo.Scripts.FPS
{
    /// <summary>
    /// Pushes thighs apart while walking to prevent leg mesh clipping.
    /// Runs in LateUpdate AFTER all animation systems.
    /// </summary>
    [DefaultExecutionOrder(900)]
    public class LegAntiClip : MonoBehaviour
    {
        [Tooltip("Degrees to push each thigh outward while walking.")]
        [Range(0f, 25f)]
        public float walkDegrees = 5f;

        private Transform _leftThigh;
        private Transform _rightThigh;
        private Transform _rootTransform;
        private float _currentDegrees;
        private Vector3 _lastPos;
        private bool _initialized;

        private void Start()
        {
            var animator = GetComponent<Animator>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();

            if (animator == null || !animator.isHuman)
            {
                enabled = false;
                return;
            }

            _leftThigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _rightThigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);

            if (_leftThigh == null || _rightThigh == null)
            {
                enabled = false;
                return;
            }

            _rootTransform = transform.root;
            _lastPos = _rootTransform.position;
            _initialized = true;
        }

        private void LateUpdate()
        {
            if (!_initialized) return;

            Vector3 pos = _rootTransform.position;
            Vector3 delta = pos - _lastPos;
            delta.y = 0f;
            float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.001f);
            _lastPos = pos;

            float target = speed > 0.1f ? walkDegrees : 0f;
            _currentDegrees = Mathf.MoveTowards(_currentDegrees, target, 8f * Time.deltaTime);

            if (_currentDegrees < 0.01f) return;

            Vector3 euler = new Vector3(_currentDegrees, 0f, 0f);
            _leftThigh.localRotation *= Quaternion.Euler(euler);
            _rightThigh.localRotation *= Quaternion.Euler(euler);
        }
    }
}
