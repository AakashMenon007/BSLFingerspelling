using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using System.Collections.Generic;
using System;

public class BSLGestureCaptureEditor : EditorWindow
{
    [MenuItem("BSL Tools/Capture Dual Hand Gesture")]
    public static void ShowWindow()
    {
        GetWindow<BSLGestureCaptureEditor>("Gesture Capture");
    }

    private string gestureName = "NewBSLGesture";

    private bool countdownActive = false;
    private float countdownTimeLeft = 0f;
    private DateTime countdownStartTime;

    void OnGUI()
    {
        GUILayout.Label("Capture Dual Hand Gesture", EditorStyles.boldLabel);
        gestureName = EditorGUILayout.TextField("Gesture Name", gestureName);

        EditorGUI.BeginDisabledGroup(countdownActive);
        if (GUILayout.Button("Capture Now"))
        {
            StartCountdown(5f); // Start a 5-second countdown
        }
        EditorGUI.EndDisabledGroup();

        if (countdownActive)
        {
            GUILayout.Label($"Capturing in: {Mathf.CeilToInt(countdownTimeLeft)} seconds...");
        }
    }

    void StartCountdown(float seconds)
    {
        countdownActive = true;
        countdownTimeLeft = seconds;
        countdownStartTime = DateTime.Now;
        EditorApplication.update += CountdownUpdate;
    }

    void CountdownUpdate()
    {
        TimeSpan elapsed = DateTime.Now - countdownStartTime;
        countdownTimeLeft = Mathf.Max(0f, 5f - (float)elapsed.TotalSeconds);

        if (countdownTimeLeft <= 0f)
        {
            countdownActive = false;
            EditorApplication.update -= CountdownUpdate;
            CaptureAndSaveGesture(gestureName);
        }

        Repaint(); // Refresh the UI
    }

    static void CaptureAndSaveGesture(string gestureName)
    {
        XRHandSubsystem handSubsystem = XRGeneralSettings.Instance.Manager.activeLoader
            .GetLoadedSubsystem<XRHandSubsystem>();

        if (handSubsystem == null)
        {
            Debug.LogError("XRHandSubsystem not found. Ensure XR Hands package is active.");
            return;
        }

        XRHand leftHand = handSubsystem.leftHand;
        XRHand rightHand = handSubsystem.rightHand;

        if (leftHand.isTracked && rightHand.isTracked)
        {
            var leftShape = CreateHandShape(leftHand, gestureName + "_Left");
            var rightShape = CreateHandShape(rightHand, gestureName + "_Right");

            DualHandGesture gestureAsset = ScriptableObject.CreateInstance<DualHandGesture>();
            gestureAsset.gestureName = gestureName;
            gestureAsset.leftHandShape = leftShape;
            gestureAsset.rightHandShape = rightShape;

            CreateFolderIfNotExists("Assets/BSLGestures");
            AssetDatabase.CreateAsset(gestureAsset, $"Assets/BSLGestures/{gestureName}_Dual.asset");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Gesture saved: {gestureName}_Dual.asset");
        }
        else
        {
            Debug.LogError("Both hands must be tracked to capture gesture.");
        }
    }

    public static readonly XRHandJointID[] tipJoints = {
        XRHandJointID.ThumbTip, XRHandJointID.IndexTip,
        XRHandJointID.MiddleTip, XRHandJointID.RingTip, XRHandJointID.LittleTip
    };

    public static readonly XRHandJointID[] baseJoints = {
        XRHandJointID.ThumbMetacarpal, XRHandJointID.IndexMetacarpal,
        XRHandJointID.MiddleMetacarpal, XRHandJointID.RingMetacarpal, XRHandJointID.LittleMetacarpal
    };

    static HandShape CreateHandShape(XRHand hand, string assetName)
    {
        var shape = ScriptableObject.CreateInstance<HandShape>();
        shape.handName = assetName;
        var data = new List<HandShape.FingerData>();

        for (int i = 0; i < 5; i++)
        {
            var tip = hand.GetJoint(tipJoints[i]);
            var baseJ = hand.GetJoint(baseJoints[i]);

            float curl = 0f;

            if (tip.TryGetPose(out Pose tipPose) && baseJ.TryGetPose(out Pose basePose))
            {
                Vector3 dir = (tipPose.position - basePose.position).normalized;
                Vector3 wristDir = hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wristPose)
                    ? (basePose.position - wristPose.position).normalized
                    : Vector3.up;

                curl = 1f - Mathf.Abs(Vector3.Dot(dir, wristDir));
            }

            data.Add(new HandShape.FingerData { fingerId = i, curl = curl });
        }

        shape.fingerCurls = data.ToArray();

        CreateFolderIfNotExists("Assets/BSLGestures");
        string path = $"Assets/BSLGestures/{assetName}.asset";
        AssetDatabase.CreateAsset(shape, path);
        AssetDatabase.SaveAssets();
        return shape;
    }

    static void CreateFolderIfNotExists(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder("Assets", "BSLGestures");
        }
    }
}
