#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class HandPrefabSaverEditor : EditorWindow
{
    public BSLGestureRecorder gestureRecorder;
    public GameObject handMeshPrefab;
    public string saveFolder = "SavedHandPoses";
    public bool isLeft = true;

    [MenuItem("BSL Tools/Save Posed Hand As Prefab")]
    public static void ShowWindow()
    {
        GetWindow<HandPrefabSaverEditor>("Save Hand Pose Prefab");
    }

    void OnGUI()
    {
        // ✅ Allow dragging from the scene by setting allowSceneObjects = true
        gestureRecorder = (BSLGestureRecorder)EditorGUILayout.ObjectField("Gesture Recorder", gestureRecorder, typeof(BSLGestureRecorder), true);
        handMeshPrefab = (GameObject)EditorGUILayout.ObjectField("Hand Mesh Prefab", handMeshPrefab, typeof(GameObject), true);

        saveFolder = EditorGUILayout.TextField("Save Folder", saveFolder);
        isLeft = EditorGUILayout.Toggle("Is Left Hand", isLeft);

        if (GUILayout.Button("Save Current Pose As Prefab"))
        {
            if (gestureRecorder == null || handMeshPrefab == null)
            {
                Debug.LogError("Assign both recorder and hand mesh prefab.");
                return;
            }

            HandGestureData gesture = gestureRecorder.CaptureCurrentGesture();
            if (gesture == null)
            {
                Debug.LogWarning("Failed to record gesture.");
                return;
            }

            gesture.gestureName = System.DateTime.Now.ToString("Pose_yyyyMMdd_HHmmss");
            gestureRecorder.SaveHandPoseAsPrefab(gesture, handMeshPrefab, saveFolder, isLeft);
        }
    }
}
#endif
