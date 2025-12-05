using UnityEngine;
using DamianGonzalez.Portals;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;

namespace CloneProject
{
    /// <summary>
    /// Handles KINEMATION Character Animation System compatibility with portal teleportation.
    /// IMPORTANT: Do NOT use Animator.Rebind() with KINEMATION - it invalidates TransformStreamHandles!
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class CASPortalCompatibility : MonoBehaviour
    {
        [SerializeField] private float resetDelay = 0.05f;
        [SerializeField] private bool verboseDebug = false;

        private Animator _animator;
        private Transform _myTransform;
        private CharacterAnimationComponent _casComponent;
        private bool _isKinemationCharacter;

        private void Awake()
        {
            // Animator might be on a child (e.g., Banana Man)
            _animator = GetComponent<Animator>();
            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();

            _myTransform = transform;

            // Check if this is a KINEMATION character
            _casComponent = GetComponentInChildren<CharacterAnimationComponent>();
            _isKinemationCharacter = _casComponent != null;

            if (verboseDebug)
            {
                Debug.Log($"[CASPortalCompatibility] Initialized - Animator: {_animator != null}, KINEMATION: {_isKinemationCharacter}");
            }
        }

        private void OnEnable()
        {
            PortalEvents.teleport += OnTeleport;
        }

        private void OnDisable()
        {
            PortalEvents.teleport -= OnTeleport;
        }

        private void OnTeleport(string groupId, Transform portalFrom, Transform portalTo,
                                Transform objectTeleported, Vector3 positionFrom, Vector3 positionTo)
        {
            if (objectTeleported != _myTransform) return;

            if (verboseDebug)
                Debug.Log($"[CASPortalCompatibility] Teleported! KINEMATION mode: {_isKinemationCharacter}");

            StartCoroutine(HandleTeleportReset());
        }

        private System.Collections.IEnumerator HandleTeleportReset()
        {
            yield return new WaitForSeconds(resetDelay);

            if (_animator == null) yield break;

            if (_isKinemationCharacter)
            {
                // KINEMATION characters: Do NOT rebind - just reset animator state softly
                // Rebind() destroys the playable graph and invalidates TransformStreamHandles

                // Option 1: Just trigger a state update without rebinding
                _animator.Update(0f);

                // Option 2: If animations look broken, reset to default state
                // _animator.Play(_animator.GetCurrentAnimatorStateInfo(0).fullPathHash, 0, 0f);

                if (verboseDebug)
                    Debug.Log("[CASPortalCompatibility] KINEMATION soft reset applied (no rebind)");
            }
            else
            {
                // Non-KINEMATION characters: Soft reset (disable/enable cycle).
                // Even non-KINEMATION paths should avoid Rebind() as a safety measure
                // in case CAS components are added later.
                _animator.enabled = false;
                _animator.enabled = true;
                _animator.Update(0f);

                if (verboseDebug)
                    Debug.Log("[CASPortalCompatibility] Soft reset applied (no rebind)");
            }
        }
    }
}
