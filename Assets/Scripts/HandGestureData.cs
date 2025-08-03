using UnityEngine;
using UnityEngine.XR.Hands;

[CreateAssetMenu(fileName = "NewHandGesture", menuName = "BSL/Hand Gesture")]
public class HandGestureData : ScriptableObject
{
    public string gestureName; // 🟢 This is the missing field causing the error.

    [System.Serializable]
    public struct JointData
    {
        public XRHandJointID jointID;
        public Vector3 leftPosition;
        public Quaternion leftRotation;
        public Vector3 rightPosition;
        public Quaternion rightRotation;
    }

    public JointData[] jointData;
}
