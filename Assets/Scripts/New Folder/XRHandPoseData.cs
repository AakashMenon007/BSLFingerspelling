using System;
using UnityEngine;
using UnityEngine.XR.Hands;

[CreateAssetMenu(menuName = "XR/Static Hand Pose", fileName = "XRHandPoseData")]
public class XRHandPoseData : ScriptableObject
{
    public enum SpaceMode { World, OriginLocal }

    [Header("Meta")]
    public string poseName = "NewPose";
    public bool isLeftHand = true;
    public SpaceMode spaceMode = SpaceMode.OriginLocal;

    [Serializable] public class PoseS { public bool tracked; public Vector3 p; public Quaternion q; }
    [Serializable] public class JointRotS { public int id; public bool tracked; public Quaternion localQ; }

    [Header("Pose")]
    public PoseS wrist;             // wrist pose in 'spaceMode'
    public PoseS palm;              // palm pose in 'spaceMode' (reference)
    public JointRotS[] joints;      // local joint rotations (relative to XR parent joints)

    // Local copy of the XR joint order (no external dependency)
    static readonly XRHandJointID[] k_Joints = new XRHandJointID[]
    {
        XRHandJointID.Wrist, XRHandJointID.Palm,
        XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip,
        XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip,
        XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip,
        XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip,
        XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip,
    };

    /// <summary>
    /// Apply this static pose to a rig (spawns/positions ghost hand). 
    /// Pass jointOffsetsOrNull if you calibrated per-joint offsets; otherwise null.
    /// </summary>
    public void ApplyToRig(XRHandGhostRig rig, Transform origin, bool useJointOffsets, Quaternion[] jointOffsetsOrNull)
    {
        if (!rig || joints == null || joints.Length == 0 || !wrist.tracked) return;

        // Convert pose space → world
        Vector3 Pw = (spaceMode == SpaceMode.World || origin == null) ? wrist.p : origin.TransformPoint(wrist.p);
        Quaternion Qw = (spaceMode == SpaceMode.World || origin == null) ? wrist.q : origin.rotation * wrist.q;

        if (rig.wristRoot)
        {
            rig.wristRoot.position = Pw;
            rig.wristRoot.rotation = Qw;
        }

        // Apply joint locals
        int count = Mathf.Min(joints.Length, k_Joints.Length);
        for (int i = 0; i < count; i++)
        {
            var bone = rig.GetBone(k_Joints[i]);
            if (!bone) continue;

            if (i == 0) { bone.localRotation = Quaternion.identity; continue; } // wrist local is neutral

            var rec = joints[i].localQ;
            var off = (useJointOffsets && jointOffsetsOrNull != null && i < jointOffsetsOrNull.Length)
                      ? jointOffsetsOrNull[i]
                      : Quaternion.identity;

            bone.localRotation = off * rec;
        }
    }
}
