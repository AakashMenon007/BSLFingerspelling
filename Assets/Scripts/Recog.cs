using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class BSLGestureRecognizer : MonoBehaviour
{
    [Header("Gesture Recognition Settings")]
    [Tooltip("Assign your saved hand gestures here (ScriptableObjects).")]
    public HandGestureData[] gesturesToRecognize;

    [Header("Target Object")]
    [Tooltip("The GameObject (e.g., a cube) that will react to gesture recognition.")]
    public GameObject targetCube;

    [Header("Matching Thresholds")]
    public float positionThreshold = 0.02f; // Acceptable distance between joints
    public float rotationThreshold = 5f;     // Acceptable angle difference in degrees

    [Header("Colors")]
    public Color matchedColor = Color.green;
    private Color originalColor;

    private XRHandSubsystem handSubsystem;

    void Start()
    {
        // Store the original cube color
        if (targetCube != null)
            originalColor = targetCube.GetComponent<Renderer>().material.color;

        // Get XRHandSubsystem
        var subsystems = new List<XRHandSubsystem>();
        SubsystemManager.GetSubsystems(subsystems);
        if (subsystems.Count > 0)
            handSubsystem = subsystems[0];
        else
            Debug.LogError("No XRHandSubsystem found.");
    }

    void Update()
    {
        if (handSubsystem == null || !handSubsystem.leftHand.isTracked || !handSubsystem.rightHand.isTracked)
            return;

        bool matched = false;

        foreach (var gesture in gesturesToRecognize)
        {
            if (IsGestureMatch(gesture))
            {
                Debug.Log($"Gesture matched: {gesture.gestureName}");
                if (targetCube != null)
                    targetCube.GetComponent<Renderer>().material.color = matchedColor;
                matched = true;
                break;
            }
        }

        if (!matched && targetCube != null)
        {
            targetCube.GetComponent<Renderer>().material.color = originalColor;
        }
    }

    private bool IsGestureMatch(HandGestureData savedGesture)
    {
        foreach (var joint in savedGesture.jointData)
        {
            XRHandJoint leftJoint = handSubsystem.leftHand.GetJoint(joint.jointID);
            XRHandJoint rightJoint = handSubsystem.rightHand.GetJoint(joint.jointID);

            if (!leftJoint.TryGetPose(out Pose currentLeftPose) || !rightJoint.TryGetPose(out Pose currentRightPose))
                return false;

            float leftPosDistance = Vector3.Distance(currentLeftPose.position, joint.leftPosition);
            float rightPosDistance = Vector3.Distance(currentRightPose.position, joint.rightPosition);

            float leftAngle = Quaternion.Angle(currentLeftPose.rotation, joint.leftRotation);
            float rightAngle = Quaternion.Angle(currentRightPose.rotation, joint.rightRotation);

            if (leftPosDistance > positionThreshold || rightPosDistance > positionThreshold ||
                leftAngle > rotationThreshold || rightAngle > rotationThreshold)
            {
                return false; // Mismatch found
            }
        }

        return true;
    }
}
