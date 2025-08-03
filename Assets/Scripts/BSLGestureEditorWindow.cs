#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class BSLGestureEditorWindow : EditorWindow
{
    private BSLGestureRecorder recorder;

    [MenuItem("BSL Tools/Gesture Recorder")]
    public static void ShowWindow()
    {
        GetWindow<BSLGestureEditorWindow>("BSL Gesture Recorder");
    }

    private void OnGUI()
    {
        GUILayout.Label("Record and Save Hand Gesture", EditorStyles.boldLabel);

        recorder = EditorGUILayout.ObjectField("Gesture Recorder", recorder, typeof(BSLGestureRecorder), true) as BSLGestureRecorder;

        if (recorder == null)
        {
            EditorGUILayout.HelpBox("Drag your GameObject with the BSLGestureRecorder component here.", MessageType.Info);
            return;
        }

        if (GUILayout.Button("Capture and Save Gesture"))
        {
            SaveGesture();
        }
    }

    private void SaveGesture()
    {
        var gesture = recorder.CaptureCurrentGesture();
        if (gesture != null)
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Gesture", "NewBSLGesture", "asset", "Save your BSL gesture asset");
            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.CreateAsset(gesture, path);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Success", "Gesture saved successfully!", "OK");
            }
        }
        else
        {
            EditorUtility.DisplayDialog("Error", "Could not record gesture. Make sure both hands are tracked.", "OK");
        }
    }
}
#endif
