using UnityEngine;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using CloneGame.Core;

namespace CloneProject
{
    // Swaps the visible character during portal transitions.
    // KINEMATION can't be SetActive(false) - that crashes animation jobs.
    // So we just toggle renderers and keep the whole GO alive.
    public class KinemationPortalSwap : MonoBehaviour
    {
        public static KinemationPortalSwap Instance { get; private set; }

        [Header("References")]
        public GameObject kinemationCharacter;
        public GameObject simpleCharacterPrefab;

        [Header("Debug")]
        public bool verboseDebug = false;

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
                // untag so it doesn't accidentally become MainCamera
                cam.tag = "Untagged";

                var listener = cam.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
        }

        private void Awake()
        {
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

                // strip all scripts - we only want the mesh for portal rendering
                foreach (var mono in _simpleCharacterInstance.GetComponentsInChildren<MonoBehaviour>())
                {
                    Destroy(mono);
                }

                var cc = _simpleCharacterInstance.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;

                DisableSimpleCharacterCameras(_simpleCharacterInstance);

                if (verboseDebug) Debug.Log("[KinemationPortalSwap] Simple character ready");
            }
            else
            {
                Debug.LogError("[KinemationPortalSwap] Prefab NULL!");
            }
        }

        // hide KINEMATION renderers and show the simple proxy instead
        // don't SetActive(false) the KINEMATION GO or the animation jobs will crash
        public void SwapToSimple()
        {
            if (_isSwapped || kinemationCharacter == null || _simpleCharacterInstance == null)
            {
                if (verboseDebug) Debug.Log($"[KinemationPortalSwap] SwapToSimple skipped - isSwapped:{_isSwapped}");
                return;
            }

            _simpleCharacterInstance.transform.localPosition = kinemationCharacter.transform.localPosition;
            _simpleCharacterInstance.transform.localRotation = kinemationCharacter.transform.localRotation;

            SetKinemationRenderersVisible(false);
            _simpleCharacterInstance.SetActive(true);

            _isSwapped = true;

            if (verboseDebug) Debug.Log("[KinemationPortalSwap] >>> SWAP TO SIMPLE (renderers hidden)");
        }

        // restore KINEMATION character after portal is done
        public void SwapToKinemation()
        {
            if (!_isSwapped || kinemationCharacter == null || _simpleCharacterInstance == null)
            {
                if (verboseDebug) Debug.Log($"[KinemationPortalSwap] SwapToKinemation skipped - isSwapped:{_isSwapped}");
                return;
            }

            kinemationCharacter.transform.localPosition = _simpleCharacterInstance.transform.localPosition;
            kinemationCharacter.transform.localRotation = _simpleCharacterInstance.transform.localRotation;

            _simpleCharacterInstance.SetActive(false);
            SetKinemationRenderersVisible(true);

            // soft update only - Rebind() crashes KINEMATION
            var animator = kinemationCharacter.GetComponent<Animator>();
            if (animator != null)
            {
                animator.Update(0f);
            }

            _isSwapped = false;

            if (verboseDebug) Debug.Log("[KinemationPortalSwap] <<< SWAP TO KINEMATION (renderers visible)");
        }

        // toggle renderer visibility without touching the GameObject's active state
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
