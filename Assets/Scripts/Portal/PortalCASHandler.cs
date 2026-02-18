// Portal + KINEMATION CAS Compatibility Handler
// Fixes TransformStreamHandle errors during portal teleportation

using UnityEngine;
using DamianGonzalez.Portals;
using System.Collections;

namespace CloneProject
{
    // Prevents "TransformStreamHandle cannot be resolved" errors when player goes through a portal.
    // Also handles rotation sync and optional one-way portal disabling.
    [DefaultExecutionOrder(105)]
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

        // resetAnimatorState is kept for inspector visibility but not used right now
        [SerializeField] private bool resetAnimatorState = false;

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
        private float _capturedPitch;
        private float _portalTransformedYaw;
        private Transform _portalToDisable;

        private void Awake()
        {
            if (playerAnimator == null)
            {
                var player = GameObject.FindGameObjectWithTag(playerTag);
                if (player != null)
                {
                    playerAnimator = player.GetComponent<Animator>();

                    if (playerAnimator == null)
                        playerAnimator = player.GetComponentInChildren<Animator>();

                    if (playerAnimator == null)
                        playerAnimator = player.GetComponentInParent<Animator>();
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

            // find FPS controller by type name to avoid compile dependency on CAS Demo assembly
            var playerObj = GameObject.FindGameObjectWithTag(playerTag);
            if (playerObj != null)
            {
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

                if (_fpsController != null)
                {
                    _setLookRotationMethod = _fpsController.GetType().GetMethod("SetLookRotation",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                }

                if (verboseDebug && _fpsController != null)
                    Debug.Log($"[PortalCASHandler] Found FPSExampleController for rotation sync");
            }

            _playerCamera = Camera.main;
            if (verboseDebug && _playerCamera != null)
                Debug.Log($"[PortalCASHandler] Found player camera: {_playerCamera.name}");
        }

        private void OnEnable()
        {
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
            var allComponents = playerAnimator.GetComponents<MonoBehaviour>();
            var casList = new System.Collections.Generic.List<MonoBehaviour>();

            foreach (var comp in allComponents)
            {
                string typeName = comp.GetType().FullName;

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

        private void OnPortalTeleport(string groupId, Transform fromPortal, Transform toPortal, Transform teleportedObject, Vector3 oldPos, Vector3 newPos)
        {
            bool isPlayer = teleportedObject.CompareTag(playerTag);

            // also handle cases where the portal system teleports a parent of the animator
            if (!isPlayer && playerAnimator != null)
            {
                isPlayer = teleportedObject == playerAnimator.transform ||
                           teleportedObject == playerAnimator.transform.root ||
                           playerAnimator.transform.IsChildOf(teleportedObject);
            }

            if (!isPlayer) return;
            if (_isTeleporting) return;

            _teleportedTransform = teleportedObject;
            _portalToDisable = fromPortal;

            // grab rotation values before we do anything else
            if (_playerCamera != null && _fpsController != null)
            {
                _portalTransformedYaw = teleportedObject.eulerAngles.y;

                _capturedPitch = _playerCamera.transform.localEulerAngles.x;
                if (_capturedPitch > 180f) _capturedPitch -= 360f;

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

            // disable CAS components immediately to prevent TransformStreamHandle errors
            if (_casComponents != null && _casComponents.Length > 0)
            {
                foreach (var comp in _casComponents)
                {
                    if (comp != null)
                        comp.enabled = false;
                }
            }

            bool wasAnimatorEnabled = false;
            if (playerAnimator != null)
            {
                wasAnimatorEnabled = playerAnimator.enabled;
                playerAnimator.enabled = false;
            }

            // wait a frame for physics
            yield return null;

            if (_teleportedTransform != null)
            {
                float finalYaw = _portalTransformedYaw + yawOffset;

                if (forwardOffset != 0 || rightOffset != 0)
                {
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

                _teleportedTransform.rotation = Quaternion.Euler(0, finalYaw, 0);

                if (_fpsController != null)
                {
                    Quaternion finalLookRotation = Quaternion.Euler(_capturedPitch, finalYaw, 0f);
                    _setLookRotationMethod?.Invoke(_fpsController, new object[] { finalLookRotation });

                    if (verboseDebug)
                        Debug.Log($"[PortalCASHandler] Applied FINAL rotation: Yaw={finalYaw:F1} (portal={_portalTransformedYaw:F1} + offset={yawOffset}), Pitch={_capturedPitch:F1}");
                }
            }

            if (softResetDelay > 0)
            {
                yield return new WaitForSeconds(softResetDelay);
            }

            if (softResetAfterTeleport && playerAnimator != null)
            {
                SoftResetAnimator();
            }

            if (playerAnimator != null && wasAnimatorEnabled)
            {
                playerAnimator.enabled = true;
            }

            yield return null;

            if (_casComponents != null)
            {
                foreach (var comp in _casComponents)
                {
                    if (comp != null)
                        comp.enabled = true;
                }
            }

            if (syncCameraRotation && _fpsController != null && _teleportedTransform != null)
            {
                SyncCameraRotation();
            }

            // sync again next frame in case FPS controller reset it
            yield return null;

            if (syncCameraRotation && _fpsController != null && _teleportedTransform != null)
            {
                SyncCameraRotation();
            }

            if (verboseDebug)
                Debug.Log("[PortalCASHandler] Teleport handling complete");

            _isTeleporting = false;

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

        // uses captured pitch and portal-computed yaw to sync FPS controller rotation
        private void SyncCameraRotation()
        {
            if (_fpsController == null || _teleportedTransform == null)
                return;

            float finalYaw = _portalTransformedYaw + yawOffset;

            _teleportedTransform.rotation = Quaternion.Euler(0, finalYaw, 0);

            Quaternion finalRotation = Quaternion.Euler(_capturedPitch, finalYaw, 0f);
            _setLookRotationMethod?.Invoke(_fpsController, new object[] { finalRotation });

            if (verboseDebug)
                Debug.Log($"[PortalCASHandler] Camera rotation FINAL sync: Yaw={finalYaw:F1}, Pitch={_capturedPitch:F1}");
        }

        // NEVER use Rebind() here - it destroys TransformStreamHandles and breaks KINEMATION
        public void SoftResetAnimator()
        {
            if (playerAnimator == null)
                return;

            if (verboseDebug)
                Debug.Log("[PortalCASHandler] Soft resetting animator...");

            bool wasEnabled = playerAnimator.enabled;
            playerAnimator.enabled = false;
            playerAnimator.enabled = wasEnabled;

            playerAnimator.Update(0f);

            if (verboseDebug)
                Debug.Log("[PortalCASHandler] Animator soft reset complete");
        }

        // alternative if SoftResetAnimator alone doesn't fix it - try disabling CAS components instead
        public void ResetCASComponents()
        {
            if (_casComponents == null || _casComponents.Length == 0)
                return;

            if (verboseDebug)
                Debug.Log("[PortalCASHandler] Resetting CAS components...");

            foreach (var comp in _casComponents)
            {
                if (comp != null)
                    comp.enabled = false;
            }

            StartCoroutine(ReenableCASCoroutine());
        }

        private IEnumerator ReenableCASCoroutine()
        {
            yield return null;

            foreach (var comp in _casComponents)
            {
                if (comp != null)
                    comp.enabled = true;
            }

            if (verboseDebug)
                Debug.Log("[PortalCASHandler] CAS components re-enabled");
        }

        // call before a custom teleport to cache animator state
        public void PrepareTeleport()
        {
            if (playerAnimator == null)
                return;

            _cachedController = playerAnimator.runtimeAnimatorController;
        }

        // call after a custom teleport to trigger the recovery coroutine
        public void CompleteTeleport()
        {
            StartCoroutine(HandleTeleportCoroutine());
        }
    }
}
