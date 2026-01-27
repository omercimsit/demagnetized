using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

namespace Streaming
{
    /// <summary>
    /// Distance-based additive scene loader/unloader.
    /// Place on a trigger point; when player approaches within loadDistance,
    /// the target scene loads additively. When player moves beyond unloadDistance,
    /// the scene unloads.
    /// </summary>
    public class SeamlessSceneLoader : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private string targetSceneName = "HDRP_TheCarnival";
        [SerializeField] private float loadDistance = 30f;
        [SerializeField] private float unloadDistance = 40f;

        [Header("Spawn Offset")]
        [Tooltip("Offset applied to all root objects in the loaded scene")]
        [SerializeField] private Vector3 sceneOffset = Vector3.zero;

        // State
        private bool _isLoaded;
        private bool _isLoading;
        private bool _isUnloading;
        private Transform _player;

        private void Start()
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) _player = p.transform;
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            _isLoading = false;
            _isUnloading = false;
        }

        private void Update()
        {
            if (_player == null) return;

            float dist = Vector3.Distance(_player.position, transform.position);

            // LOAD when player approaches
            if (dist < loadDistance && !_isLoaded && !_isLoading)
            {
                StartCoroutine(LoadSceneAsync());
            }
            // UNLOAD when player moves away
            else if (dist > unloadDistance && _isLoaded && !_isUnloading)
            {
                StartCoroutine(UnloadSceneAsync());
            }
        }

        private IEnumerator LoadSceneAsync()
        {
            // Guard: check if scene is already loaded
            Scene existing = SceneManager.GetSceneByName(targetSceneName);
            if (existing.isLoaded)
            {
                _isLoaded = true;
                yield break;
            }

            _isLoading = true;
            Debug.Log($"[Stream] Pre-loading scene: {targetSceneName}...");

            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(targetSceneName, LoadSceneMode.Additive);
            if (asyncLoad == null)
            {
                Debug.LogError($"[Stream] Failed to start loading {targetSceneName} - is it in Build Settings?");
                _isLoading = false;
                yield break;
            }

            asyncLoad.allowSceneActivation = true;

            while (!asyncLoad.isDone)
            {
                yield return null;
            }

            _isLoaded = true;
            _isLoading = false;

            // Apply offset to root objects if needed
            if (sceneOffset != Vector3.zero)
            {
                Scene loadedScene = SceneManager.GetSceneByName(targetSceneName);
                if (loadedScene.isLoaded)
                {
                    foreach (var root in loadedScene.GetRootGameObjects())
                    {
                        root.transform.position += sceneOffset;
                    }
                }
            }

            Debug.Log($"[Stream] {targetSceneName} READY!");
        }

        private IEnumerator UnloadSceneAsync()
        {
            Scene scene = SceneManager.GetSceneByName(targetSceneName);
            if (!scene.isLoaded)
            {
                _isLoaded = false;
                yield break;
            }

            _isUnloading = true;
            Debug.Log($"[Stream] Unloading scene: {targetSceneName}...");

            AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(scene);
            if (asyncUnload == null)
            {
                Debug.LogError($"[Stream] Failed to unload {targetSceneName}");
                _isUnloading = false;
                yield break;
            }

            while (!asyncUnload.isDone)
            {
                yield return null;
            }

            _isLoaded = false;
            _isUnloading = false;

            Debug.Log($"[Stream] {targetSceneName} unloaded.");
        }
    }
}
