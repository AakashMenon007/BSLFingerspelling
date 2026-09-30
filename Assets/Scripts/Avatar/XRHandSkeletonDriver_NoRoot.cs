using UnityEngine;
using UnityEngine.XR.Hands;

// Custom version of XRHandSkeletonDriver that ignores wrist/root updates
public class XRHandSkeletonDriver_NoRoot : XRHandSkeletonDriver
{
    // Prevent the wrist/root from moving—VRIK will handle that.
    protected override void OnRootPoseUpdated(Pose rootPose)
    {
        // Do nothing here on purpose
    }
}
