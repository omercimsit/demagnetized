using UnityEngine;
using System.Collections.Generic;

namespace Streaming
{
    // toggles indoor/outdoor object groups based on player distance to a door/transition point
    // no separate scene loads - just SetActive on root objects
    // TODO: the fade coroutine is not really finished, it just does instant for now
    public class AreaStreaming : MonoBehaviour
    {
        [Header("Area Parents")]
        [Tooltip("Indoor area root (e.g. Garage and all child objects)")]
        [SerializeField] private GameObject insideArea;

        [Tooltip("Outdoor area root (e.g. Factory and all child objects)")]
        [SerializeField] private GameObject outsideArea;

        [Header("Transition Settings")]
        [Tooltip("Transition point (door position)")]
        [SerializeField] private Transform transitionPoint;

        [Tooltip("Load indoor area when player is within this distance")]
        [SerializeField] private float insideLoadDistance = 25f;

        [Tooltip("Unload indoor area when player moves beyond this distance")]
        [SerializeField] private float insideUnloadDistance = 40f;

        [Tooltip("Unload outdoor area when player is this far inside")]
        [SerializeField] private float outsideUnloadDistance = 15f;

        [Header("Detection")]
        [Tooltip("Player tag")]
        [SerializeField] private string playerTag = "Player";

        [Tooltip("Check interval in seconds")]
        [SerializeField] private float checkInterval = 0.3f;

        [Header("Fade Settings")]
        [Tooltip("Use fade instead of instant toggle")]
        [SerializeField] private bool useFade = false;

        [Tooltip("Fade duration in seconds")]
        [SerializeField] private float fadeDuration = 0.5f;

        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = true;
        [SerializeField] private bool showGizmos = true;

        [Tooltip("Enable/disable streaming (for testing)")]
        [SerializeField] private bool enableStreaming = true;

        [Tooltip("Startup delay before streaming kicks in")]
        [SerializeField] private float startupDelay = 2f;

        private Transform _player;
        private float _lastCheckTime;
        private float _startTime;

        private bool _insideActive = false;
        private bool _outsideActive = true;
        private bool _playerInside = false;
        private bool _initialized = false;

        // cached for fade - only used if useFade is on
        private Renderer[] _insideRenderers;
        private Renderer[] _outsideRenderers;

        private void Start()
        {
            _startTime = Time.time;
            FindPlayer();

            if (useFade)
            {
                if (insideArea != null)
                    _insideRenderers = insideArea.GetComponentsInChildren<Renderer>(true);
                if (outsideArea != null)
                    _outsideRenderers = outsideArea.GetComponentsInChildren<Renderer>(true);
            }

            // keep both areas on at start - don't touch anything until the system is ready
            if (insideArea != null) _insideActive = insideArea.activeSelf;
            if (outsideArea != null) _outsideActive = outsideArea.activeSelf;

            Log($"Area Streaming initialized. Streaming enabled: {enableStreaming}");
        }

        private void Update()
        {
            if (Time.time - _startTime < startupDelay) return;
            if (!enableStreaming) return;
            if (Time.time - _lastCheckTime < checkInterval) return;
            _lastCheckTime = Time.time;

            if (_player == null)
            {
                FindPlayer();
                return;
            }

            // safety check - never disable outside if refs are missing
            if (transitionPoint == null || insideArea == null)
            {
                return;
            }

            CheckPlayerPosition();
        }

        private void FindPlayer()
        {
            var playerObj = GameObject.FindGameObjectWithTag(playerTag);
            if (playerObj != null)
            {
                _player = playerObj.transform;
            }
        }

        private void CheckPlayerPosition()
        {
            if (transitionPoint == null) return;

            float distanceToTransition = Vector3.Distance(_player.position, transitionPoint.position);

            Vector3 toPlayer = (_player.position - transitionPoint.position).normalized;
            float dot = Vector3.Dot(transitionPoint.forward, toPlayer);
            bool isInsideNow = dot > 0; // positive dot = player is on the inside side

            // inside area
            if (!_insideActive && distanceToTransition <= insideLoadDistance)
            {
                Log("Loading INSIDE area (approaching door)");
                SetAreaActive(insideArea, true);
                _insideActive = true;
            }
            else if (_insideActive && !_playerInside && distanceToTransition > insideUnloadDistance)
            {
                Log("Unloading INSIDE area (too far)");
                SetAreaActive(insideArea, false);
                _insideActive = false;
            }

            // outside area
            if (_outsideActive && isInsideNow && distanceToTransition > outsideUnloadDistance)
            {
                Log("Unloading OUTSIDE area (player deep inside)");
                SetAreaActive(outsideArea, false);
                _outsideActive = false;
                _playerInside = true;
            }
            else if (!_outsideActive && !isInsideNow)
            {
                Log("Loading OUTSIDE area (player exiting)");
                SetAreaActive(outsideArea, true);
                _outsideActive = true;
                _playerInside = false;
            }
            else if (!_outsideActive && distanceToTransition <= outsideUnloadDistance)
            {
                // player is near the door from inside, preload the outside
                Log("Preloading OUTSIDE area (approaching exit)");
                SetAreaActive(outsideArea, true);
                _outsideActive = true;
            }
        }

        private void SetAreaActive(GameObject area, bool active, bool instant = false)
        {
            if (area == null) return;

            if (useFade && !instant)
            {
                StartCoroutine(FadeArea(area, active));
            }
            else
            {
                area.SetActive(active);
            }
        }

        private System.Collections.IEnumerator FadeArea(GameObject area, bool fadeIn)
        {
            Renderer[] renderers = area == insideArea ? _insideRenderers : _outsideRenderers;
            if (renderers == null || renderers.Length == 0)
            {
                area.SetActive(fadeIn);
                yield break;
            }

            if (fadeIn)
            {
                area.SetActive(true);
            }

            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / fadeDuration;
                float alpha = fadeIn ? t : (1f - t);

                // this is simplified - for proper fade you'd need material instances
                // not great but works for now
                yield return null;
            }

            if (!fadeIn)
            {
                area.SetActive(false);
            }
        }

        // force both on - useful when debugging
        public void ForceLoadAll()
        {
            SetAreaActive(insideArea, true, instant: true);
            SetAreaActive(outsideArea, true, instant: true);
            _insideActive = true;
            _outsideActive = true;
        }

        public void SetPlayerInside(bool inside)
        {
            _playerInside = inside;

            if (inside)
            {
                SetAreaActive(insideArea, true, instant: true);
                SetAreaActive(outsideArea, false, instant: true);
                _insideActive = true;
                _outsideActive = false;
            }
            else
            {
                SetAreaActive(insideArea, false, instant: true);
                SetAreaActive(outsideArea, true, instant: true);
                _insideActive = false;
                _outsideActive = true;
            }
        }

        private void Log(string message)
        {
            if (showDebugLogs)
                Debug.Log($"<color=magenta>[AreaStreaming]</color> {message}");
        }

        private void OnDrawGizmos()
        {
            if (!showGizmos || transitionPoint == null) return;

            Vector3 pos = transitionPoint.position;

            // green = inside load
            Gizmos.color = new Color(0, 1, 0, 0.2f);
            Gizmos.DrawWireSphere(pos, insideLoadDistance);

            // yellow = inside unload
            Gizmos.color = new Color(1, 1, 0, 0.2f);
            Gizmos.DrawWireSphere(pos, insideUnloadDistance);

            // red = outside unload (smaller)
            Gizmos.color = new Color(1, 0, 0, 0.3f);
            Gizmos.DrawWireSphere(pos, outsideUnloadDistance);

            Gizmos.color = Color.blue;
            Gizmos.DrawRay(pos, transitionPoint.forward * 5f);
            Gizmos.DrawSphere(pos + transitionPoint.forward * 5f, 0.3f);

            Gizmos.color = Color.red;
            Gizmos.DrawRay(pos, -transitionPoint.forward * 5f);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(pos + transitionPoint.forward * 5.5f, "INSIDE →");
            UnityEditor.Handles.Label(pos - transitionPoint.forward * 5.5f, "← OUTSIDE");
            UnityEditor.Handles.Label(pos + Vector3.up * 2f,
                $"Inside: {(_insideActive ? "ON" : "OFF")}\nOutside: {(_outsideActive ? "ON" : "OFF")}");
#endif
        }
    }
}
