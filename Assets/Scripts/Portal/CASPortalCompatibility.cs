using UnityEngine;
using DamianGonzalez.Portals;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;

namespace CloneProject
{
    // Handles animator state after portal teleport for KINEMATION characters.
    // DO NOT call Animator.Rebind() here - it destroys TransformStreamHandles and crashes the jobs.
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
            // animator might be on a child object
            _animator = GetComponent<Animator>();
            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();

            _myTransform = transform;

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
                // soft reset only - Rebind() invalidates TransformStreamHandles with KINEMATION
                _animator.Update(0f);

                // if animations still look broken after teleport, could try replaying current state:
                // _animator.Play(_animator.GetCurrentAnimatorStateInfo(0).fullPathHash, 0, 0f);

                if (verboseDebug)
                    Debug.Log("[CASPortalCompatibility] KINEMATION soft reset applied (no rebind)");
            }
            else
            {
                // non-KINEMATION path: disable/enable is safer than Rebind in case CAS gets added later
                _animator.enabled = false;
                _animator.enabled = true;
                _animator.Update(0f);

                if (verboseDebug)
                    Debug.Log("[CASPortalCompatibility] Soft reset applied (no rebind)");
            }
        }
    }
}
