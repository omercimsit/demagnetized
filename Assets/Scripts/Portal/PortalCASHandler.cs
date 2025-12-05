// Portal + Kinemotion CAS Compatibility Handler
// Fixes TransformStreamHandle errors during portal teleportation

using UnityEngine;
using DamianGonzalez.Portals;
using System.Collections;
// CAS_Demo.Scripts.FPS - resolved at runtime via reflection

namespace CloneProject
{
    /// <summary>
    /// Handles Kinemotion CAS compatibility with Damian Gonzalez Portal System.
    /// Prevents "TransformStreamHandle cannot be resolved" errors during teleportation.
    /// </summary>
    [DefaultExecutionOrder(105)] // Run before portal system (110+)
    public class PortalCASHandler : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The player's Animator component with CAS")]
        [SerializeField] private Animator playerAnimator;
        
        [Tooltip("Auto-find animator on player tag if not set")]
        [SerializeField] private string playerTag = "Player";

        [Header("Settings")]
        [Tooltip("Soft reset animator after teleport (disable/enable cycle)")]
        [SerializeField] private bool softResetAfterTeleport = true;

        [Tooltip("Delay before soft reset (helps with physics settling)")]
        [SerializeField] private float softResetDelay = 0.02f;
        
        #pragma warning disable CS0414
        [Tooltip("Reset animator state machine after teleport")]
        [SerializeField] private bool resetAnimatorState = false;
        #pragma warning restore CS0414

        [Header("Camera Rotation Sync")]
        [Tooltip("Sync FPS camera rotation after teleport")]
        [SerializeField] private bool syncCameraRotation = true;

        [Header("Teleport Position Offset")]
        [Tooltip("Forward offset after teleport (meters)")]
        [SerializeField] private float forwardOffset = 0.5f;

        [Tooltip("Right offset after teleport (meters)")]
        [SerializeField] private float rightOffset = 0f;

        [Header("Teleport Rotation Offset")]
        [Tooltip("Additional Y rotation after teleport (degrees, positive = right)")]
        [SerializeField] private float yawOffset = 0f;

        [Header("One-Way Portal")]
        [Tooltip("Disable destination portal after teleport (one-way portal)")]
        [SerializeField] private bool disablePortalAfterTeleport = true;

        [Header("Debug")]
        [SerializeField] private bool verboseDebug = false;

        private MonoBehaviour[] _casComponents;
        private bool _isTeleporting;
        private RuntimeAnimatorController _cachedController;
        private MonoBehaviour _fpsController;
        private System.Reflection.MethodInfo _setLookRotationMethod;
        private Transform _teleportedTransform;
        private Camera _playerCamera;
        private float _capturedPitch; // Camera pitch at teleport moment
        private float _portalTransformedYaw; // Body yaw from portal transformation
        private Transform _portalToDisable; // For one-way portal disable

        private void Awake()
        {
            if (playerAnimator == null)
            {
                var player = GameObject.FindGameObjectWithTag(playerTag);
                if (player != null)
                {
                    // First try to get animator directly
                    playerAnimator = player.GetComponent<Animator>();
                    
                    // If not found, search in children (CAS components might be on child)
                    if (playerAnimator == null)
                    {
                        playerAnimator = player.GetComponentInChildren<Animator>();
                    }
                    
                    // Also try parent if this is a child with the tag
                    if (playerAnimator == null)
                    {
                        playerAnimator = player.GetComponentInParent<Animator>();
                    }
                }
            }

            if (playerAnimator != null)
            {
                CacheCASComponents();

                if (verboseDebug)
                    Debug.Log($"[PortalCASHandler] Found Animator on: {playerAnimator.gameObject.name}");
            }
            else
            {
                Debug.LogWarning("[PortalCASHandler] No Animator found! Please assign the player's Animator.");
            }

            // Find FPS controller for camera rotation sync
            var playerObj = GameObject.FindGameObjectWithTag(playerTag);
            if (playerObj != null)
            {
                // Find FPSExampleController by type name (avoids compile error when CAS Demo not imported)
                foreach (var mb in playerObj.GetComponents<MonoBehaviour>())
                {
                    if (mb.GetType().Name == "FPSExampleController") { _fpsController = mb; break; }
                }
                if (_fpsController == null)
                {
                    foreach (var mb in playerObj.GetComponentsInChildren<MonoBehaviour>())
                    {
                        if (mb.GetType().Name == "FPSExampleController") { _fpsController = mb; break; }
                    }
                }

                // Cache SetLookRotation method via reflection
                if (_fpsController != null)
                {
                    _setLookRotationMethod = _fpsController.GetType().GetMethod("SetLookRotation",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                }

                if (verboseDebug && _fpsController != null)
                    Debug.Log($"[PortalCASHandler] Found FPSExampleController for rotation sync");
            }

            // Find player camera for rotation sync
            _playerCamera = Camera.main;
            if (verboseDebug && _playerCamera != null)
                Debug.Log($"[PortalCASHandler] Found player camera: {_playerCamera.name}");
        }

        private void OnEnable()
        {
            // Subscribe to portal events
            PortalEvents.teleport += OnPortalTeleport;
            
            if (verboseDebug)
                Debug.Log("[PortalCASHandler] Subscribed to portal events");
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            PortalEvents.teleport -= OnPortalTeleport;
        }

        private void CacheCASComponents()
        {
            // Cache all CAS-related MonoBehaviour components
            // These are the components that use Animation Rigging/Playable API
            var allComponents = playerAnimator.GetComponents<MonoBehaviour>();
            var casList = new System.Collections.Generic.List<MonoBehaviour>();

            foreach (var comp in allComponents)
            {
                string typeName = comp.GetType().FullName;
                
                // Check for Kinemotion CAS components
                if (typeName.Contains("KINEMATION") || 
                    typeName.Contains("CharacterAnimation") ||
                    typeName.Contains("ProceduralAnimation") ||
                    typeName.Contains("LayeredBlending"))
                {
                    casList.Add(comp);
                    
                    if (verboseDebug)
                        Debug.Log($"[PortalCASHandler] Found CAS component: {typeName}");
                }
            }

            _casComponents = casList.ToArray();

            if (verboseDebug)
                Debug.Log($"[PortalCASHandler] Cached {_casComponents.Length} CAS components");
        }

        /// <summary>
        /// Called when player teleports through a portal
        /// </summary>
        private void OnPortalTeleport(string groupId, Transform fromPortal, Transform toPortal, Transform teleportedObject, Vector3 oldPos, Vector3 newPos)
        {
            // Check if this is the player being teleported
            // The portal system teleports the CharacterController's root object,
            // but the Animator might be on a child object, so we check both the object itself
            // and its children for the Player tag
            bool isPlayer = teleportedObject.CompareTag(playerTag);

            // Also check if the teleported object contains our animator (handles parent/child scenarios)
            if (!isPlayer && playerAnimator != null)
            {
                isPlayer = teleportedObject == playerAnimator.transform ||
                           teleportedObject == playerAnimator.transform.root ||
                           playerAnimator.transform.IsChildOf(teleportedObject);
            }

            if (!isPlayer)
                return;

            if (_isTeleporting)
                return;

            // Cache the teleported transform for rotation sync
            _teleportedTransform = teleportedObject;

            // Cache portal reference for later disable (after coroutine completes)
            _portalToDisable = fromPortal;

            // Capture rotation values at teleport moment (DON'T apply yet - wait for yawOffset)
            // Portal has already transformed the body rotation, but FPS controller doesn't know
            if (_playerCamera != null && _fpsController != null)
            {
                // Capture yaw from body's new rotation (portal has already transformed this)
                _portalTransformedYaw = teleportedObject.eulerAngles.y;

                // Capture pitch from camera's LOCAL rotation (relative to body - this doesn't change)
                _capturedPitch = _playerCamera.transform.localEulerAngles.x;
                if (_capturedPitch > 180f) _capturedPitch -= 360f; // Normalize to -180 to 180

                if (verboseDebug)
                    Debug.Log($"[PortalCASHandler] Captured rotation: Yaw={_portalTransformedYaw:F1}, Pitch={_capturedPitch:F1}, YawOffset={yawOffset}");
            }

            if (verboseDebug)
                Debug.Log($"[PortalCASHandler] Player teleported from {oldPos} to {newPos}");

            StartCoroutine(HandleTeleportCoroutine());
        }

        private IEnumerator HandleTeleportCoroutine()
        {
            _isTeleporting = true;

            if (verboseDebug)
                Debug.Log("[PortalCASHandler] Starting teleport handling...");

            // CRITICAL: Immediately disable CAS components to prevent TransformStreamHandle errors
            // The crash happens when CAS tries to evaluate handles during/after teleport
            if (_casComponents != null && _casComponents.Length > 0)
            {
                foreach (var comp in _casComponents)
                {
                    if (comp != null)
                        comp.enabled = false;
                }
            }

            // Also disable the animator to stop any playable graph evaluation
            bool wasAnimatorEnabled = false;
            if (playerAnimator != null)
            {
                wasAnimatorEnabled = playerAnimator.enabled;
                playerAnimator.enabled = false;
            }

            // Wait a frame for physics to settle
            yield return null;

            // Apply position and rotation offsets (after physics settle)
            if (_teleportedTransform != null)
            {
                // Calculate FINAL yaw (portal transformation + user offset)
                float finalYaw = _portalTransformedYaw + yawOffset;

                // Position offset - use FINAL rotation direction
                if (forwardOffset != 0 || rightOffset != 0)
                {
                    // Calculate forward/right based on FINAL rotation (not current body rotation)
                    Quaternion finalRotation = Quaternion.Euler(0, finalYaw, 0);
                    Vector3 forward = finalRotation * Vector3.forward;
                    Vector3 right = finalRotation * Vector3.right;
                    Vector3 offset = forward * forwardOffset + right * rightOffset;

                    var cc = _teleportedTransform.GetComponent<CharacterController>();
                    if (cc != null)
                    {
                        cc.enabled = false;
                        _teleportedTransform.position += offset;
                        cc.enabled = true;
                    }
                    else
                    {
                        _teleportedTransform.position += offset;
                    }

                    if (verboseDebug)
                        Debug.Log($"[PortalCASHandler] Applied position offset: Forward={forwardOffset}, Right={rightOffset}");
                }

                // Apply FINAL rotation to body
                _teleportedTransform.rotation = Quaternion.Euler(0, finalYaw, 0);

                // Apply FINAL rotation to FPS controller (yaw + pitch)
                if (_fpsController != null)
                {
                    Quaternion finalLookRotation = Quaternion.Euler(_capturedPitch, finalYaw, 0f);
                    _setLookRotationMethod?.Invoke(_fpsController, new object[] { finalLookRotation });

                    if (verboseDebug)
                        Debug.Log($"[PortalCASHandler] Applied FINAL rotation: Yaw={finalYaw:F1} (portal={_portalTransformedYaw:F1} + offset={yawOffset}), Pitch={_capturedPitch:F1}");
                }
            }

            // Wait additional delay if configured
            if (softResetDelay > 0)
            {
                yield return new WaitForSeconds(softResetDelay);
            }

            // Soft reset animator (disable/enable cycle, NOT Rebind)
            if (softResetAfterTeleport && playerAnimator != null)
            {
                SoftResetAnimator();
            }

            // Re-enable animator
            if (playerAnimator != null && wasAnimatorEnabled)
            {
                playerAnimator.enabled = true;
            }

            // Wait another frame for animator to stabilize
            yield return null;

            // Re-enable CAS components
            if (_casComponents != null)
            {
                foreach (var comp in _casComponents)
                {
                    if (comp != null)
                        comp.enabled = true;
                }
            }

            // Sync camera rotation with teleported transform
            if (syncCameraRotation && _fpsController != null && _teleportedTransform != null)
            {
                SyncCameraRotation();
            }

            // Wait one more frame and sync again to ensure FPS controller doesn't overwrite
            yield return null;

            if (syncCameraRotation && _fpsController != null && _teleportedTransform != null)
            {
                SyncCameraRotation();
            }

            if (verboseDebug)
                Debug.Log("[PortalCASHandler] Teleport handling complete");

            _isTeleporting = false;

            // One-way portal: Disable portal at the very end
            if (disablePortalAfterTeleport && _portalToDisable != null)
            {
                var portalSetup = _portalToDisable.GetComponentInParent<DamianGonzalez.Portals.PortalSetup>();
                if (portalSetup != null)
                {
                    Debug.Log($"[PortalCASHandler] Disabling portal: {portalSetup.gameObject.name}");
                    portalSetup.gameObject.SetActive(false);
                }
                _portalToDisable = null;
            }
        }

        /// <summary>
        /// Syncs the FPS controller's internal rotation with the FINAL calculated rotation.
        /// Uses captured pitch and portal yaw + offset for consistency.
        /// </summary>
        private void SyncCameraRotation()
        {
            if (_fpsController == null || _teleportedTransform == null)
                return;

            // Use the FINAL calculated values (portal yaw + offset + captured pitch)
            float finalYaw = _portalTransformedYaw + yawOffset;

            // Also force body rotation to match (in case anything changed it)
            _teleportedTransform.rotation = Quaternion.Euler(0, finalYaw, 0);

            Quaternion finalRotation = Quaternion.Euler(_capturedPitch, finalYaw, 0f);

            // Call SetLookRotation to sync the FPS controller's internal pitch/yaw values
            _setLookRotationMethod?.Invoke(_fpsController, new object[] { finalRotation });

            if (verboseDebug)
                Debug.Log($"[PortalCASHandler] Camera rotation FINAL sync: Yaw={finalYaw:F1}, Pitch={_capturedPitch:F1}");
        }

        /// <summary>
        /// Soft resets the animator using disable/enable cycle + Update(0f).
        /// NEVER use Animator.Rebind() with KINEMATION - it destroys TransformStreamHandles.
        /// </summary>
        public void SoftResetAnimator()
        {
            if (playerAnimator == null)
                return;

            if (verboseDebug)
                Debug.Log("[PortalCASHandler] Soft resetting animator...");

            // KINEMATION-safe reset: Disable/Enable cycle instead of Rebind()
            // The disable/enable cycle triggers CAS's internal recovery logic in LateUpdate:
            // "if (!_isGraphValid && _animator.playableGraph.IsValid())"
            bool wasEnabled = playerAnimator.enabled;
            playerAnimator.enabled = false;
            playerAnimator.enabled = wasEnabled;

            // Soft update to let KINEMATION rebuild internally
            playerAnimator.Update(0f);

            if (verboseDebug)
                Debug.Log("[PortalCASHandler] Animator soft reset complete");
        }

        /// <summary>
        /// Alternative method: Disable/Enable all CAS components.
        /// Use this if SoftResetAnimator alone doesn't work.
        /// </summary>
        public void ResetCASComponents()
        {
            if (_casComponents == null || _casComponents.Length == 0)
                return;

            if (verboseDebug)
                Debug.Log("[PortalCASHandler] Resetting CAS components...");

            foreach (var comp in _casComponents)
            {
                if (comp != null)
                {
                    comp.enabled = false;
                }
            }

            // Re-enable after a short delay
            StartCoroutine(ReenableCASCoroutine());
        }

        private IEnumerator ReenableCASCoroutine()
        {
            yield return null; // Wait one frame

            foreach (var comp in _casComponents)
            {
                if (comp != null)
                {
                    comp.enabled = true;
                }
            }

            if (verboseDebug)
                Debug.Log("[PortalCASHandler] CAS components re-enabled");
        }

        
        /// <summary>
        /// Call this before teleportation to prepare the animation system.
        /// Useful for custom teleportation implementations.
        /// </summary>
        public void PrepareTeleport()
        {
            if (playerAnimator == null)
                return;

            // Optionally cache state before teleport
            _cachedController = playerAnimator.runtimeAnimatorController;
        }

        /// <summary>
        /// Call this after teleportation to restore the animation system.
        /// Useful for custom teleportation implementations.
        /// </summary>
        public void CompleteTeleport()
        {
            StartCoroutine(HandleTeleportCoroutine());
        }
    }
}
