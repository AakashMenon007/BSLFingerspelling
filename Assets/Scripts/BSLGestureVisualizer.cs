using UnityEngine;
using UnityEngine.XR.Hands;

public class BSLHandPoseApplier : MonoBehaviour
{
    [Header("Assign the Gesture Asset")]
    public HandGestureData gestureToApply;

    [Header("Assign Left and Right Hand Joints")]
    public Transform[] leftHandJoints = new Transform[26];
    public Transform[] rightHandJoints = new Transform[26];

    [ContextMenu("Apply Gesture To Hands")]
    public void ApplyGesture()
    {
        if (gestureToApply == null || gestureToApply.jointData.Length != 26)
        {
            Debug.LogError("Invalid gesture data.");
            return;
        }

        for (int i = 0; i < 26; i++)
        {
            if (leftHandJoints[i] != null)
            {
                leftHandJoints[i].localPosition = gestureToApply.jointData[i].leftPosition;
                leftHandJoints[i].localRotation = gestureToApply.jointData[i].leftRotation;
            }

            if (rightHandJoints[i] != null)
            {
                rightHandJoints[i].localPosition = gestureToApply.jointData[i].rightPosition;
                rightHandJoints[i].localRotation = gestureToApply.jointData[i].rightRotation;
            }
        }

        Debug.Log("Gesture applied to hand mesh.");
    }
}
