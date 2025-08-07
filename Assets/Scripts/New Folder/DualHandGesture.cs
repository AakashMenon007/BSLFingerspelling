using UnityEngine;

[CreateAssetMenu(fileName = "NewDualHandGesture", menuName = "BSL/Dual Hand Gesture")]
public class DualHandGesture : ScriptableObject
{
    public string gestureName;
    public HandShape leftHandShape;
    public HandShape rightHandShape;
}
