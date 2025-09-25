using UnityEngine;
using UnityEngine.SceneManagement;

public class SimpleSceneLoader : MonoBehaviour
{
    [Header("Scene Settings")]
    [Tooltip("Name of the scene to load. (Preferred)")]
    public string sceneName;

    [Tooltip("Index of the scene to load (used if sceneName is empty).")]
    public int sceneIndex = -1;

    [Header("Options")]
    public LoadSceneMode loadMode = LoadSceneMode.Single;
    [Tooltip("Use async loading (recommended).")]
    public bool useAsync = true;
    [Tooltip("If true, sets Time.timeScale = 1 before loading (fixes stuck UI if you paused).")]
    public bool resetTimeScale = true;

    // Call this from a Button OnClick
    public void LoadScene()
    {
        if (resetTimeScale) Time.timeScale = 1f;

        // Prefer name if provided
        if (!string.IsNullOrEmpty(sceneName))
        {
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[SimpleSceneLoader] Scene '{sceneName}' is NOT in Build Settings or name/casing is wrong.");
                return;
            }

            if (useAsync)
            {
                var op = SceneManager.LoadSceneAsync(sceneName, loadMode);
                if (op == null) Debug.LogError($"[SimpleSceneLoader] Failed to start async load for '{sceneName}'.");
            }
            else
            {
                SceneManager.LoadScene(sceneName, loadMode);
            }
            return;
        }

        // Fallback to index
        if (sceneIndex < 0 || sceneIndex >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogError($"[SimpleSceneLoader] Invalid scene index {sceneIndex}. Check Build Settings.");
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneIndex))
        {
            Debug.LogError($"[SimpleSceneLoader] Scene index {sceneIndex} is NOT in Build Settings.");
            return;
        }

        if (useAsync)
        {
            var op = SceneManager.LoadSceneAsync(sceneIndex, loadMode);
            if (op == null) Debug.LogError($"[SimpleSceneLoader] Failed to start async load for index {sceneIndex}.");
        }
        else
        {
            SceneManager.LoadScene(sceneIndex, loadMode);
        }
    }
}
