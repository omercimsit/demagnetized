using UnityEngine;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using CloneGame.Core;

namespace CloneProject
{
    /// <summary>
    /// Handles character visual swap during portal transitions.
    /// IMPORTANT: KINEMATION characters cannot be SetActive(false) - it crashes animation jobs!
    /// Instead, we hide/show renderers while keeping the animation system running.
    /// </summary>
    public class KinemationPortalSwap : MonoBehaviour
    {
        public static KinemationPortalSwap Instance { get; private set; }

        [Header("References")]
        public GameObject kinemationCharacter;
        public GameObject simpleCharacterPrefab;

        [Header("Debug")]
        public bool verboseDebug = false; // Disabled by default

        private GameObject _simpleCharacterInstance;
        private bool _isSwapped = false;
        private Renderer[] _kinemationRenderers;
        private CharacterAnimationComponent _casComponent;

        private void DisableSimpleCharacterCameras(GameObject root)
        {
            if (root == null) return;

            var cameras = root.GetComponentsInChildren<Camera>(true);
            foreach (var cam in cameras)
            {
                if (cam == null) continue;

                cam.enabled = false;
                // Prevent camera ownership conflicts when portal mesh is swapped.
                cam.tag = "Untagged";

                var listener = cam.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
        }

        private void Awake()
        {
            // Singleton pattern - don't override if this is a clone
            if (Instance != null && Instance != this)
            {
                if (verboseDebug) Debug.Log("[KinemationPortalSwap] Clone detected, skipping");
                Destroy(this);
                return;
            }

            Instance = this;

            if (kinemationCharacter == null)
                kinemationCharacter = transform.Find(GameConstants.PLAYER_CHARACTER_NAME)?.gameObject;

            if (kinemationCharacter != null)
            {
                // Cache renderers for fast show/hide
                _kinemationRenderers = kinemationCharacter.GetComponentsInChildren<Renderer>();
                _casComponent = kinemationCharacter.GetComponent<CharacterAnimationComponent>();
            }

            if (verboseDebug)
                Debug.Log($"[KinemationPortalSwap] Awake - Kinemation: {kinemationCharacter?.name}, Renderers: {_kinemationRenderers?.Length ?? 0}");
        }

        private void Start()
        {
            if (this == null) return;

            if (simpleCharacterPrefab != null)
            {
                _simpleCharacterInstance = Instantiate(simpleCharacterPrefab, transform);
                _simpleCharacterInstance.name = "SimplePlayer_ForPortal";
                _simpleCharacterInstance.SetActive(false);

                // Destroy ALL scripts - we only need the visual mesh for portal rendering
                foreach (var mono in _simpleCharacterInstance.GetComponentsInChildren<MonoBehaviour>())
                {
                    Destroy(mono);
                }

                // Disable CharacterController to prevent physics conflicts
                var cc = _simpleCharacterInstance.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;

                // Ensure visual-only proxy cannot ever hijack the player's FPS camera.
                DisableSimpleCharacterCameras(_simpleCharacterInstance);

                if (verboseDebug) Debug.Log("[KinemationPortalSwap] Simple character ready");
            }
            else
            {
                Debug.LogError("[KinemationPortalSwap] Prefab NULL!");
            }
        }

        /// <summary>
        /// Swap to simple character for portal rendering.
        /// KINEMATION stays active but hidden - prevents animation job crashes!
        /// </summary>
        public void SwapToSimple()
        {
            if (_isSwapped || kinemationCharacter == null || _simpleCharacterInstance == null)
            {
                if (verboseDebug) Debug.Log($"[KinemationPortalSwap] SwapToSimple skipped - isSwapped:{_isSwapped}");
                return;
            }

            // Position the simple character
            _simpleCharacterInstance.transform.localPosition = kinemationCharacter.transform.localPosition;
            _simpleCharacterInstance.transform.localRotation = kinemationCharacter.transform.localRotation;

            // CRITICAL: Don't SetActive(false) on KINEMATION character!
            // Just hide renderers - animation jobs keep running safely
            SetKinemationRenderersVisible(false);
            _simpleCharacterInstance.SetActive(true);

            _isSwapped = true;

            if (verboseDebug) Debug.Log("[KinemationPortalSwap] >>> SWAP TO SIMPLE (renderers hidden)");
        }

        /// <summary>
        /// Swap back to KINEMATION character after portal.
        /// </summary>
        public void SwapToKinemation()
        {
            if (!_isSwapped || kinemationCharacter == null || _simpleCharacterInstance == null)
            {
                if (verboseDebug) Debug.Log($"[KinemationPortalSwap] SwapToKinemation skipped - isSwapped:{_isSwapped}");
                return;
            }

            // Sync position from simple character
            kinemationCharacter.transform.localPosition = _simpleCharacterInstance.transform.localPosition;
            kinemationCharacter.transform.localRotation = _simpleCharacterInstance.transform.localRotation;

            // Hide simple, show KINEMATION
            _simpleCharacterInstance.SetActive(false);
            SetKinemationRenderersVisible(true);

            // Soft animator update (NO Rebind - it crashes KINEMATION!)
            var animator = kinemationCharacter.GetComponent<Animator>();
            if (animator != null)
            {
                animator.Update(0f);
            }

            _isSwapped = false;

            if (verboseDebug) Debug.Log("[KinemationPortalSwap] <<< SWAP TO KINEMATION (renderers visible)");
        }

        /// <summary>
        /// Show or hide KINEMATION character renderers without disabling the GameObject.
        /// This keeps animation jobs running safely.
        /// </summary>
        private void SetKinemationRenderersVisible(bool visible)
        {
            if (_kinemationRenderers == null) return;

            foreach (var renderer in _kinemationRenderers)
            {
                if (renderer != null)
                    renderer.enabled = visible;
            }
        }

        public static void SwapToSimpleStatic(Transform t = null)
        {
            if (Instance != null) Instance.SwapToSimple();
            else Debug.LogError("[KinemationPortalSwap] Instance is NULL!");
        }

        public static void SwapToKinemationStatic(Transform t = null)
        {
            if (Instance != null) Instance.SwapToKinemation();
            else Debug.LogError("[KinemationPortalSwap] Instance is NULL!");
        }
    }
}
