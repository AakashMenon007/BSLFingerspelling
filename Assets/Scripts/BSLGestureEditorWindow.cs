#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;

public class BSLGestureEditorWindow : EditorWindow
{
    private BSLGestureRecorder recorder;
    private bool isCountingDown = false;
    private float countdownTime = 5f;
    private double startTime;

    [MenuItem("BSL Tools/Gesture Recorder")]
    public static void ShowWindow()
    {
        GetWindow<BSLGestureEditorWindow>("BSL Gesture Recorder");
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
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

        if (!isCountingDown)
        {
            if (GUILayout.Button("Capture and Save Gesture"))
            {
                StartCountdown();
            }
        }
        else
        {
            float remaining = Mathf.Max(0, countdownTime - (float)(EditorApplication.timeSinceStartup - startTime));
            EditorGUILayout.HelpBox($"Capturing in {remaining:F1} seconds...", MessageType.Warning);
        }
    }

    private void StartCountdown()
    {
        startTime = EditorApplication.timeSinceStartup;
        isCountingDown = true;
    }

    private void OnEditorUpdate()
    {
        if (isCountingDown)
        {
            float elapsed = (float)(EditorApplication.timeSinceStartup - startTime);
            if (elapsed >= countdownTime)
            {
                isCountingDown = false;
                CaptureAndSaveGesture();
                Repaint(); // Refresh GUI
            }
        }
    }

    private void CaptureAndSaveGesture()
    {
        var gesture = recorder.CaptureCurrentGesture();
        if (gesture != null)
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Gesture", "NewBSLGesture", "asset", "Save your BSL gesture asset");
            if (!string.IsNullOrEmpty(path))
            {
                gesture.gestureName = System.IO.Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(gesture, path);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Success", $"Gesture '{gesture.gestureName}' saved successfully!", "OK");
            }
        }
        else
        {
            EditorUtility.DisplayDialog("Error", "Could not record gesture. Make sure both hands are tracked.", "OK");
        }
    }
}
#endif
