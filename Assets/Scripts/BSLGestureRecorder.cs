using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class BSLGestureRecorder : MonoBehaviour
{
    private XRHandSubsystem handSubsystem;

    private static readonly XRHandJointID[] ValidJointIDs = new XRHandJointID[]
    {
        XRHandJointID.Wrist, XRHandJointID.Palm,
        XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip,
        XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip,
        XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip,
        XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip,
        XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip
    };

    void Start()
    {
        List<XRHandSubsystem> handSubsystems = new();
        SubsystemManager.GetSubsystems(handSubsystems);
        if (handSubsystems.Count > 0)
        {
            handSubsystem = handSubsystems[0];
        }
        else
        {
            Debug.LogError("No XRHandSubsystem found.");
        }
    }

    public HandGestureData CaptureCurrentGesture()
    {
        if (handSubsystem == null || !handSubsystem.leftHand.isTracked || !handSubsystem.rightHand.isTracked)
        {
            Debug.LogWarning("Both hands must be tracked to record a gesture.");
            return null;
        }

        List<HandGestureData.JointData> data = new();

        foreach (XRHandJointID jointID in ValidJointIDs)
        {
            XRHandJoint jointLeft = handSubsystem.leftHand.GetJoint(jointID);
            XRHandJoint jointRight = handSubsystem.rightHand.GetJoint(jointID);

            if (jointLeft.TryGetPose(out Pose poseLeft) && jointRight.TryGetPose(out Pose poseRight))
            {
                var jointData = new HandGestureData.JointData
                {
                    jointID = jointID,
                    leftPosition = poseLeft.position,
                    leftRotation = poseLeft.rotation,
                    rightPosition = poseRight.position,
                    rightRotation = poseRight.rotation
                };

                data.Add(jointData);
            }
        }

        var gestureAsset = ScriptableObject.CreateInstance<HandGestureData>();
        gestureAsset.jointData = data.ToArray();
        return gestureAsset;
    }

#if UNITY_EDITOR
    public void SaveHandPoseAsPrefab(HandGestureData gesture, GameObject handMeshPrefab, string savePath, bool isLeft)
    {
        GameObject handInstance = GameObject.Instantiate(handMeshPrefab);
        handInstance.name = gesture.gestureName + (isLeft ? "_Left" : "_Right");

        foreach (var joint in gesture.jointData)
        {
            string jointName = joint.jointID.ToString();
            Transform jointTransform = handInstance.transform.FindDeepChild(jointName);

            if (jointTransform != null)
            {
                jointTransform.localPosition = isLeft ? joint.leftPosition : joint.rightPosition;
                jointTransform.localRotation = isLeft ? joint.leftRotation : joint.rightRotation;
            }
            else
            {
                Debug.LogWarning($"Joint '{jointName}' not found in hand prefab.");
            }
        }

        string fullPath = $"Assets/{savePath}/{handInstance.name}.prefab";
        PrefabUtility.SaveAsPrefabAssetAndConnect(handInstance, fullPath, InteractionMode.UserAction);
        Debug.Log($"Saved posed hand prefab: {fullPath}");
        DestroyImmediate(handInstance);
    }
#endif
}