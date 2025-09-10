using System;
using UnityEngine;

[CreateAssetMenu(fileName = "HandJointPoseAsset", menuName = "BSL/Hand Joint Pose Asset", order = 0)]
public class HandJointPoseAsset : ScriptableObject
{
    public enum HandSide { Left, Right }

    [Serializable]
    public struct BonePose
    {
        public string bonePath;        // full path under the rig root (e.g., "Armature/Hand/Wrist/Index_1")
        public Quaternion localRotation;
    }

    [Header("Metadata")]
    public string gestureName;
    public HandSide side;

    [Header("Source Rig Info")]
    public string sourcePrefabGuid;    // to help find the same model again (optional)
    public string sourceRootName;      // name of the rig root used when capturing

    [Header("Captured Poses")]
    public BonePose[] bones;           // all bones you care about (26 for your rig)
}
