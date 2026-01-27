using UnityEngine;
using System.Collections.Generic;

namespace Streaming
{
    /// <summary>
    /// Toggles indoor/outdoor GameObject groups based on player distance to a transition point.
    /// Avoids separate scene loads — just activates/deactivates root objects.
    /// </summary>
    public class AreaStreaming : MonoBehaviour
    {
        [Header("=== AREA PARENTS ===")]
        [Tooltip("Indoor area root (e.g. Garage and all child objects)")]
        [SerializeField] private GameObject insideArea;
        
        [Tooltip("Outdoor area root (e.g. Factory and all child objects)")]
        [SerializeField] private GameObject outsideArea;
        
        [Header("=== TRANSITION SETTINGS ===")]
        [Tooltip("Transition point (door position)")]
        [SerializeField] private Transform transitionPoint;
        
        [Tooltip("Load indoor area when player is within this distance")]
        [SerializeField] private float insideLoadDistance = 25f;
        
        [Tooltip("Unload indoor area when player moves beyond this distance")]
        [SerializeField] private float insideUnloadDistance = 40f;
        
        [Tooltip("Unload outdoor area when player is this far inside")]
        [SerializeField] private float outsideUnloadDistance = 15f;
        
        [Header("=== DETECTION ===")]
        [Tooltip("Player tag")]
        [SerializeField] private string playerTag = "Player";
        
        [Tooltip("Check interval in seconds")]
        [SerializeField] private float checkInterval = 0.3f;
        
        [Header("=== FADE SETTINGS ===")]
        [Tooltip("Use fade instead of instant toggle")]
        [SerializeField] private bool useFade = false;
        
        [Tooltip("Fade duration in seconds")]
        [SerializeField] private float fadeDuration = 0.5f;
        
        [Header("=== DEBUG ===")]
        [SerializeField] private bool showDebugLogs = true;
        [SerializeField] private bool showGizmos = true;
        
        [Tooltip("Enable/disable streaming (for testing)")]
        [SerializeField] private bool enableStreaming = true;
        
        [Tooltip("Startup delay before streaming kicks in")]
        [SerializeField] private float startupDelay = 2f;
        
        // State
        private Transform _player;
        private float _lastCheckTime;
        private float _startTime;
        
        private bool _insideActive = false;
        private bool _outsideActive = true;
        private bool _playerInside = false;
        #pragma warning disable CS0414
        private bool _initialized = false;
        #pragma warning restore CS0414
        
        // Cached renderers for fade
        private Renderer[] _insideRenderers;
        private Renderer[] _outsideRenderers;
        
        private void Start()
        {
            _startTime = Time.time;
            FindPlayer();
            
            // Cache renderers if using fade
            if (useFade)
            {
                if (insideArea != null)
                    _insideRenderers = insideArea.GetComponentsInChildren<Renderer>(true);
                if (outsideArea != null)
                    _outsideRenderers = outsideArea.GetComponentsInChildren<Renderer>(true);
            }
            
            // IMPORTANT: Keep both areas ON at start until system is ready
            // Don't disable anything automatically
            if (insideArea != null) _insideActive = insideArea.activeSelf;
            if (outsideArea != null) _outsideActive = outsideArea.activeSelf;
            
            Log($"Area Streaming initialized. Streaming enabled: {enableStreaming}");
        }
        
        private void Update()
        {
            // Wait for startup delay before doing anything
            if (Time.time - _startTime < startupDelay) return;
            
            // Skip if streaming is disabled
            if (!enableStreaming) return;
            
            if (Time.time - _lastCheckTime < checkInterval) return;
            _lastCheckTime = Time.time;
            
            if (_player == null)
            {
                FindPlayer();
                return;
            }
            
            // Safety: Never disable outside if references not set
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
            
            // Determine if player is inside or outside based on position relative to transition
            Vector3 toPlayer = (_player.position - transitionPoint.position).normalized;
            float dot = Vector3.Dot(transitionPoint.forward, toPlayer);
            bool isInsideNow = dot > 0; // Positive = inside direction
            
            // === INSIDE AREA LOGIC ===
            if (!_insideActive && distanceToTransition <= insideLoadDistance)
            {
                // Approaching door - load inside
                Log("Loading INSIDE area (approaching door)");
                SetAreaActive(insideArea, true);
                _insideActive = true;
            }
            else if (_insideActive && !_playerInside && distanceToTransition > insideUnloadDistance)
            {
                // Moved far away - unload inside
                Log("Unloading INSIDE area (too far)");
                SetAreaActive(insideArea, false);
                _insideActive = false;
            }
            
            // === OUTSIDE AREA LOGIC ===
            if (_outsideActive && isInsideNow && distanceToTransition > outsideUnloadDistance)
            {
                // Player went deep inside - unload outside
                Log("Unloading OUTSIDE area (player deep inside)");
                SetAreaActive(outsideArea, false);
                _outsideActive = false;
                _playerInside = true;
            }
            else if (!_outsideActive && !isInsideNow)
            {
                // Player exited to outside - load outside
                Log("Loading OUTSIDE area (player exiting)");
                SetAreaActive(outsideArea, true);
                _outsideActive = true;
                _playerInside = false;
            }
            else if (!_outsideActive && distanceToTransition <= outsideUnloadDistance)
            {
                // Player near door from inside - preload outside
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
            // For fade, we need renderers
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
                
                // This is simplified - for proper fade you'd need material instances
                // For now, just instant
                yield return null;
            }
            
            if (!fadeIn)
            {
                area.SetActive(false);
            }
        }
        
        /// <summary>
        /// Force load both areas (for debugging)
        /// </summary>
        public void ForceLoadAll()
        {
            SetAreaActive(insideArea, true, instant: true);
            SetAreaActive(outsideArea, true, instant: true);
            _insideActive = true;
            _outsideActive = true;
        }
        
        /// <summary>
        /// Force player inside state
        /// </summary>
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
            
            // Inside load distance (green)
            Gizmos.color = new Color(0, 1, 0, 0.2f);
            Gizmos.DrawWireSphere(pos, insideLoadDistance);
            
            // Inside unload distance (yellow)
            Gizmos.color = new Color(1, 1, 0, 0.2f);
            Gizmos.DrawWireSphere(pos, insideUnloadDistance);
            
            // Outside unload distance (red - smaller)
            Gizmos.color = new Color(1, 0, 0, 0.3f);
            Gizmos.DrawWireSphere(pos, outsideUnloadDistance);
            
            // Direction arrows
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(pos, transitionPoint.forward * 5f);
            Gizmos.DrawSphere(pos + transitionPoint.forward * 5f, 0.3f);
            
            Gizmos.color = Color.red;
            Gizmos.DrawRay(pos, -transitionPoint.forward * 5f);
            
            // Labels
            #if UNITY_EDITOR
            UnityEditor.Handles.Label(pos + transitionPoint.forward * 5.5f, "INSIDE →");
            UnityEditor.Handles.Label(pos - transitionPoint.forward * 5.5f, "← OUTSIDE");
            UnityEditor.Handles.Label(pos + Vector3.up * 2f, 
                $"Inside: {(_insideActive ? "ON" : "OFF")}\nOutside: {(_outsideActive ? "ON" : "OFF")}");
            #endif
        }
    }
}
