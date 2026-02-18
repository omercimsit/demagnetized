using UnityEngine;
using System.Collections.Generic;

namespace Demagnetized.Cinematic
{
    // The black flooding liquid that chases you down the corridor.
    // Moves on its own and reacts to walls with a wave-like bounce.
    // TODO: add a speed ramp based on player health or story events
    public class ChasingShadow : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float _moveSpeed = 5f;
        [SerializeField] private Vector3 _moveDirection = Vector3.back;
        [SerializeField] private float _floorY = 0.5f;

        [Header("Wave Effect")]
        [SerializeField] private float _waveAmplitude = 1.5f;
        [SerializeField] private float _waveFrequency = 3f;
        [SerializeField] private float _sideWaveAmplitude = 2f;
        [SerializeField] private float _sideWaveFrequency = 2f;

        [Header("Wall Collision")]
        [SerializeField] private bool _reactToWalls = true;
        [SerializeField] private float _wallDetectionRange = 5f;
        [SerializeField] private LayerMask _wallLayers = -1;
        [SerializeField] private float _wallBounceIntensity = 2f;

        [Header("Scale Pulse")]
        [SerializeField] private float _baseScaleX = 15f;
        [SerializeField] private float _baseScaleY = 8f;
        [SerializeField] private float _scalePulseAmount = 0.3f;
        [SerializeField] private float _scalePulseSpeed = 4f;

        // child objects that act as reaching tendrils
        [Header("Tendrils")]
        [SerializeField] private bool _createTendrils = true;
        [SerializeField] private int _tendrilCount = 5;
        [SerializeField] private float _tendrilSpread = 3f;
        [SerializeField] private float _tendrilWaveOffset = 0.5f;

        [Header("Camera")]
        [SerializeField] private Transform _cameraTransform;
        [SerializeField] private bool _lookAtCamera = true;

        private List<Transform> _tendrils = new List<Transform>();
        private float _timeOffset;
        private Vector3 _wallReactionOffset;
        private float _wallReactionDecay = 5f;

        private void Start()
        {
            _timeOffset = Random.Range(0f, 10f);

            if (_cameraTransform == null && Camera.main != null)
                _cameraTransform = Camera.main.transform;

            if (_createTendrils)
                CreateTendrils();
        }

        private void Update()
        {
            MoveForward();
            ApplyWaveMotion();

            if (_reactToWalls)
                DetectAndReactToWalls();

            ApplyScalePulse();

            if (_lookAtCamera && _cameraTransform != null)
                LookAtCamera();

            UpdateTendrils();
        }

        private void MoveForward()
        {
            transform.position += _moveDirection.normalized * _moveSpeed * Time.deltaTime;
        }

        private void ApplyWaveMotion()
        {
            float time = Time.time + _timeOffset;

            float waveY = Mathf.Sin(time * _waveFrequency) * _waveAmplitude;
            float waveX = Mathf.Sin(time * _sideWaveFrequency + 1.5f) * _sideWaveAmplitude;

            Vector3 pos = transform.position;
            pos.y = _floorY + waveY + Mathf.Abs(waveY) * 0.5f;
            pos.x += waveX * Time.deltaTime;

            // wall reaction decays naturally over time
            pos += _wallReactionOffset;
            _wallReactionOffset = Vector3.Lerp(_wallReactionOffset, Vector3.zero, _wallReactionDecay * Time.deltaTime);

            transform.position = pos;
        }

        private void DetectAndReactToWalls()
        {
            Vector3 rightDir = transform.right;
            Vector3 leftDir = -transform.right;

            RaycastHit hit;

            if (Physics.Raycast(transform.position, rightDir, out hit, _wallDetectionRange, _wallLayers))
            {
                float proximity = 1f - (hit.distance / _wallDetectionRange);
                _wallReactionOffset += Vector3.up * proximity * _wallBounceIntensity * Time.deltaTime;
                _wallReactionOffset += leftDir * proximity * _wallBounceIntensity * 0.5f * Time.deltaTime;
            }

            if (Physics.Raycast(transform.position, leftDir, out hit, _wallDetectionRange, _wallLayers))
            {
                float proximity = 1f - (hit.distance / _wallDetectionRange);
                _wallReactionOffset += Vector3.up * proximity * _wallBounceIntensity * Time.deltaTime;
                _wallReactionOffset += rightDir * proximity * _wallBounceIntensity * 0.5f * Time.deltaTime;
            }

            // ceiling check keeps it from going too high in narrow sections
            if (Physics.Raycast(transform.position, Vector3.up, out hit, _wallDetectionRange * 0.5f, _wallLayers))
            {
                float proximity = 1f - (hit.distance / (_wallDetectionRange * 0.5f));
                _wallReactionOffset += Vector3.down * proximity * _wallBounceIntensity * Time.deltaTime;
            }
        }

        private void ApplyScalePulse()
        {
            float time = Time.time + _timeOffset;
            float pulse = Mathf.Sin(time * _scalePulseSpeed) * _scalePulseAmount;

            Vector3 scale = new Vector3(
                _baseScaleX + pulse * 2f,
                _baseScaleY + pulse,
                1f
            );

            transform.localScale = scale;
        }

        private void LookAtCamera()
        {
            Vector3 lookDir = _cameraTransform.position - transform.position;
            lookDir.y = 0;

            if (lookDir != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(-lookDir);
        }

        private void CreateTendrils()
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();

            if (meshFilter == null || meshRenderer == null) return;

            for (int i = 0; i < _tendrilCount; i++)
            {
                GameObject tendril = new GameObject($"Tendril_{i}");
                tendril.transform.SetParent(transform);

                float xOffset = (i - _tendrilCount / 2f) * _tendrilSpread;
                float zOffset = Random.Range(-1f, 1f);
                tendril.transform.localPosition = new Vector3(xOffset, 0, zOffset);
                tendril.transform.localRotation = Quaternion.identity;
                tendril.transform.localScale = Vector3.one * Random.Range(0.3f, 0.6f);

                MeshFilter tf = tendril.AddComponent<MeshFilter>();
                MeshRenderer tr = tendril.AddComponent<MeshRenderer>();
                tf.sharedMesh = meshFilter.sharedMesh;
                tr.sharedMaterial = meshRenderer.sharedMaterial;

                _tendrils.Add(tendril.transform);
            }
        }

        private void UpdateTendrils()
        {
            float time = Time.time + _timeOffset;

            for (int i = 0; i < _tendrils.Count; i++)
            {
                Transform tendril = _tendrils[i];
                if (tendril == null) continue;

                // each tendril is offset in phase so they don't all move in sync
                float phase = i * _tendrilWaveOffset;
                float waveY = Mathf.Sin((time + phase) * _waveFrequency * 1.5f) * (_waveAmplitude * 0.5f);
                float waveX = Mathf.Sin((time + phase) * _sideWaveFrequency * 1.2f) * 0.3f;

                Vector3 localPos = tendril.localPosition;
                localPos.y = waveY;
                localPos.x += waveX * Time.deltaTime;
                tendril.localPosition = localPos;

                // FIXME: Random.Range in Update causes GC. Should pre-bake random scale offsets
                float scalePulse = 1f + Mathf.Sin((time + phase) * _scalePulseSpeed * 1.5f) * 0.2f;
                tendril.localScale = Vector3.one * Random.Range(0.3f, 0.6f) * scalePulse;
            }
        }

        public void SetSpeed(float speed)
        {
            _moveSpeed = speed;
        }

        public void SetDirection(Vector3 direction)
        {
            _moveDirection = direction;
        }

        public void Surge(float speedMultiplier, float duration)
        {
            StartCoroutine(SurgeCoroutine(speedMultiplier, duration));
        }

        private System.Collections.IEnumerator SurgeCoroutine(float speedMultiplier, float duration)
        {
            float originalSpeed = _moveSpeed;
            _moveSpeed *= speedMultiplier;

            yield return new WaitForSeconds(duration);

            _moveSpeed = originalSpeed;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(transform.position, _moveDirection.normalized * 3f);

            if (_reactToWalls)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(transform.position, _wallDetectionRange);
            }
        }
    }
}
