using System.Linq;
using UnityEngine;
using UnityEngine.XR.Hands;

[DisallowMultipleComponent]
public class XRHandGhostRig : MonoBehaviour
{
    [Header("Rig root / renderer")]
    [Tooltip("Root bone that follows the recorded wrist world pose.")]
    public Transform wristRoot;
    [Tooltip("Optional: set for ghost material swap during replay.")]
    public SkinnedMeshRenderer skinnedMesh;

    [Header("Drag your bones here (no IDs needed)")]
    public Transform Wrist;
    public Transform Palm;

    public Transform ThumbMetacarpal;
    public Transform ThumbProximal;
    public Transform ThumbDistal;
    public Transform ThumbTip;

    public Transform IndexMetacarpal;
    public Transform IndexProximal;
    public Transform IndexIntermediate;
    public Transform IndexDistal;
    public Transform IndexTip;

    public Transform MiddleMetacarpal;
    public Transform MiddleProximal;
    public Transform MiddleIntermediate;
    public Transform MiddleDistal;
    public Transform MiddleTip;

    public Transform RingMetacarpal;
    public Transform RingProximal;
    public Transform RingIntermediate;
    public Transform RingDistal;
    public Transform RingTip;

    public Transform LittleMetacarpal;
    public Transform LittleProximal;
    public Transform LittleIntermediate;
    public Transform LittleDistal;
    public Transform LittleTip;

    void OnValidate()
    {
        // Convenience: if wristRoot not set, use Wrist if present
        if (!wristRoot && Wrist) wristRoot = Wrist;
    }

    // API expected by XRHandRecorderMesh (no IDs exposed in Inspector)
    public Transform GetBone(XRHandJointID id)
    {
        switch (id)
        {
            case XRHandJointID.Wrist: return Wrist;
            case XRHandJointID.Palm: return Palm;

            case XRHandJointID.ThumbMetacarpal: return ThumbMetacarpal;
            case XRHandJointID.ThumbProximal: return ThumbProximal;
            case XRHandJointID.ThumbDistal: return ThumbDistal;
            case XRHandJointID.ThumbTip: return ThumbTip;

            case XRHandJointID.IndexMetacarpal: return IndexMetacarpal;
            case XRHandJointID.IndexProximal: return IndexProximal;
            case XRHandJointID.IndexIntermediate: return IndexIntermediate;
            case XRHandJointID.IndexDistal: return IndexDistal;
            case XRHandJointID.IndexTip: return IndexTip;

            case XRHandJointID.MiddleMetacarpal: return MiddleMetacarpal;
            case XRHandJointID.MiddleProximal: return MiddleProximal;
            case XRHandJointID.MiddleIntermediate: return MiddleIntermediate;
            case XRHandJointID.MiddleDistal: return MiddleDistal;
            case XRHandJointID.MiddleTip: return MiddleTip;

            case XRHandJointID.RingMetacarpal: return RingMetacarpal;
            case XRHandJointID.RingProximal: return RingProximal;
            case XRHandJointID.RingIntermediate: return RingIntermediate;
            case XRHandJointID.RingDistal: return RingDistal;
            case XRHandJointID.RingTip: return RingTip;

            case XRHandJointID.LittleMetacarpal: return LittleMetacarpal;
            case XRHandJointID.LittleProximal: return LittleProximal;
            case XRHandJointID.LittleIntermediate: return LittleIntermediate;
            case XRHandJointID.LittleDistal: return LittleDistal;
            case XRHandJointID.LittleTip: return LittleTip;
        }
        return null;
    }

    // Optional helpers to speed up setup
    [ContextMenu("Auto-Fill Common Bones By Name")]
    void AutoFillByName()
    {
        var all = GetComponentsInChildren<Transform>(true);

        Wrist = Wrist ?? FindByKeys(all, "wrist", "hand_root", "root");
        Palm = Palm ?? FindByKeys(all, "palm", "hand", "hand_palm");

        ThumbMetacarpal = ThumbMetacarpal ?? FindFinger(all, "thumb", "metacarpal", "meta", "1");
        ThumbProximal = ThumbProximal ?? FindFinger(all, "thumb", "proximal", "prox", "2");
        ThumbDistal = ThumbDistal ?? FindFinger(all, "thumb", "distal", "dist", "3");
        ThumbTip = ThumbTip ?? FindFinger(all, "thumb", "tip");

        IndexMetacarpal = IndexMetacarpal ?? FindFinger(all, "index", "metacarpal", "meta", "1");
        IndexProximal = IndexProximal ?? FindFinger(all, "index", "proximal", "prox", "2");
        IndexIntermediate = IndexIntermediate ?? FindFinger(all, "index", "intermediate", "mid", "3");
        IndexDistal = IndexDistal ?? FindFinger(all, "index", "distal", "dist", "4");
        IndexTip = IndexTip ?? FindFinger(all, "index", "tip");

        MiddleMetacarpal = MiddleMetacarpal ?? FindFinger(all, "middle", "metacarpal", "meta", "1");
        MiddleProximal = MiddleProximal ?? FindFinger(all, "middle", "proximal", "prox", "2");
        MiddleIntermediate = MiddleIntermediate ?? FindFinger(all, "middle", "intermediate", "mid", "3");
        MiddleDistal = MiddleDistal ?? FindFinger(all, "middle", "distal", "dist", "4");
        MiddleTip = MiddleTip ?? FindFinger(all, "middle", "tip");

        RingMetacarpal = RingMetacarpal ?? FindFinger(all, "ring", "metacarpal", "meta", "1");
        RingProximal = RingProximal ?? FindFinger(all, "ring", "proximal", "prox", "2");
        RingIntermediate = RingIntermediate ?? FindFinger(all, "ring", "intermediate", "mid", "3");
        RingDistal = RingDistal ?? FindFinger(all, "ring", "distal", "dist", "4");
        RingTip = RingTip ?? FindFinger(all, "ring", "tip");

        LittleMetacarpal = LittleMetacarpal ?? FindFinger(all, "little", "pinky", "metacarpal", "meta", "1");
        LittleProximal = LittleProximal ?? FindFinger(all, "little", "pinky", "proximal", "prox", "2");
        LittleIntermediate = LittleIntermediate ?? FindFinger(all, "little", "pinky", "intermediate", "mid", "3");
        LittleDistal = LittleDistal ?? FindFinger(all, "little", "pinky", "distal", "dist", "4");
        LittleTip = LittleTip ?? FindFinger(all, "little", "pinky", "tip");

        // default wristRoot if still empty
        if (!wristRoot && Wrist) wristRoot = Wrist;
    }

    static Transform FindByKeys(Transform[] all, params string[] keys)
    {
        string Norm(string s) => s.ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");
        foreach (var t in all)
        {
            var n = Norm(t.name);
            if (keys.Any(k => n.Contains(Norm(k)))) return t;
        }
        return null;
    }

    static Transform FindFinger(Transform[] all, string fingerA, string fingerB = null, params string[] rankKeys)
    {
        string Norm(string s) => s.ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");
        foreach (var t in all)
        {
            var n = Norm(t.name);
            bool hasFinger = n.Contains(Norm(fingerA)) || (fingerB != null && n.Contains(Norm(fingerB)));
            if (!hasFinger) continue;
            if (rankKeys.Length == 0) return t;
            if (rankKeys.Any(k => n.Contains(Norm(k)))) return t;
        }
        return null;
    }
}
