using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using System.Collections;

namespace Core
{
    /// <summary>
    /// MATRIX PORTAL SYSTEM - True "Window Into Another Space" Effect
    /// Uses mathematically correct matrix transformation for camera positioning.
    /// No parallax approximations - pure spatial transformation.
    /// </summary>
    public class MatrixPortalSystem : MonoBehaviour
    {
        #region Configuration

        [Header("Portal Components")]
        [Tooltip("The portal surface mesh (entry portal)")]
        [SerializeField] private MeshRenderer _portalScreen;
        
        [Tooltip("Camera that renders the exit portal view")]
        [SerializeField] private Camera _portalCamera;
        
        [Tooltip("Player's main camera")]
        [SerializeField] private Camera _playerCamera;

        [Tooltip("Optional explicit player controller reference")]
        [SerializeField] private CharacterController _playerController;

        [Header("Target Scene")]
        [SerializeField] private string _targetSceneName = "HDRP_TheCarnival";
        
        [Tooltip("Exit portal / destination transform for TELEPORTATION (spawn point)")]
        [SerializeField] private Transform _destination;
        
        [Tooltip("Optional: Separate camera target for portal VIEW (if null, uses destination)")]
        [SerializeField] private Transform _cameraTarget;
        
        [SerializeField] private string[] _destinationNames = { "PLAYERSPAWN", "PLAYER_SPAWN", "DESTINATION", "EXIT_PORTAL" };
        [SerializeField] private string[] _cameraTargetNames = { "CAMERA_TARGET", "CAMERATARGET", "LOOK_TARGET" };

        [Header("Rendering")]
        [SerializeField] [Range(0.5f, 2f)] private float _renderScale = 1f;
        #pragma warning disable CS0414
        [SerializeField] [Range(-30f, 30f)] private float _fovOffset = 0f;
        #pragma warning restore CS0414
        [SerializeField] private float _maxRenderDistance = 30f;
        
        [Header("Oblique Clipping")]
        [Tooltip("Align near clip plane with exit portal to hide objects behind it")]
        [SerializeField] private bool _useObliqueClipping = true;
        [SerializeField] [Range(0.01f, 0.5f)] private float _nearClipOffset = 0.05f;

        [Header("Teleportation")]
        [SerializeField] private float _teleportDistance = 2.5f;
        
        [Header("Unilateral (One-Way) Settings")]
        [Tooltip("Enable one-way portal (only enter from front)")]
        [SerializeField] private bool _isUnilateral = true;
        
        [Tooltip("Enable back-face culling (don't render when viewed from behind)")]
        [SerializeField] private bool _enableBackfaceCulling = true;
        
        [Header("Scene Offset")]
        [SerializeField] private float _sceneOffsetDistance = 500f;

        [Header("Debug")]
        [SerializeField] private bool _showDebugLogs = true;
        [SerializeField] private bool _showDebugGizmos = true;

        #endregion

        #region Private State

        private RenderTexture _portalTexture;
        private Material _portalMaterial;
        private CharacterController _playerCC;
        
        private bool _initialized = false;
        private bool _targetSceneLoaded = false;
        private bool _isTeleporting = false;
        
        private Vector3 _sceneOffset;
        private float _lastPortalSide = 0f;
        
        private Plane[] _frustumPlanes = new Plane[6];

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            StartCoroutine(Initialize());
        }

        private IEnumerator Initialize()
        {
            Log("Initializing Matrix Portal System...");
            
            // Wait a frame for other systems
            yield return null;
            
            // Find player camera if not assigned
            if (_playerCamera == null)
            {
                _playerCamera = Camera.main;
                if (_playerCamera == null)
                {
                    Debug.LogError("[MatrixPortal] No player camera found!");
                    yield break;
                }
            }
            
            // Find CharacterController
            _playerCC = _playerController;
            if (_playerCC == null && ServiceLocator.Instance != null)
                _playerCC = ServiceLocator.Instance.PlayerController;
            if (_playerCC == null)
                _playerCC = FindFirstObjectByType<CharacterController>();
            if (_playerCC == null)
            {
                Debug.LogWarning("[MatrixPortal] No CharacterController found - teleportation will use camera transform");
            }
            
            // Setup portal camera
            if (_portalCamera == null)
            {
                Debug.LogError("[MatrixPortal] Portal camera not assigned!");
                yield break;
            }
            
            // Unparent portal camera
            _portalCamera.transform.SetParent(null);
            
            // Get portal material
            if (_portalScreen != null)
            {
                _portalMaterial = _portalScreen.material;
            }
            
            // Setup render texture
            SetupRenderTexture();
            
            // Setup camera settings
            SetupPortalCamera();
            
            // Calculate scene offset
            _sceneOffset = new Vector3(_sceneOffsetDistance, 0, 0);
            
            // Load target scene
            yield return StartCoroutine(LoadTargetScene());
            
            _initialized = true;
            Log($"Matrix Portal System initialized. Destination: {(_destination != null ? _destination.name : "NULL")}");
        }

        private void OnDestroy()
        {
            // CRITICAL: Clear camera's targetTexture BEFORE destroying the RenderTexture
            // This prevents "Releasing render texture that is set as Camera.targetTexture!" error
            if (_portalCamera != null)
            {
                _portalCamera.targetTexture = null;
            }
            
            if (_portalTexture != null)
            {
                _portalTexture.Release();
                DestroyImmediate(_portalTexture);
                _portalTexture = null;
            }
        }

        #endregion

        #region Setup Methods

        private void SetupRenderTexture()
        {
            int width = Mathf.RoundToInt(Screen.width * _renderScale);
            int height = Mathf.RoundToInt(Screen.height * _renderScale);
            
            _portalTexture = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR);
            _portalTexture.antiAliasing = 4;
            _portalTexture.filterMode = FilterMode.Bilinear;
            _portalTexture.Create();
            
            _portalCamera.targetTexture = _portalTexture;
            
            // Ensure we use the ScreenSpace shader
            var portalShader = Shader.Find("Portal/ScreenSpace");
            if (portalShader == null)
            {
                portalShader = Shader.Find("Unlit/Texture"); // Fallback
                Debug.LogWarning("[MatrixPortal] Portal/ScreenSpace shader not found, using fallback");
            }

            if (_portalMaterial == null)
            {
                if (portalShader != null)
                    _portalMaterial = new Material(portalShader);
                
                if (_portalScreen != null)
                    _portalScreen.material = _portalMaterial;
            }
            else if (portalShader != null && _portalMaterial.shader != portalShader)
            {
                // FORCE update shader if it's different
                _portalMaterial.shader = portalShader;
                Log($"Updated portal material shader to {portalShader.name}");
            }
            
            if (_portalMaterial != null)
            {
                _portalMaterial.SetTexture("_PortalTex", _portalTexture);
                _portalMaterial.SetTexture("_MainTex", _portalTexture); // Compatibility
                
                // Handle DirectX flipping if needed
                if (SystemInfo.graphicsDeviceVersion.StartsWith("Direct3D") && _portalTexture.texelSize.y < 0)
                {
                     _portalMaterial.SetFloat("_FlipY", 1);
                }
            }
            
            Log($"RenderTexture created: {width}x{height} with ScreenSpace shader");
        }

        private void SetupPortalCamera()
        {
            var hdCam = _portalCamera.GetComponent<HDAdditionalCameraData>();
            if (hdCam != null)
            {
                hdCam.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                hdCam.SMAAQuality = HDAdditionalCameraData.SMAAQualityLevel.High;
                // Important for portal visual quality
                hdCam.dithering = true;
            }
            
            _portalCamera.enabled = true;
            _portalCamera.nearClipPlane = 0.1f;
            
            Log("Portal camera configured");
        }

        private IEnumerator LoadTargetScene()
        {
            var existingScene = SceneManager.GetSceneByName(_targetSceneName);
            if (existingScene.IsValid() && existingScene.isLoaded)
            {
                Log($"Scene '{_targetSceneName}' already loaded - skipping offset");
                // Don't apply offset again - scene was already positioned
                DisableSceneCameras(existingScene);
                FindDestination(existingScene);
                _targetSceneLoaded = true;
                yield break;
            }
            
            Log($"Loading scene '{_targetSceneName}'...");
            var loadOp = SceneManager.LoadSceneAsync(_targetSceneName, LoadSceneMode.Additive);
            if (loadOp == null)
            {
                Debug.LogError($"[MatrixPortal] Failed to load scene '{_targetSceneName}'");
                yield break;
            }
            
            yield return loadOp;
            
            var loadedScene = SceneManager.GetSceneByName(_targetSceneName);
            if (loadedScene.IsValid())
            {
                DisableSceneCameras(loadedScene);
                ApplySceneOffset(loadedScene);
                FindDestination(loadedScene);
                _targetSceneLoaded = true;
                Log($"Scene '{_targetSceneName}' loaded successfully");
            }
        }

        /// <summary>
        /// Disable all cameras, audio listeners, and audio sources in the loaded scene
        /// so they don't interfere with the game camera. The portal uses its own _portalCamera.
        /// </summary>
        private void DisableSceneCameras(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                {
                    if (cam != _portalCamera && cam != _playerCamera)
                        cam.enabled = false;
                }
                foreach (var al in root.GetComponentsInChildren<AudioListener>(true))
                    al.enabled = false;
            }
        }

        private void ApplySceneOffset(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            if (roots.Length == 0) return;
            
            // Check if scene is already offset (first root object is far from origin)
            float currentX = roots[0].transform.position.x;
            if (Mathf.Abs(currentX) > 100f)
            {
                Log($"Scene already offset (X={currentX:F0}), skipping additional offset");
                return;
            }
            
            // Apply offset to all root objects
            foreach (var root in roots)
            {
                root.transform.position += _sceneOffset;
            }
            Log($"Scene offset applied: {_sceneOffset}");
        }

        private void FindDestination(Scene scene)
        {
            if (_destination != null && _cameraTarget != null) return;
            
            foreach (var root in scene.GetRootGameObjects())
            {
                // Search for destination (spawn point)
                if (_destination == null)
                {
                    foreach (var destName in _destinationNames)
                    {
                        if (root.name == destName)
                        {
                            _destination = root.transform;
                            FixDestinationHeight();
                            Log($"Found destination (spawn): '{destName}' at {_destination.position}");
                            break;
                        }
                        
                        var child = root.transform.Find(destName);
                        if (child != null)
                        {
                            _destination = child;
                            FixDestinationHeight();
                            Log($"Found destination (spawn): '{destName}' at {_destination.position}");
                            break;
                        }
                    }
                }
                
                // Search for camera target (look direction)
                if (_cameraTarget == null)
                {
                    foreach (var targetName in _cameraTargetNames)
                    {
                        if (root.name == targetName)
                        {
                            _cameraTarget = root.transform;
                            Log($"Found camera target: '{targetName}' at {_cameraTarget.position}");
                            break;
                        }
                        
                        var child = root.transform.Find(targetName);
                        if (child != null)
                        {
                            _cameraTarget = child;
                            Log($"Found camera target: '{child.name}' at {_cameraTarget.position}");
                            break;
                        }
                    }
                }
            }
            
            if (_destination == null)
            {
                Debug.LogWarning("[MatrixPortal] No destination found! Creating default.");
                var go = new GameObject("PORTAL_DESTINATION");
                float playerY = _playerCamera != null ? _playerCamera.transform.position.y : 18f;
                go.transform.position = _sceneOffset + new Vector3(0, playerY, 0);
                SceneManager.MoveGameObjectToScene(go, scene);
                _destination = go.transform;
            }
            
            // If no camera target found, use destination
            if (_cameraTarget == null)
            {
                _cameraTarget = _destination;
                Log("Camera target not found, using destination as camera target");
            }
        }
        
        /// <summary>
        /// Fixes destination Y position if it's underground (due to bad scene offset)
        /// </summary>
        private void FixDestinationHeight()
        {
            if (_destination == null) return;
            
            // If destination is underground, move it to player's height
            if (_destination.position.y < 0)
            {
                float playerY = _playerCamera != null ? _playerCamera.transform.position.y : 18f;
                Vector3 fixedPos = _destination.position;
                fixedPos.y = playerY;
                _destination.position = fixedPos;
                Log($"Fixed destination Y position to {playerY}");
            }
        }

        #endregion

        #region Update Loop

        private void LateUpdate()
        {
            if (!_initialized || !_targetSceneLoaded) return;
            if (_portalCamera == null || _destination == null || _playerCamera == null || _portalScreen == null) return;

            float dist = Vector3.Distance(_playerCamera.transform.position, _portalScreen.transform.position);

            // Distance culling
            if (dist > _maxRenderDistance)
            {
                _portalCamera.enabled = false;
                return;
            }

            // === BACK-FACE CULLING (Unilateral Optimization) ===
            // If player is viewing portal from behind, don't render (saves ~50% GPU)
            if (_enableBackfaceCulling)
            {
                Vector3 portalToPlayer = (_playerCamera.transform.position - _portalScreen.transform.position).normalized;
                float viewDot = Vector3.Dot(_portalScreen.transform.forward, portalToPlayer);
                
                // Positive dot = player is behind the portal
                if (viewDot > 0.01f)
                {
                    _portalCamera.enabled = false;
                    return;
                }
            }

            // Frustum culling
            GeometryUtility.CalculateFrustumPlanes(_playerCamera, _frustumPlanes);
            if (!GeometryUtility.TestPlanesAABB(_frustumPlanes, _portalScreen.bounds))
            {
                _portalCamera.enabled = false;
                return;
            }

            _portalCamera.enabled = true;

            // === CORE: TRUE MATRIX TRANSFORMATION ===
            CalculatePortalCameraTransform();
            
            // Oblique clipping
            if (_useObliqueClipping)
                ApplyObliqueClipping();
        }

        private void Update()
        {
            if (!_initialized || !_targetSceneLoaded || _isTeleporting) return;
            if (_playerCamera == null || _portalScreen == null) return;

            Vector3 playerPos = _playerCamera.transform.position;
            float distToPortal = Vector3.Distance(playerPos, _portalScreen.transform.position);

            float currentSide = GetPortalSide(playerPos);
            bool crossed = (_lastPortalSide > 0 && currentSide < 0) || (_lastPortalSide < 0 && currentSide > 0);

            if (crossed && _lastPortalSide != 0 && distToPortal < _teleportDistance)
            {
                // === UNILATERAL CHECK (One-Way Portal) ===
                if (_isUnilateral && !IsPlayerEnteringFromFront())
                {
                    if (_showDebugLogs)
                        Log("Teleport blocked: Player approaching from back (one-way portal)");
                    return;
                }
                
                Teleport();
                return;
            }

            _lastPortalSide = currentSide;
        }

        #endregion

        #region Matrix Transformation

        /// <summary>
        /// THE CORE ALGORITHM: True matrix-based portal camera positioning.
        /// Uses _cameraTarget for camera view (separate from spawn destination).
        /// </summary>
        private void CalculatePortalCameraTransform()
        {
            // Use camera target for rendering (falls back to destination if not set)
            Transform targetForCamera = _cameraTarget != null ? _cameraTarget : _destination;
            
            if (targetForCamera == null || _portalScreen == null || _playerCamera == null) return;
            
            // === STEP 1: Get portal transforms ===
            Transform entryPortal = _portalScreen.transform;
            Transform exitPortal = targetForCamera;  // Use camera target, NOT spawn destination
            Transform playerCam = _playerCamera.transform;
            
            // === STEP 2: Calculate relative position of player to entry portal ===
            // Convert player world position to entry portal's local space
            Vector3 playerLocalPos = entryPortal.InverseTransformPoint(playerCam.position);
            
            // Flip Z axis (player looks IN, we look OUT through the other side)
            playerLocalPos = new Vector3(playerLocalPos.x, playerLocalPos.y, -playerLocalPos.z);
            
            // Convert to exit portal's world space
            Vector3 newPos = exitPortal.TransformPoint(playerLocalPos);
            
            // === STEP 3: Calculate relative rotation ===
            // The relative rotation from entry to exit (with 180° flip because portals face each other)
            Quaternion portalRotationDelta = exitPortal.rotation * Quaternion.Euler(0, 180f, 0) * Quaternion.Inverse(entryPortal.rotation);
            
            // Apply this rotation delta to the player camera's world rotation
            Quaternion newRot = portalRotationDelta * playerCam.rotation;
            
            // === STEP 4: Apply transform ===
            _portalCamera.transform.SetPositionAndRotation(newPos, newRot);
            
            // === STEP 5: Match camera parameters EXACTLY (no offset!) ===
            _portalCamera.fieldOfView = _playerCamera.fieldOfView;  // No FOV offset for zero parallax
            _portalCamera.aspect = _playerCamera.aspect;
            _portalCamera.nearClipPlane = _playerCamera.nearClipPlane;
            
            // Reset projection matrix before oblique clipping modifies it
            _portalCamera.ResetProjectionMatrix();
            
            // Debug logging
            if (_showDebugLogs && Time.frameCount % 120 == 0)
            {
                Log($"[Matrix] Player: {_playerCamera.transform.position:F1} → Camera: {newPos:F1}");
            }
        }

        private void ApplyObliqueClipping()
        {
            // Calculate clip plane in camera space
            Transform clipPlane = _destination;
            Vector3 camPos = _portalCamera.transform.position;
            
            int dot = System.Math.Sign(Vector3.Dot(clipPlane.forward, clipPlane.position - camPos));
            
            Vector3 camSpacePos = _portalCamera.worldToCameraMatrix.MultiplyPoint(clipPlane.position);
            Vector3 camSpaceNormal = _portalCamera.worldToCameraMatrix.MultiplyVector(clipPlane.forward) * dot;
            float camSpaceDst = -Vector3.Dot(camSpacePos, camSpaceNormal) + _nearClipOffset;
            
            if (Mathf.Abs(camSpaceDst) > 0.1f)
            {
                Vector4 clipPlaneCameraSpace = new Vector4(camSpaceNormal.x, camSpaceNormal.y, camSpaceNormal.z, camSpaceDst);
                _portalCamera.projectionMatrix = _portalCamera.CalculateObliqueMatrix(clipPlaneCameraSpace);
            }
        }

        #endregion

        #region Teleportation

        private float GetPortalSide(Vector3 pos)
        {
            return Mathf.Sign(Vector3.Dot(pos - _portalScreen.transform.position, _portalScreen.transform.forward));
        }

        /// <summary>
        /// Checks if player is entering the portal from the correct (front) direction.
        /// Uses velocity-based dot product check for unilateral portals.
        /// </summary>
        private bool IsPlayerEnteringFromFront()
        {
            Vector3 velocity = _playerCC != null ? _playerCC.velocity : Vector3.zero;
            
            // If player is stationary, check position-based direction
            if (velocity.sqrMagnitude < 0.1f)
            {
                Vector3 playerToPortal = (_portalScreen.transform.position - _playerCamera.transform.position).normalized;
                float posDot = Vector3.Dot(_portalScreen.transform.forward, playerToPortal);
                return posDot > 0; // Player is in front and facing portal
            }
            
            // Velocity-based check: negative dot = entering from front
            float velDot = Vector3.Dot(_portalScreen.transform.forward, velocity.normalized);
            return velDot < 0;
        }

        private void Teleport()
        {
            _isTeleporting = true;
            Log("Teleporting...");
            
            Transform playerTransform = _playerCC != null ? _playerCC.transform : _playerCamera.transform;
            
            // Capture velocity
            Vector3 velocity = _playerCC != null ? _playerCC.velocity : Vector3.zero;
            
            // Calculate transformation matrix (same as camera)
            Matrix4x4 entryW2L = _portalScreen.transform.worldToLocalMatrix;
            Matrix4x4 exitL2W = _destination.transform.localToWorldMatrix;
            Matrix4x4 flip = Matrix4x4.Rotate(Quaternion.Euler(0, 180f, 0));
            Matrix4x4 M = exitL2W * flip * entryW2L;
            
            // Transform position and rotation
            Vector3 newPos = M.MultiplyPoint3x4(playerTransform.position);
            Quaternion newRot = M.rotation * playerTransform.rotation;
            
            // Transform velocity
            Vector3 newVelocity = M.MultiplyVector(velocity);
            
            // Execute teleport
            if (_playerCC != null)
            {
                _playerCC.enabled = false;
                playerTransform.SetPositionAndRotation(newPos, newRot);
                Physics.SyncTransforms();
                _playerCC.enabled = true;
                
                // Apply velocity
                if (newVelocity.magnitude > 0.5f)
                {
                    StartCoroutine(ApplyVelocity(newVelocity));
                }
            }
            else
            {
                playerTransform.SetPositionAndRotation(newPos, newRot);
            }
            
            // Move to target scene
            var targetScene = SceneManager.GetSceneByName(_targetSceneName);
            if (targetScene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(playerTransform.root.gameObject, targetScene);
                SceneManager.SetActiveScene(targetScene);
            }
            
            // Reset camera history (prevent TAA ghosting)
            ResetCameraHistory();
            
            // Reset animation IK (prevent foot/hand stretching)
            ResetAnimationIK();
            
            _lastPortalSide = 1f;
            _isTeleporting = false;
            
            Log($"Teleported to {newPos}");
        }

        private IEnumerator ApplyVelocity(Vector3 velocity)
        {
            float duration = 0.15f;
            float elapsed = 0f;
            
            while (elapsed < duration && _playerCC != null && _playerCC.enabled)
            {
                _playerCC.Move(velocity * Time.deltaTime);
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private void ResetCameraHistory()
        {
            var hdCam = _playerCamera.GetComponent<HDAdditionalCameraData>();
            if (hdCam != null)
            {
                // === TAA/DLSS HISTORY RESET ===
                // Force clear temporal buffers by toggling AA mode
                // Fallback: Toggle AA mode to force buffer clear
                var originalAA = hdCam.antialiasing;
                hdCam.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                StartCoroutine(RestoreAA(hdCam, originalAA));
            }
            
            // Reset projection matrices
            _playerCamera.ResetWorldToCameraMatrix();
            _playerCamera.ResetProjectionMatrix();
            
            if (_showDebugLogs)
                Log("Camera history reset completed");
        }

        private IEnumerator RestoreAA(HDAdditionalCameraData hdCam, HDAdditionalCameraData.AntialiasingMode mode)
        {
            yield return null;
            yield return null;
            if (hdCam != null) hdCam.antialiasing = mode;
        }
        
        /// <summary>
        /// Resets animation IK systems after teleportation to prevent stretching.
        /// NOTE: KINEMATION CAS integration is handled by dedicated scripts:
        /// - KinemationPortalGuard.cs (disables CAS during portal)
        /// - PortalCASPreventer.cs (prevents crash)
        /// - KinemationPortalFix.cs (fixes execution order)
        /// </summary>
        private void ResetAnimationIK()
        {
            // Soft reset: Do NOT use Rebind() - it destroys KINEMATION's TransformStreamHandles!
            // Disable/enable cycle + Update(0f) resets IK without breaking the playable graph.
            var animator = _playerCC?.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.enabled = false;
                animator.enabled = true;
                animator.Update(0f);
            }
        }

        #endregion

        #region Debug

        private void Log(string msg)
        {
            if (_showDebugLogs)
                Debug.Log($"<color=#00FF88>[MatrixPortal]</color> {msg}");
        }

        private void OnDrawGizmos()
        {
            if (!_showDebugGizmos) return;
            
            // Draw entry portal
            if (_portalScreen != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireCube(_portalScreen.transform.position, _portalScreen.bounds.size);
                Gizmos.DrawRay(_portalScreen.transform.position, _portalScreen.transform.forward * 2f);
            }
            
            // Draw exit portal
            if (_destination != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(_destination.position, 0.5f);
                Gizmos.DrawRay(_destination.position, _destination.forward * 2f);
            }
        }

        #endregion
    }
}
