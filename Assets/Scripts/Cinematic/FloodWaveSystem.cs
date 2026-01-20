using UnityEngine;
using UnityEngine.VFX;
using System.Collections.Generic;

namespace Demagnetized.Cinematic
{
    /// <summary>
    /// AAA-Quality Flood Wave System
    /// Procedural mesh with Gerstner waves, wall collision, and VFX integration.
    /// Designed for HDRP cinematic sequences.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class FloodWaveSystem : MonoBehaviour
    {
        #region Serialized Fields
        
        [Header("=== MESH GENERATION ===")]
        [SerializeField] private int _resolutionX = 64;
        [SerializeField] private int _resolutionZ = 32;
        [SerializeField] private float _width = 8f;
        [SerializeField] private float _length = 40f;
        [SerializeField] private float _baseHeight = 2f;
        
        [Header("=== MOVEMENT ===")]
        [SerializeField] private Transform _targetToChase;
        [SerializeField] private float _chaseSpeed = 8f;
        [SerializeField] private float _minDistanceFromTarget = 5f;
        [SerializeField] private float _maxDistanceFromTarget = 15f;
        [SerializeField] private float _accelerationWhenFar = 2f;
        [SerializeField] private AnimationCurve _speedCurve = AnimationCurve.EaseInOut(0, 0.5f, 1, 1.5f);
        
        [Header("=== GERSTNER WAVES ===")]
        [SerializeField] private GerstnerWave[] _waves = new GerstnerWave[]
        {
            new GerstnerWave { amplitude = 0.8f, wavelength = 4f, speed = 2f, direction = new Vector2(0, 1), steepness = 0.5f },
            new GerstnerWave { amplitude = 0.4f, wavelength = 2f, speed = 3f, direction = new Vector2(0.3f, 0.7f), steepness = 0.3f },
            new GerstnerWave { amplitude = 0.2f, wavelength = 1f, speed = 4f, direction = new Vector2(-0.2f, 0.8f), steepness = 0.2f }
        };
        
        [Header("=== FRONT WAVE (Leading Edge) ===")]
        [SerializeField] private float _frontWaveHeight = 3f;
        [SerializeField] private float _frontWaveSteepness = 0.8f;
        #pragma warning disable CS0414
        [SerializeField] private float _frontWaveWidth = 3f;
        #pragma warning restore CS0414
        [SerializeField] private AnimationCurve _frontWaveShape = AnimationCurve.EaseInOut(0, 0, 1, 1);
        
        [Header("=== WALL COLLISION ===")]
        [SerializeField] private bool _enableWallCollision = true;
        [SerializeField] private LayerMask _wallLayers = -1;
        [SerializeField] private float _wallDetectionDistance = 3f;
        [SerializeField] private int _raycastsPerSide = 8;
        [SerializeField] private float _wallPushStrength = 1.5f;
        [SerializeField] private float _wallWaveBoost = 0.5f;
        
        [Header("=== SPLASH VFX ===")]
        [SerializeField] private VisualEffect _splashVFX;
        [SerializeField] private FloodSplashController _splashController;
        [SerializeField] private float _splashThreshold = 0.5f;
        [SerializeField] private float _splashCooldown = 0.1f;
        [SerializeField] private int _maxSplashesPerFrame = 5;

        [Header("=== AUDIO ===")]
        [SerializeField] private AudioSource _rushAudioSource;
        [SerializeField] private AudioClip _rushLoopClip;
        [SerializeField] private float _rushVolume = 0.8f;
        [SerializeField] private AnimationCurve _volumeByDistance = AnimationCurve.EaseInOut(0, 0.3f, 1, 1f);
        
        [Header("=== TURBULENCE ===")]
        [SerializeField] private float _turbulenceStrength = 0.3f;
        [SerializeField] private float _turbulenceScale = 2f;
        [SerializeField] private float _turbulenceSpeed = 1f;
        
        [Header("=== CORRIDOR CONSTRAINTS ===")]
        [SerializeField] private bool _constrainToCorridor = true;
        [SerializeField] private float _corridorMinX = -170f;
        [SerializeField] private float _corridorMaxX = -150f;
        [SerializeField] private float _corridorFloorY = 0f;
        
        [Header("=== DEBUG ===")]
        [SerializeField] private bool _showDebugGizmos = true;
        [SerializeField] private bool _showWallRays = false;
        
        #endregion
        
        #region Private Fields
        
        private Mesh _mesh;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        
        private Vector3[] _baseVertices;
        private Vector3[] _vertices;
        private Vector3[] _normals;
        private Vector2[] _uvs;
        private int[] _triangles;
        
        private float[] _wallProximity; // Per-vertex wall proximity
        private Vector3[] _wallNormals; // Per-vertex wall push direction
        
        private float _lastSplashTime;
        private int _splashesThisFrame;
        private List<Vector3> _splashPositions = new List<Vector3>();
        
        private float _currentSpeed;
        private float _distanceToTarget;
        private Vector3 _velocity;
        
        #endregion
        
        #region Unity Lifecycle

        private void OnDisable()
        {
            StopAllCoroutines();
        }

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();

            GenerateMesh();
        }

        private void Start()
        {
            if (_targetToChase == null)
            {
                // Try to find player/character
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) _targetToChase = player.transform;
            }

            _currentSpeed = _chaseSpeed;

            // Initialize rush audio
            if (_rushAudioSource != null && _rushLoopClip != null)
            {
                _rushAudioSource.clip = _rushLoopClip;
                _rushAudioSource.loop = true;
                _rushAudioSource.volume = _rushVolume;
                _rushAudioSource.Play();
            }
        }
        
        private void Update()
        {
            UpdateMovement();
            UpdateWallCollision();
            UpdateWaveVertices();
            TriggerSplashes();
            UpdateAudio();
            UpdateSplashControllerEffects();

            _splashesThisFrame = 0;
        }
        
        private void LateUpdate()
        {
            // Apply mesh changes
            _mesh.vertices = _vertices;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }
        
        #endregion
        
        #region Mesh Generation
        
        private void GenerateMesh()
        {
            _mesh = new Mesh();
            _mesh.name = "FloodWaveMesh";
            _mesh.MarkDynamic(); // Optimize for frequent updates
            
            int vertexCount = (_resolutionX + 1) * (_resolutionZ + 1);
            _baseVertices = new Vector3[vertexCount];
            _vertices = new Vector3[vertexCount];
            _normals = new Vector3[vertexCount];
            _uvs = new Vector2[vertexCount];
            _wallProximity = new float[vertexCount];
            _wallNormals = new Vector3[vertexCount];
            
            // Generate vertices
            for (int z = 0; z <= _resolutionZ; z++)
            {
                for (int x = 0; x <= _resolutionX; x++)
                {
                    int index = z * (_resolutionX + 1) + x;
                    
                    float xPos = (x / (float)_resolutionX - 0.5f) * _width;
                    float zPos = (z / (float)_resolutionZ) * _length;
                    
                    _baseVertices[index] = new Vector3(xPos, 0, zPos);
                    _vertices[index] = _baseVertices[index];
                    _uvs[index] = new Vector2(x / (float)_resolutionX, z / (float)_resolutionZ);
                    _normals[index] = Vector3.up;
                }
            }
            
            // Generate triangles
            _triangles = new int[_resolutionX * _resolutionZ * 6];
            int triIndex = 0;
            
            for (int z = 0; z < _resolutionZ; z++)
            {
                for (int x = 0; x < _resolutionX; x++)
                {
                    int bottomLeft = z * (_resolutionX + 1) + x;
                    int bottomRight = bottomLeft + 1;
                    int topLeft = bottomLeft + _resolutionX + 1;
                    int topRight = topLeft + 1;
                    
                    // First triangle
                    _triangles[triIndex++] = bottomLeft;
                    _triangles[triIndex++] = topLeft;
                    _triangles[triIndex++] = bottomRight;
                    
                    // Second triangle
                    _triangles[triIndex++] = bottomRight;
                    _triangles[triIndex++] = topLeft;
                    _triangles[triIndex++] = topRight;
                }
            }
            
            _mesh.vertices = _vertices;
            _mesh.uv = _uvs;
            _mesh.triangles = _triangles;
            _mesh.normals = _normals;
            _mesh.RecalculateBounds();
            
            _meshFilter.mesh = _mesh;
        }
        
        #endregion
        
        #region Movement
        
        private void UpdateMovement()
        {
            if (_targetToChase == null) return;
            
            Vector3 targetPos = _targetToChase.position;
            Vector3 myPos = transform.position;
            
            // Calculate distance (only Z axis matters for corridor chase)
            _distanceToTarget = targetPos.z - myPos.z;
            
            // Dynamic speed based on distance
            float normalizedDist = Mathf.InverseLerp(_minDistanceFromTarget, _maxDistanceFromTarget, _distanceToTarget);
            float speedMultiplier = _speedCurve.Evaluate(normalizedDist);
            
            if (_distanceToTarget > _maxDistanceFromTarget)
            {
                // Too far, accelerate
                _currentSpeed = Mathf.Lerp(_currentSpeed, _chaseSpeed * _accelerationWhenFar, Time.deltaTime * 2f);
            }
            else if (_distanceToTarget < _minDistanceFromTarget)
            {
                // Too close, slow down
                _currentSpeed = Mathf.Lerp(_currentSpeed, _chaseSpeed * 0.5f, Time.deltaTime * 3f);
            }
            else
            {
                _currentSpeed = Mathf.Lerp(_currentSpeed, _chaseSpeed * speedMultiplier, Time.deltaTime);
            }
            
            // Move forward
            _velocity = Vector3.forward * _currentSpeed;
            transform.position += _velocity * Time.deltaTime;
            
            // Constrain to corridor
            if (_constrainToCorridor)
            {
                Vector3 pos = transform.position;
                pos.x = Mathf.Clamp(pos.x, _corridorMinX, _corridorMaxX);
                pos.y = _corridorFloorY;
                transform.position = pos;
            }
        }
        
        #endregion
        
        #region Wave Calculation
        
        private void UpdateWaveVertices()
        {
            float time = Time.time;
            
            for (int i = 0; i < _vertices.Length; i++)
            {
                Vector3 baseVertex = _baseVertices[i];
                Vector3 worldPos = transform.TransformPoint(baseVertex);
                
                // Start with base height
                float height = _baseHeight;
                float xOffset = 0f;
                float zOffset = 0f;
                
                // === GERSTNER WAVES ===
                foreach (var wave in _waves)
                {
                    Vector3 gerstner = CalculateGerstnerWave(worldPos, wave, time);
                    xOffset += gerstner.x;
                    height += gerstner.y;
                    zOffset += gerstner.z;
                }
                
                // === FRONT WAVE (Leading Edge) ===
                float frontFactor = 1f - (_uvs[i].y); // 0 at back, 1 at front
                float frontWave = _frontWaveShape.Evaluate(frontFactor);
                float frontHeight = frontWave * _frontWaveHeight;
                
                // Add steepness to front
                float steepnessOffset = frontWave * _frontWaveSteepness * Mathf.Sin(time * 3f + worldPos.x * 0.5f);
                frontHeight += steepnessOffset;
                
                // Front wave width modulation
                float widthMod = 1f + frontWave * (Mathf.Sin(time * 2f + worldPos.x) * 0.3f);
                
                height += frontHeight;
                
                // === TURBULENCE ===
                float turbulence = Mathf.PerlinNoise(
                    worldPos.x * _turbulenceScale + time * _turbulenceSpeed,
                    worldPos.z * _turbulenceScale + time * _turbulenceSpeed * 0.7f
                ) * 2f - 1f;
                height += turbulence * _turbulenceStrength;
                
                // === WALL PROXIMITY BOOST ===
                if (_enableWallCollision && _wallProximity[i] > 0)
                {
                    // Boost wave height near walls
                    height += _wallProximity[i] * _wallWaveBoost * (1f + Mathf.Sin(time * 5f) * 0.5f);
                    
                    // Push away from wall
                    xOffset += _wallNormals[i].x * _wallProximity[i] * _wallPushStrength;
                }
                
                // === APPLY ===
                _vertices[i] = new Vector3(
                    baseVertex.x + xOffset * widthMod,
                    height,
                    baseVertex.z + zOffset
                );
            }
        }
        
        private Vector3 CalculateGerstnerWave(Vector3 position, GerstnerWave wave, float time)
        {
            float k = 2f * Mathf.PI / wave.wavelength;
            float c = Mathf.Sqrt(9.81f / k); // Phase speed
            float d = Vector2.Dot(wave.direction.normalized, new Vector2(position.x, position.z));
            float f = k * (d - c * wave.speed * time);
            float a = wave.amplitude;
            float q = wave.steepness / (k * a); // Steepness factor
            
            return new Vector3(
                q * a * wave.direction.x * Mathf.Cos(f),
                a * Mathf.Sin(f),
                q * a * wave.direction.y * Mathf.Cos(f)
            );
        }
        
        #endregion
        
        #region Wall Collision
        
        private void UpdateWallCollision()
        {
            if (!_enableWallCollision) return;
            
            _splashPositions.Clear();
            
            // Sample wall proximity at key vertices
            for (int z = 0; z <= _resolutionZ; z += Mathf.Max(1, _resolutionZ / _raycastsPerSide))
            {
                for (int x = 0; x <= _resolutionX; x++)
                {
                    int index = z * (_resolutionX + 1) + x;
                    Vector3 worldPos = transform.TransformPoint(_vertices[index]);
                    worldPos.y += 1f; // Raycast from slightly above
                    
                    // Reset
                    _wallProximity[index] = 0f;
                    _wallNormals[index] = Vector3.zero;
                    
                    // Check left and right
                    CheckWallDirection(index, worldPos, Vector3.right);
                    CheckWallDirection(index, worldPos, Vector3.left);
                }
            }
            
            // Interpolate wall proximity for vertices we didn't sample
            InterpolateWallProximity();
        }
        
        private void CheckWallDirection(int vertexIndex, Vector3 origin, Vector3 direction)
        {
            if (Physics.Raycast(origin, direction, out RaycastHit hit, _wallDetectionDistance, _wallLayers))
            {
                float proximity = 1f - (hit.distance / _wallDetectionDistance);
                proximity = Mathf.Pow(proximity, 0.5f); // Non-linear falloff
                
                if (proximity > _wallProximity[vertexIndex])
                {
                    _wallProximity[vertexIndex] = proximity;
                    _wallNormals[vertexIndex] = hit.normal;
                    
                    // Register splash position
                    if (proximity > _splashThreshold && _splashesThisFrame < _maxSplashesPerFrame)
                    {
                        _splashPositions.Add(hit.point);
                    }
                }
            }
        }
        
        private void InterpolateWallProximity()
        {
            // Simple interpolation for non-sampled vertices
            int sampleStep = Mathf.Max(1, _resolutionZ / _raycastsPerSide);
            
            for (int z = 0; z <= _resolutionZ; z++)
            {
                if (z % sampleStep == 0) continue; // Skip sampled rows
                
                int prevZ = (z / sampleStep) * sampleStep;
                int nextZ = Mathf.Min(prevZ + sampleStep, _resolutionZ);
                float t = (z - prevZ) / (float)sampleStep;
                
                for (int x = 0; x <= _resolutionX; x++)
                {
                    int index = z * (_resolutionX + 1) + x;
                    int prevIndex = prevZ * (_resolutionX + 1) + x;
                    int nextIndex = nextZ * (_resolutionX + 1) + x;
                    
                    _wallProximity[index] = Mathf.Lerp(_wallProximity[prevIndex], _wallProximity[nextIndex], t);
                    _wallNormals[index] = Vector3.Lerp(_wallNormals[prevIndex], _wallNormals[nextIndex], t);
                }
            }
        }
        
        #endregion
        
        #region VFX
        
        private void TriggerSplashes()
        {
            if (Time.time - _lastSplashTime < _splashCooldown) return;

            foreach (var pos in _splashPositions)
            {
                if (_splashesThisFrame >= _maxSplashesPerFrame) break;

                // Get wall normal for this splash position
                Vector3 wallNormal = GetWallNormalAtPosition(pos);
                float intensity = _currentSpeed / _chaseSpeed;

                // Use FloodSplashController if available (preferred)
                if (_splashController != null)
                {
                    _splashController.TriggerWallSplash(pos, wallNormal, intensity);
                }
                // Fallback to direct VFX if no controller
                else if (_splashVFX != null)
                {
                    _splashVFX.SetVector3("SplashPosition", pos);
                    _splashVFX.SetFloat("SplashIntensity", intensity);
                    _splashVFX.SendEvent("OnSplash");
                }

                _splashesThisFrame++;
                _lastSplashTime = Time.time;
            }
        }

        private Vector3 GetWallNormalAtPosition(Vector3 position)
        {
            // Find closest wall normal from our cached data
            float minDist = float.MaxValue;
            Vector3 closestNormal = Vector3.right;

            for (int i = 0; i < _wallNormals.Length; i++)
            {
                if (_wallProximity[i] > 0.1f)
                {
                    Vector3 vertWorldPos = transform.TransformPoint(_vertices[i]);
                    float dist = Vector3.Distance(position, vertWorldPos);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        closestNormal = _wallNormals[i];
                    }
                }
            }

            return closestNormal;
        }

        private void UpdateAudio()
        {
            if (_rushAudioSource == null) return;

            // Volume based on distance to player
            float normalizedDistance = Mathf.InverseLerp(_minDistanceFromTarget, _maxDistanceFromTarget, _distanceToTarget);
            float volume = _volumeByDistance.Evaluate(1f - normalizedDistance) * _rushVolume;
            _rushAudioSource.volume = volume;

            // Pitch variation based on speed
            float pitchMod = Mathf.Lerp(0.9f, 1.1f, _currentSpeed / (_chaseSpeed * _accelerationWhenFar));
            _rushAudioSource.pitch = pitchMod;
        }

        private void UpdateSplashControllerEffects()
        {
            if (_splashController == null) return;

            // Update foam position at leading edge
            Vector3 foamPos = transform.position + Vector3.forward * _length;
            _splashController.UpdateFoamPosition(foamPos, transform.forward);

            // Update mist volume
            Vector3 mistPos = transform.position + Vector3.forward * (_length * 0.5f);
            Vector3 mistSize = new Vector3(_width, 3f, _length * 0.5f);
            _splashController.UpdateMistPosition(mistPos, mistSize);

            // Set intensity based on speed
            float intensity = _currentSpeed / _chaseSpeed;
            _splashController.SetIntensity(intensity);
        }

        #endregion
        
        #region Public API
        
        /// <summary>
        /// Trigger a surge - temporary speed boost with visual intensity
        /// </summary>
        public void Surge(float speedMultiplier = 2f, float duration = 1f)
        {
            StartCoroutine(SurgeCoroutine(speedMultiplier, duration));
        }
        
        private System.Collections.IEnumerator SurgeCoroutine(float multiplier, float duration)
        {
            float originalSpeed = _chaseSpeed;
            float originalFrontHeight = _frontWaveHeight;
            
            _chaseSpeed *= multiplier;
            _frontWaveHeight *= 1.5f;
            
            yield return new WaitForSeconds(duration);
            
            // Smooth return
            float elapsed = 0f;
            float returnDuration = 0.5f;
            
            while (elapsed < returnDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / returnDuration;
                _chaseSpeed = Mathf.Lerp(_chaseSpeed, originalSpeed, t);
                _frontWaveHeight = Mathf.Lerp(_frontWaveHeight, originalFrontHeight, t);
                yield return null;
            }
            
            _chaseSpeed = originalSpeed;
            _frontWaveHeight = originalFrontHeight;
        }
        
        /// <summary>
        /// Set the target to chase
        /// </summary>
        public void SetTarget(Transform target)
        {
            _targetToChase = target;
        }
        
        /// <summary>
        /// Pause/Resume the flood
        /// </summary>
        public void SetPaused(bool paused)
        {
            enabled = !paused;
        }
        
        /// <summary>
        /// Instantly teleport to position
        /// </summary>
        public void TeleportTo(Vector3 position)
        {
            transform.position = position;
        }
        
        /// <summary>
        /// Get current distance to target
        /// </summary>
        public float GetDistanceToTarget() => _distanceToTarget;
        
        /// <summary>
        /// Get current speed
        /// </summary>
        public float GetCurrentSpeed() => _currentSpeed;
        
        #endregion
        
        #region Debug
        
        private void OnDrawGizmosSelected()
        {
            if (!_showDebugGizmos) return;
            
            // Draw corridor bounds
            if (_constrainToCorridor)
            {
                Gizmos.color = Color.cyan;
                Vector3 center = new Vector3((_corridorMinX + _corridorMaxX) / 2f, _corridorFloorY + 2f, transform.position.z);
                Vector3 size = new Vector3(_corridorMaxX - _corridorMinX, 4f, _length);
                Gizmos.DrawWireCube(center, size);
            }
            
            // Draw chase target
            if (_targetToChase != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position, _targetToChase.position);
                Gizmos.DrawWireSphere(_targetToChase.position, 0.5f);
            }
            
            // Draw wall rays
            if (_showWallRays && Application.isPlaying)
            {
                Gizmos.color = Color.yellow;
                for (int i = 0; i < _splashPositions.Count; i++)
                {
                    Gizmos.DrawSphere(_splashPositions[i], 0.2f);
                }
            }
        }
        
        #endregion
    }
    
    #region Data Structures
    
    [System.Serializable]
    public struct GerstnerWave
    {
        [Tooltip("Wave height")]
        public float amplitude;
        
        [Tooltip("Distance between wave peaks")]
        public float wavelength;
        
        [Tooltip("Wave movement speed")]
        public float speed;
        
        [Tooltip("Wave direction (X, Z)")]
        public Vector2 direction;
        
        [Tooltip("Wave sharpness (0-1)")]
        [Range(0f, 1f)]
        public float steepness;
    }
    
    #endregion
}
