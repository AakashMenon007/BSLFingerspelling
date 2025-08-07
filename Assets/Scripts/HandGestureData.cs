using UnityEngine;
using UnityEngine.XR.Hands;

[CreateAssetMenu(fileName = "NewHandGestureData", menuName = "BSL/Hand Gesture Data")]
public class HandGestureData : ScriptableObject
{
    public string gestureName;
    public JointData[] jointData;

    [System.Serializable]
    public class JointData
    {
        public XRHandJointID jointID;
        public Vector3 leftPosition;
        public Quaternion leftRotation;
        public Vector3 rightPosition;
        public Quaternion rightRotation;
    }
}
