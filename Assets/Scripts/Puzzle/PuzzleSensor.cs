using UnityEngine;
using UnityEngine.Events;
using CloneSystem;

namespace CloneGame.Puzzle
{
    public enum SensorRequirement
    {
        WantsShadow,
        WantsLight
    }

    /// <summary>
    /// Puzzle sensor that detects if it's in shadow or light.
    /// Uses raycasting to determine if light is blocked by clone.
    /// </summary>
    public class PuzzleSensor : MonoBehaviour
    {
        [Header("Sensor Configuration")]
        [SerializeField] private SensorRequirement _requirement = SensorRequirement.WantsShadow;
        [SerializeField] private string _sensorName = "Sensor";

        [Header("References")]
        [SerializeField] private Transform _lightSource;
        [SerializeField] private Renderer _indicatorRenderer;

        [Header("Detection Settings")]
        [SerializeField] private LayerMask _shadowCasterMask = ~0;
        [SerializeField] private float _raycastOffset = 0.5f;

        [Header("Visual Feedback")]
        [SerializeField] private Color _activeColor = Color.green;
        [SerializeField] private Color _inactiveColor = Color.red;

        [Header("Events")]
        public UnityEvent OnActivated;
        public UnityEvent OnDeactivated;

        private bool _isActive = false;
        private bool _isInShadow = false;
        private MaterialPropertyBlock _propBlock;
        private Light _cachedLight; // Cached to avoid per-frame GetComponent<Light>
        private static readonly RaycastHit[] _hitBuffer = new RaycastHit[16]; // NonAlloc buffer

        // Pre-allocated raycast offsets (avoids per-frame array creation)
        private static readonly Vector3[] _rayOffsets = new Vector3[]
        {
            Vector3.zero,
            Vector3.right * 0.2f,
            Vector3.left * 0.2f,
            Vector3.forward * 0.2f,
            Vector3.back * 0.2f
        };

        public bool IsActive => _isActive;
        public string SensorName => _sensorName;

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
        }

        private void Start()
        {
            if (_lightSource == null)
            {
                GameObject lightObj = GameObject.Find("SpotLight_Rotating");
                if (lightObj != null) _lightSource = lightObj.transform;
            }
            // Cache Light component once
            if (_lightSource != null)
                _cachedLight = _lightSource.GetComponent<Light>();
            UpdateVisuals();
        }

        private void Update()
        {
            if (_lightSource == null) return;
            CheckShadowState();
            UpdateActiveState();
        }

        private void CheckShadowState()
        {
            Vector3 sensorPos = transform.position + Vector3.up * _raycastOffset;
            Vector3 lightPos = _lightSource.position;
            Vector3 direction = lightPos - sensorPos;
            float distance = direction.magnitude;

            // Multiple raycasts using pre-allocated offset array (zero GC)
            foreach (var offset in _rayOffsets)
            {
                Vector3 rayStart = sensorPos + offset;
                if (Physics.Raycast(rayStart, (lightPos - rayStart).normalized, out RaycastHit hit, distance, _shadowCasterMask))
                {
                    if (hit.transform != _lightSource && hit.transform.root != _lightSource.root)
                    {
                        // Any non-trigger collider blocking light = shadow
                        if (hit.collider != null && !hit.collider.isTrigger)
                        {
                            _isInShadow = true;
                            return;
                        }
                    }
                }
            }

            // Use RaycastNonAlloc to avoid per-frame array allocation
            int hitCount = Physics.RaycastNonAlloc(sensorPos, direction.normalized, _hitBuffer, distance, _shadowCasterMask);
            for (int i = 0; i < hitCount; i++)
            {
                ref var hit = ref _hitBuffer[i];
                if (hit.transform == _lightSource || hit.transform.root == _lightSource.root) continue;

                if (hit.collider != null && !hit.collider.isTrigger)
                {
                    _isInShadow = true;
                    return;
                }
            }

            // Use cached Light component (avoid per-frame GetComponent)
            if (_cachedLight != null && _cachedLight.type == LightType.Spot)
            {
                Vector3 lightForward = _lightSource.forward;
                Vector3 toSensor = (sensorPos - lightPos).normalized;
                float angle = Vector3.Angle(lightForward, toSensor);

                if (angle > _cachedLight.spotAngle * 0.5f || distance > _cachedLight.range)
                {
                    _isInShadow = true;
                    return;
                }
            }

            _isInShadow = false;
        }

        private void UpdateActiveState()
        {
            bool shouldBeActive = _requirement == SensorRequirement.WantsShadow ? _isInShadow : !_isInShadow;

            if (shouldBeActive != _isActive)
            {
                _isActive = shouldBeActive;

                if (_isActive)
                {
                    OnActivated?.Invoke();
                    Debug.Log($"[Sensor] {_sensorName} ACTIVATED");
                }
                else
                {
                    OnDeactivated?.Invoke();
                    Debug.Log($"[Sensor] {_sensorName} DEACTIVATED");
                }

                UpdateVisuals();
            }
        }

        private void UpdateVisuals()
        {
            if (_indicatorRenderer == null) return;

            _indicatorRenderer.GetPropertyBlock(_propBlock);
            Color color = _isActive ? _activeColor : _inactiveColor;
            _propBlock.SetColor("_BaseColor", color);
            _propBlock.SetColor("_EmissiveColor", color * 2f);
            _indicatorRenderer.SetPropertyBlock(_propBlock);
        }

        private void OnDrawGizmos()
        {
            if (_lightSource == null) return;

            Vector3 sensorPos = transform.position + Vector3.up * _raycastOffset;
            Gizmos.color = _isInShadow ? Color.red : Color.yellow;
            Gizmos.DrawLine(sensorPos, _lightSource.position);
            Gizmos.color = _isActive ? Color.green : Color.red;
            Gizmos.DrawWireSphere(sensorPos, 0.3f);
        }
    }
}
