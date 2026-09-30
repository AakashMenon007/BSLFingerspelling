using UnityEngine;
using UnityEngine.XR.Hands;
using RootMotion.FinalIK;

public class XRHandsToVRIKTargets : MonoBehaviour
{
    [Header("Wiring")]
    public VRIK vrik;
    public XRHandTrackingEvents leftEvents;
    public XRHandTrackingEvents rightEvents;
    public Transform leftHandTarget;
    public Transform rightHandTarget;

    [Header("Auto wrist orientation fix (optional)")]
    public bool autoCalibrateOnFirstTrack = true;
    Quaternion _leftRotFix = Quaternion.identity, _rightRotFix = Quaternion.identity;
    bool _leftCalibrated, _rightCalibrated;

    void OnEnable()
    {
        // Use Dynamic updates only (configure on the component) for arm IK.
        leftEvents.poseUpdated.AddListener(OnLeftPoseUpdated);
        rightEvents.poseUpdated.AddListener(OnRightPoseUpdated);

        // Optional: handle tracking loss to fall back to controllers, etc.
        // leftEvents.trackingChanged.AddListener(OnLeftTrackingChanged);
        // rightEvents.trackingChanged.AddListener(OnRightTrackingChanged);
    }

    void OnDisable()
    {
        leftEvents.poseUpdated.RemoveListener(OnLeftPoseUpdated);
        rightEvents.poseUpdated.RemoveListener(OnRightPoseUpdated);
    }

    void OnLeftPoseUpdated(Pose p) => Apply(p, leftHandTarget, ref _leftRotFix, ref _leftCalibrated, vrik?.references?.leftHand);
    void OnRightPoseUpdated(Pose p) => Apply(p, rightHandTarget, ref _rightRotFix, ref _rightCalibrated, vrik?.references?.rightHand);

    void Apply(Pose wristPose, Transform target, ref Quaternion rotFix, ref bool calibrated, Transform rigHandBone)
    {
        // Because targets live under Camera Offset, XR Hands poses can be applied as local.
        target.localPosition = wristPose.position;

        var rawRot = wristPose.rotation;

        // One-time orientation fix so the target’s axes match the rig hand bone axes.
        if (autoCalibrateOnFirstTrack && !calibrated && rigHandBone != null)
        {
            // We want: rawRot * rotFix == rigHandBone.rotation (in world); approximate once.
            rotFix = Quaternion.Inverse(rawRot) * rigHandBone.rotation;
            calibrated = true;
        }

        target.localRotation = rawRot * rotFix;
    }
}
