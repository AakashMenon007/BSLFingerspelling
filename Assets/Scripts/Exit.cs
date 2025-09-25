using UnityEngine;

public class ExitApp : MonoBehaviour
{
    // Call this from a Button OnClick
    public void QuitApplication()
    {
        Debug.Log("Exit button pressed");

#if UNITY_EDITOR
        // Stop play mode if running in the editor
        UnityEditor.EditorApplication.isPlaying = false;
#else
        // Quit the app in a built player
        Application.Quit();
#endif
    }
}
