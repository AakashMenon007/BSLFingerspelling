using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using TMPro;

public class XRHandPoseMatcherBSL : MonoBehaviour
{
    // ---------- Joint order & parents (same as your original) ----------
    static readonly XRHandJointID[] J = new XRHandJointID[]
    {
        XRHandJointID.Wrist, XRHandJointID.Palm,
        XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip,
        XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip,
        XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip,
        XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip,
        XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip,
    };

    static readonly int[] P = new int[]
    {   // parents (index into J)
        -1, 0,  1,2,3,4,  1,6,7,8,9,  1,11,12,13,14,  1,16,17,18,19,  1,21,22,23,24
    };

    // ---------- Config per gesture (letter) ----------
    public enum HandRelation { Any, SideBySide, Stacked } // SideBySide: hands level; Stacked: one above the other

    [Serializable]
    public class BSLGesture
    {
        [Header("Label & Targets")]
        public string label = "A";                 // e.g., "A".."Z"
        public XRHandGhostRig targetLeft;          // can be null if gesture is right-only
        public XRHandGhostRig targetRight;         // can be null if gesture is left-only
        public bool requireLeft = true;            // set depending on the letter
        public bool requireRight = false;          // set depending on the letter

        [Header("Spatial relation & distance checks (only used if both hands required)")]
        public HandRelation relation = HandRelation.SideBySide;
        [Tooltip("Minimum 3D distance between wrists to count as 'separate hands'.")]
        public float minHandDistanceM = 0.06f;     // 6 cm default
        [Tooltip("For Stacked: minimum vertical separation to consider one above the other.")]
        public float minVerticalSeparationM = 0.06f;
        [Tooltip("For SideBySide: allowed vertical difference to still be considered 'level'.")]
        public float verticalLevelToleranceM = 0.06f;
        [Tooltip("For Stacked: hands should be roughly aligned in XZ; this is the max planar separation.")]
        public float maxPlanarSeparationM = 0.15f;

        // ----- Internal caches -----
        [NonSerialized] public Quaternion[] leftLocals, rightLocals;
        [NonSerialized] public bool[] leftHas, rightHas;
        [NonSerialized] public float leftT, rightT;        // stability timers
        [NonSerialized] public bool leftGreen, rightGreen; // per-hand green state
    }

    [Header("Gesture set (fill with up to 26 entries for A..Z)")]
    public List<BSLGesture> gestures = new List<BSLGesture>(26);

    [Header("Materials (swapped on target ghost mesh when green/unmatched)")]
    public Material unmatchedMaterial;   // e.g., translucent red/grey
    public Material matchedMaterial;     // e.g., translucent green

    [Header("Finger shape tolerances (degrees)")]
    [Tooltip("Average angle difference across all compared joints must be <= this.")]
    public float avgJointDegThreshold = 10f;
    [Tooltip("Any single joint angle difference must be <= this.")]
    public float perJointDegThreshold = 25f;

    [Header("Root alignment tolerances")]
    [Tooltip("Max distance (meters) between live wrist and target wrist.")]
    public float wristPosToleranceM = 0.05f;
    [Tooltip("Max rotation delta (degrees) at the root (wrist or palm).")]
    public float rootRotToleranceDeg = 20f;
    [Tooltip("For LEFT, use PALM orientation (often fixes tilt).")]
    public bool usePalmRotationForLeft = true;
    [Tooltip("For RIGHT, use PALM orientation.")]
    public bool usePalmRotationForRight = false;

    [Header("Stability / Debounce")]
    [Tooltip("How long (seconds) a hand must continuously match before turning green.")]
    public float matchHoldSeconds = 0.15f;

    [Header("Output Text (works with 3D TextMeshPro or uGUI)")]
    public TMP_Text recognizedText;      // assign a TextMeshPro (3D) OR TextMeshProUGUI
    public string noMatchText = "";      // what to display when nothing matches
    [Tooltip("If true, rotates the 3D text to face the camera (ignored for uGUI).")]
    public bool faceCamera = false;

    [Header("Debug")]
    public bool showDebugScore = false;

    XRHandSubsystem _hands;

    // Cache original materials to restore when not matched
    readonly Dictionary<XRHandGhostRig, Material> _originalMat = new Dictionary<XRHandGhostRig, Material>();

    void Awake()
    {
        var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
        _hands = loader?.GetLoadedSubsystem<XRHandSubsystem>();
        if (_hands == null) Debug.LogError("[XRHandPoseMatcherBSL] XRHandSubsystem not available.");

        BakeAllTargets();
        CacheOriginalMaterials();
        if (recognizedText) recognizedText.text = noMatchText;
    }

    void CacheOriginalMaterials()
    {
        foreach (var g in gestures)
        {
            if (g.targetLeft && g.targetLeft.skinnedMesh && !_originalMat.ContainsKey(g.targetLeft))
                _originalMat[g.targetLeft] = g.targetLeft.skinnedMesh.sharedMaterial;
            if (g.targetRight && g.targetRight.skinnedMesh && !_originalMat.ContainsKey(g.targetRight))
                _originalMat[g.targetRight] = g.targetRight.skinnedMesh.sharedMaterial;
        }
    }

    void BakeAllTargets()
    {
        foreach (var g in gestures)
        {
            if (g.targetLeft) BakeOne(g.targetLeft, out g.leftLocals, out g.leftHas);
            if (g.targetRight) BakeOne(g.targetRight, out g.rightLocals, out g.rightHas);
        }
    }

    void BakeOne(XRHandGhostRig rig, out Quaternion[] locals, out bool[] has)
    {
        locals = new Quaternion[J.Length];
        has = new bool[J.Length];
        for (int i = 0; i < J.Length; i++)
        {
            var t = rig.GetBone(J[i]);
            if (t)
            {
                locals[i] = t.localRotation; // captured pose’s authored local rotations
                has[i] = true;
            }
            else
            {
                locals[i] = Quaternion.identity;
                has[i] = false;
            }
        }
    }

    void Update()
    {
        if (_hands == null) return;

        bool anyRecognized = false;
        string bestLabel = null;

        foreach (var g in gestures)
        {
            bool leftOK = !g.requireLeft || EvaluateOne(_hands.leftHand, g.targetLeft, g.leftLocals, g.leftHas, true, ref g.leftT, ref g.leftGreen);
            bool rightOK = !g.requireRight || EvaluateOne(_hands.rightHand, g.targetRight, g.rightLocals, g.rightHas, false, ref g.rightT, ref g.rightGreen);

            // Both required hands must be green
            if (leftOK && rightOK)
            {
                // If gesture requires both hands, apply spatial checks; otherwise accept immediately.
                bool passesRelation = true;
                if (g.requireLeft && g.requireRight)
                    passesRelation = CheckSpatialRelation(g, out _, out _, out _);

                if (passesRelation)
                {
                    anyRecognized = true;
                    bestLabel = g.label;
                    // Policy: last matching gesture wins. Change to "break;" if you prefer first-wins.
                }
            }
        }

        if (recognizedText)
            recognizedText.text = anyRecognized ? bestLabel : noMatchText;
    }

    void LateUpdate()
    {
        // Optional billboard for 3D TextMeshPro
        if (faceCamera && recognizedText)
        {
            var canvas = recognizedText as TextMeshProUGUI;
            if (canvas == null) // only rotate world-space (3D) text
            {
                var cam = Camera.main;
                if (cam)
                {
                    Vector3 fwd = (recognizedText.transform.position - cam.transform.position).normalized;
                    fwd.y = 0f; // keep level
                    if (fwd.sqrMagnitude > 1e-6f)
                        recognizedText.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                }
            }
        }
    }

    bool EvaluateOne(XRHand xr, XRHandGhostRig rig, Quaternion[] targetLocals, bool[] hasJoint,
                     bool isLeft, ref float matchTimer, ref bool isGreen)
    {
        if ((rig == null) || (targetLocals == null) || (hasJoint == null)) return false;

        // 1) Sample XR world rotations
        var xrWorldQ = new Quaternion[J.Length];
        var xrTracked = new bool[J.Length];
        for (int i = 0; i < J.Length; i++)
        {
            var j = xr.GetJoint(J[i]);
            if (j.TryGetPose(out Pose p)) { xrTracked[i] = true; xrWorldQ[i] = p.rotation; }
            else { xrTracked[i] = false; xrWorldQ[i] = Quaternion.identity; }
        }

        // 2) Build locals
        var xrLocalQ = new Quaternion[J.Length];
        for (int i = 0; i < J.Length; i++)
        {
            int pi = P[i];
            if (pi < 0) xrLocalQ[i] = Quaternion.identity; // wrist local
            else
            {
                if (xrTracked[i] && xrTracked[pi]) xrLocalQ[i] = Quaternion.Inverse(xrWorldQ[pi]) * xrWorldQ[i];
                else if (xrTracked[i]) xrLocalQ[i] = xrWorldQ[i];
                else xrLocalQ[i] = Quaternion.identity;
            }
        }

        // 3) Finger shape comparison (skip 0 = wrist)
        float sumDeg = 0f; int cnt = 0; float maxDeg = 0f;
        for (int i = 1; i < J.Length; i++)
        {
            if (!hasJoint[i]) continue;
            float deg = Quaternion.Angle(targetLocals[i], xrLocalQ[i]);
            if (!float.IsFinite(deg)) continue;
            sumDeg += deg; cnt++;
            if (deg > maxDeg) maxDeg = deg;
        }
        float avgDeg = (cnt > 0) ? (sumDeg / cnt) : 180f;

        // 4) Root alignment vs target rig
        var wr = xr.GetJoint(XRHandJointID.Wrist);
        float posDelta = 999f;
        Pose wPose = default;
        if (wr.TryGetPose(out wPose) && rig.wristRoot)
            posDelta = Vector3.Distance(wPose.position, rig.wristRoot.position);

        var palm = xr.GetJoint(XRHandJointID.Palm);
        Quaternion liveRootQ = wPose.rotation;
        if ((isLeft && usePalmRotationForLeft) || (!isLeft && usePalmRotationForRight))
            if (palm.TryGetPose(out Pose pPose)) liveRootQ = pPose.rotation;

        float rotDelta = 999f;
        if (rig.wristRoot)
            rotDelta = Quaternion.Angle(rig.wristRoot.rotation, liveRootQ);

        bool shapeOK = (avgDeg <= avgJointDegThreshold) && (maxDeg <= perJointDegThreshold);
        bool rootOK = (posDelta <= wristPosToleranceM) && (rotDelta <= rootRotToleranceDeg);
        bool frameOK = shapeOK && rootOK;

        // 5) Stability & material swap
        if (frameOK) matchTimer += Time.deltaTime;
        else matchTimer = 0f;

        bool nowGreen = matchTimer >= matchHoldSeconds;
        if (nowGreen != isGreen)
        {
            isGreen = nowGreen;
            SwapMat(rig, isGreen ? matchedMaterial : unmatchedMaterial);
        }

        if (showDebugScore)
            Debug.Log($"[{(isLeft ? "LEFT" : "RIGHT")}:{(rig ? rig.name : "null")}] avg:{avgDeg:0.0}° max:{maxDeg:0.0}° pos:{posDelta:0.000}m rot:{rotDelta:0.0}° => {(frameOK ? "OK" : "NO")} {(isGreen ? "GREEN" : "")}");

        return isGreen;
    }

    bool CheckSpatialRelation(BSLGesture g, out float dist3D, out float planar, out float vert)
    {
        dist3D = planar = vert = float.MaxValue;

        if (!_hands.leftHand.isTracked || !_hands.rightHand.isTracked) return false;

        var lW = _hands.leftHand.GetJoint(XRHandJointID.Wrist);
        var rW = _hands.rightHand.GetJoint(XRHandJointID.Wrist);
        if (!lW.TryGetPose(out Pose lPose) || !rW.TryGetPose(out Pose rPose)) return false;

        Vector3 dlr = rPose.position - lPose.position;
        dist3D = dlr.magnitude;

        // Assume world up for standing/sitting users
        Vector3 up = Vector3.up;
        Vector3 dlrPlanarV = Vector3.ProjectOnPlane(dlr, up);
        planar = dlrPlanarV.magnitude;
        vert = Mathf.Abs(Vector3.Dot(dlr, up));

        // Enforce minimum 3D distance to avoid fused-hands false positives
        if (dist3D < g.minHandDistanceM) return false;

        switch (g.relation)
        {
            case HandRelation.Any:
                return true;

            case HandRelation.SideBySide:
                // Hands roughly at the same height
                return (vert <= g.verticalLevelToleranceM);

            case HandRelation.Stacked:
                // One above the other, and not far apart in XZ
                return (vert >= g.minVerticalSeparationM) && (planar <= g.maxPlanarSeparationM);

            default:
                return true;
        }
    }

    void SwapMat(XRHandGhostRig rig, Material mat)
    {
        if (!rig || !rig.skinnedMesh) return;
        if (mat == null)
        {
            if (_originalMat.TryGetValue(rig, out var orig))
                rig.skinnedMesh.sharedMaterial = orig;
            return;
        }
        rig.skinnedMesh.sharedMaterial = mat;
    }

    void OnDisable()
    {
        // Restore materials
        foreach (var kv in _originalMat)
            if (kv.Key && kv.Key.skinnedMesh)
                kv.Key.skinnedMesh.sharedMaterial = kv.Value;

        // Reset per-gesture state
        foreach (var g in gestures)
        {
            g.leftT = g.rightT = 0f;
            g.leftGreen = g.rightGreen = false;
        }

        if (recognizedText) recognizedText.text = noMatchText;
    }
}
