using UnityEngine;
using UnityEngine.XR.Hands;

public static class HandFeatureDefs
{
    public struct Features
    {
        public bool tracked;
        public float[] curl;        // 5 curls (thumb..little) in 0..1
        public Vector3 palmNormal;  // world-space
        public Vector3 wristPos;    // world-space
        public float scale;         // wrist->middle MCP distance (m)
        public bool isLeft;
    }

    static bool PoseOf(XRHand hand, XRHandJointID id, out Pose p)
    {
        var j = hand.GetJoint(id);
        return j.TryGetPose(out p);
    }

    public static float HandScale(XRHand hand)
    {
        if (!PoseOf(hand, XRHandJointID.Wrist, out var w) ||
            !PoseOf(hand, XRHandJointID.MiddleMetacarpal, out var m)) return 0.06f;
        return Vector3.Distance(w.position, m.position);
    }

    public static Vector3 PalmNormal(XRHand hand)
    {
        PoseOf(hand, XRHandJointID.Wrist, out var wrist);
        PoseOf(hand, XRHandJointID.IndexMetacarpal, out var idx);
        PoseOf(hand, XRHandJointID.LittleMetacarpal, out var lit);
        var v1 = (idx.position - wrist.position).normalized;
        var v2 = (lit.position - wrist.position).normalized;
        var n = Vector3.Normalize(Vector3.Cross(v2, v1)); // out of the palm
        return n.sqrMagnitude > 0 ? n : Vector3.forward;
    }

    static float Curl(XRHand hand, XRHandJointID tip, XRHandJointID mcp, float scale)
    {
        if (!PoseOf(hand, tip, out var a) || !PoseOf(hand, mcp, out var b)) return 1f;
        return Mathf.Clamp01(Vector3.Distance(a.position, b.position) / Mathf.Max(0.001f, scale));
    }

    public static Features Extract(XRHand hand, bool isLeft)
    {
        var f = new Features { curl = new float[5], isLeft = isLeft, tracked = hand.isTracked };
        if (!f.tracked) return f;

        f.scale = HandScale(hand);
        f.palmNormal = PalmNormal(hand);
        PoseOf(hand, XRHandJointID.Wrist, out var wrist);
        f.wristPos = wrist.position;

        f.curl[0] = Curl(hand, XRHandJointID.ThumbTip, XRHandJointID.ThumbMetacarpal, f.scale);
        f.curl[1] = Curl(hand, XRHandJointID.IndexTip, XRHandJointID.IndexMetacarpal, f.scale);
        f.curl[2] = Curl(hand, XRHandJointID.MiddleTip, XRHandJointID.MiddleMetacarpal, f.scale);
        f.curl[3] = Curl(hand, XRHandJointID.RingTip, XRHandJointID.RingMetacarpal, f.scale);
        f.curl[4] = Curl(hand, XRHandJointID.LittleTip, XRHandJointID.LittleMetacarpal, f.scale);
        return f;
    }

    // Mirror features across sagittal plane (for comparing opposite hands)
    public static void MirrorInPlace(ref Features f)
    {
        f.palmNormal = new Vector3(-f.palmNormal.x, f.palmNormal.y, f.palmNormal.z);
        // curls are symmetric; wristPos mirroring is not applied (we compare inter-hand in head-local)
    }
}
