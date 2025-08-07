using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using TMPro;

public class DualGestureRecognizer : MonoBehaviour
{
    public List<DualHandGesture> savedGestures;
    public float recognitionThreshold = 0.4f;

    public Renderer feedbackCube; // Assign your cube in the inspector
    public TMP_Text gestureNameText; // Assign your TextMeshPro UI element

    private XRHandSubsystem handSubsystem;

    void Start()
    {
        handSubsystem = XRGeneralSettings.Instance.Manager.activeLoader.GetLoadedSubsystem<XRHandSubsystem>();

        if (handSubsystem == null)
        {
            Debug.LogError("XRHandSubsystem not found.");
            return;
        }
    }

    void Update()
    {
        if (handSubsystem == null || savedGestures == null || savedGestures.Count == 0)
            return;

        XRHand leftHand = handSubsystem.leftHand;
        XRHand rightHand = handSubsystem.rightHand;

        if (!leftHand.isTracked || !rightHand.isTracked)
        {
            UpdateFeedback(Color.red, "Hands not tracked");
            return;
        }

        HandShape currentLeft = GetCurrentHandShape(leftHand);
        HandShape currentRight = GetCurrentHandShape(rightHand);

        foreach (var gesture in savedGestures)
        {
            float leftDiff = CompareHandShapes(currentLeft, gesture.leftHandShape);
            float rightDiff = CompareHandShapes(currentRight, gesture.rightHandShape);

            Debug.Log($"Comparing to {gesture.gestureName} | Left Diff: {leftDiff:F2}, Right Diff: {rightDiff:F2}");

            if (leftDiff < recognitionThreshold && rightDiff < recognitionThreshold)
            {
                UpdateFeedback(Color.green, $"Detected: {gesture.gestureName}");
                return;
            }
        }

        UpdateFeedback(Color.gray, "No match");
    }

    void UpdateFeedback(Color color, string gestureText)
    {
        if (feedbackCube != null)
            feedbackCube.material.color = color;

        if (gestureNameText != null)
            gestureNameText.text = gestureText;
    }

    HandShape GetCurrentHandShape(XRHand hand)
    {
        var shape = ScriptableObject.CreateInstance<HandShape>();
        var data = new List<HandShape.FingerData>();

        XRHandJointID[] tipJoints = {
            XRHandJointID.ThumbTip, XRHandJointID.IndexTip,
            XRHandJointID.MiddleTip, XRHandJointID.RingTip, XRHandJointID.LittleTip
        };

        XRHandJointID[] baseJoints = {
            XRHandJointID.ThumbMetacarpal, XRHandJointID.IndexMetacarpal,
            XRHandJointID.MiddleMetacarpal, XRHandJointID.RingMetacarpal, XRHandJointID.LittleMetacarpal
        };

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
        return shape;
    }

    float CompareHandShapes(HandShape current, HandShape reference)
    {
        float totalDifference = 0f;

        for (int i = 0; i < reference.fingerCurls.Length; i++)
        {
            float diff = Mathf.Abs(current.fingerCurls[i].curl - reference.fingerCurls[i].curl);
            totalDifference += diff;
            Debug.Log($"Finger {i}: Current = {current.fingerCurls[i].curl:F2}, Ref = {reference.fingerCurls[i].curl:F2}, Diff = {diff:F2}");
        }

        return totalDifference / reference.fingerCurls.Length;
    }
}
