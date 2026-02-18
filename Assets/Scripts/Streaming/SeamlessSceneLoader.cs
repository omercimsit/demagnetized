using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

namespace Streaming
{
    // loads/unloads a scene additively based on player distance to this trigger point
    // put this on an empty GameObject near the area transition
    public class SeamlessSceneLoader : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private string targetSceneName = "HDRP_TheCarnival";
        [SerializeField] private float loadDistance = 30f;
        [SerializeField] private float unloadDistance = 40f;

        [Header("Spawn Offset")]
        [Tooltip("Offset applied to all root objects in the loaded scene")]
        [SerializeField] private Vector3 sceneOffset = Vector3.zero;

        // state flags
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

            // load when player gets close
            if (dist < loadDistance && !_isLoaded && !_isLoading)
            {
                StartCoroutine(LoadSceneAsync());
            }
            // unload when player walks away
            else if (dist > unloadDistance && _isLoaded && !_isUnloading)
            {
                StartCoroutine(UnloadSceneAsync());
            }
        }

        private IEnumerator LoadSceneAsync()
        {
            // already loaded? skip
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

            // shift everything if an offset was specified
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
