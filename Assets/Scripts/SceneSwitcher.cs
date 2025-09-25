using UnityEngine;
using UnityEngine.SceneManagement;

public class SimpleSceneLoader : MonoBehaviour
{
    [Header("Scene Settings")]
    [Tooltip("Name of the scene to load.")]
    public string sceneName;

    [Tooltip("Index of the scene to load (optional, used if sceneName is empty).")]
    public int sceneIndex = -1;

    // Call this function from a button OnClick or any other script
    public void LoadScene()
    {
        if (!string.IsNullOrEmpty(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
        else if (sceneIndex >= 0 && sceneIndex < SceneManager.sceneCountInBuildSettings)
        {
            SceneManager.LoadScene(sceneIndex);
        }
        else
        {
            Debug.LogError("SimpleSceneLoader: Please set a valid scene name or index in the inspector.");
        }
    }
}
