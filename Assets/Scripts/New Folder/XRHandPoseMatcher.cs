using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using TMPro;

public class XRHandPoseMatcherBSL : MonoBehaviour
{
    static readonly XRHandJointID[] J = new XRHandJointID[]
    {
        XRHandJointID.Wrist, XRHandJointID.Palm,
        XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip,
        XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip,
        XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip,
        XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip,
        XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip,
    };

    static readonly int[] P = new int[] { -1, 0, 1, 2, 3, 4, 1, 6, 7, 8, 9, 1, 11, 12, 13, 14, 1, 16, 17, 18, 19, 1, 21, 22, 23, 24 };

    public enum HandRelation { Any, SideBySide, Stacked }

    [Serializable]
    public class BSLGesture
    {
        [Header("Label & Targets")]
        public string label = "A";
        public XRHandGhostRig targetLeft;
        public XRHandGhostRig targetRight;
        public bool requireLeft = true;
        public bool requireRight = false;

        [Header("Spatial relation (when both hands required)")]
        public HandRelation relation = HandRelation.SideBySide;
        public float minHandDistanceM = 0.06f;
        public float minVerticalSeparationM = 0.06f;
        public float verticalLevelToleranceM = 0.06f;
        public float maxPlanarSeparationM = 0.15f;

        [NonSerialized] public Quaternion[] leftLocals, rightLocals;
        [NonSerialized] public bool[] leftHas, rightHas;
        [NonSerialized] public float leftT, rightT;
        [NonSerialized] public bool leftGreen, rightGreen;
    }

    [Header("Gesture set (A..Z)")]
    public List<BSLGesture> gestures = new List<BSLGesture>(26);

    [Header("Materials")]
    public Material unmatchedMaterial;
    public Material matchedMaterial;

    [Header("Finger shape tolerances (deg)")]
    public float avgJointDegThreshold = 10f;
    public float perJointDegThreshold = 25f;

    [Header("Root alignment tolerances")]
    public float wristPosToleranceM = 0.05f;
    public float rootRotToleranceDeg = 20f;
    public bool usePalmRotationForLeft = true;
    public bool usePalmRotationForRight = false;

    [Header("Stability / Debounce")]
    public float matchHoldSeconds = 0.15f;

    public enum RecognitionMode { AnyBest, PrioritizeSelected, OnlySelected }

    [Header("Recognition priority (carousel/UI)")]
    public RecognitionMode recognitionMode = RecognitionMode.OnlySelected;
    [Tooltip("Set from UI/trainer. -1 = none selected.")]
    public int selectedGestureIndex = -1;

    [Header("Latch after recognition")]
    public float recognitionLatchSeconds = 2f;

    [Header("Auto-Advance")]
    [Tooltip("When true, after a gesture latches and the latch window ends, the carousel moves to the next letter automatically.")]
    public bool autoAdvanceEnabled = true;

    [Tooltip("Extra delay (seconds) after the latch window before advancing.")]
    public float autoAdvanceDelay = 0.1f;

    float _autoAdvanceAt = -1f;
    bool _pendingAutoAdvance = false;

    [Header("Output Text (debug)")]
    public TMP_Text recognizedText;
    public string noMatchText = "";
    public bool faceCamera = false;

    [Tooltip("When true, this component will NOT write to recognizedText (prevents TMP conflicts with your training UI).")]
    public bool suppressRecognizedTextOutput = true;

    [Header("Debug")]
    public bool showDebugScore = false;

    // Fired when a gesture latches (gestureIndex, label)
    public event Action<int, string> OnGestureLatched;

    XRHandSubsystem _hands;
    readonly Dictionary<XRHandGhostRig, Material> _originalMat = new Dictionary<XRHandGhostRig, Material>();

    int _latchedIndex = -1;
    float _latchUntil = 0f;

    void Awake()
    {
        var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
        _hands = loader?.GetLoadedSubsystem<XRHandSubsystem>();
        if (_hands == null) Debug.LogError("[XRHandPoseMatcherBSL] XRHandSubsystem not available.");

        BakeAllTargets();
        CacheOriginalMaterials();
        if (!suppressRecognizedTextOutput && recognizedText) recognizedText.text = noMatchText;
    }

    public void SetRecognizedTextTarget(TMP_Text text)
    {
        recognizedText = text;
        if (!suppressRecognizedTextOutput && recognizedText)
            recognizedText.text = noMatchText;
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
            if (t) { locals[i] = t.localRotation; has[i] = true; }
            else { locals[i] = Quaternion.identity; has[i] = false; }
        }
    }

    void Update()
    {
        if (_hands == null) return;

        // Handle scheduled auto-advance (runs after latch window expires)
        if (_pendingAutoAdvance && Time.time >= _autoAdvanceAt)
        {
            _pendingAutoAdvance = false;
            _autoAdvanceAt = -1f;

            if (gestures.Count > 0)
            {
                NextGesture();
            }
        }

        // Keep latched visuals/text during hold
        if (_latchedIndex >= 0)
        {
            if (Time.time < _latchUntil)
            {
                if (!suppressRecognizedTextOutput && recognizedText) recognizedText.text = gestures[_latchedIndex].label;
                MaintainLatchVisuals();
                return;
            }
            else
            {
                ClearLatch();
            }
        }

        // Live evaluation (in priority order)
        int choice = -1;
        var order = BuildEvalOrder();

        if (order.Count == 0)
        {
            if (!suppressRecognizedTextOutput && recognizedText) recognizedText.text = noMatchText;
            return;
        }

        foreach (int gi in order)
        {
            var g = gestures[gi];
            bool leftOK = !g.requireLeft || EvaluateOne(_hands.leftHand, g.targetLeft, g.leftLocals, g.leftHas, true, ref g.leftT, ref g.leftGreen);
            bool rightOK = !g.requireRight || EvaluateOne(_hands.rightHand, g.targetRight, g.rightLocals, g.rightHas, false, ref g.rightT, ref g.rightGreen);

            if (leftOK && rightOK)
            {
                bool passes = true;
                if (g.requireLeft && g.requireRight)
                    passes = CheckSpatialRelation(g, out _, out _, out _);

                if (passes) { choice = gi; break; }
            }
        }

        if (choice >= 0)
        {
            _latchedIndex = choice;
            _latchUntil = Time.time + Mathf.Max(0.01f, recognitionLatchSeconds);

            if (!suppressRecognizedTextOutput && recognizedText) recognizedText.text = gestures[choice].label;
            ForceGreenVisuals(gestures[choice]);

            // Schedule auto-advance to fire right after the latch window
            if (autoAdvanceEnabled && gestures.Count > 0)
            {
                _pendingAutoAdvance = true;
                _autoAdvanceAt = _latchUntil + Mathf.Max(0f, autoAdvanceDelay);
            }

            OnGestureLatched?.Invoke(choice, gestures[choice].label);
        }
        else
        {
            if (!suppressRecognizedTextOutput && recognizedText) recognizedText.text = noMatchText;
        }
    }

    List<int> BuildEvalOrder()
    {
        var order = new List<int>();
        bool validSelected = selectedGestureIndex >= 0 && selectedGestureIndex < gestures.Count;

        switch (recognitionMode)
        {
            case RecognitionMode.OnlySelected:
                if (validSelected) order.Add(selectedGestureIndex);
                break;
            case RecognitionMode.PrioritizeSelected:
                if (validSelected) order.Add(selectedGestureIndex);
                for (int i = 0; i < gestures.Count; i++)
                    if (i != selectedGestureIndex) order.Add(i);
                break;
            case RecognitionMode.AnyBest:
                for (int i = 0; i < gestures.Count; i++) order.Add(i);
                break;
        }
        return order;
    }

    void LateUpdate()
    {
        if (faceCamera && recognizedText)
        {
            var tmpu = recognizedText as TextMeshProUGUI;
            if (tmpu == null)
            {
                var cam = Camera.main;
                if (cam)
                {
                    Vector3 fwd = (recognizedText.transform.position - cam.transform.position).normalized;
                    fwd.y = 0f;
                    if (fwd.sqrMagnitude > 1e-6f)
                        recognizedText.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                }
            }
        }
    }

    bool EvaluateOne(XRHand xr, XRHandGhostRig rig, Quaternion[] targetLocals, bool[] hasJoint,
                     bool isLeft, ref float matchTimer, ref bool isGreen, bool forceStickGreen = false)
    {
        if ((rig == null) || (targetLocals == null) || (hasJoint == null)) return false;

        if (forceStickGreen)
        {
            isGreen = true;
            SwapMat(rig, matchedMaterial);
            return true;
        }

        var xrWorldQ = new Quaternion[J.Length];
        var xrTracked = new bool[J.Length];
        for (int i = 0; i < J.Length; i++)
        {
            var j = xr.GetJoint(J[i]);
            if (j.TryGetPose(out Pose p)) { xrTracked[i] = true; xrWorldQ[i] = p.rotation; }
            else { xrTracked[i] = false; xrWorldQ[i] = Quaternion.identity; }
        }

        var xrLocalQ = new Quaternion[J.Length];
        for (int i = 0; i < J.Length; i++)
        {
            int pi = P[i];
            if (pi < 0) xrLocalQ[i] = Quaternion.identity;
            else
            {
                if (xrTracked[i] && xrTracked[pi]) xrLocalQ[i] = Quaternion.Inverse(xrWorldQ[pi]) * xrWorldQ[i];
                else if (xrTracked[i]) xrLocalQ[i] = xrWorldQ[i];
                else xrLocalQ[i] = Quaternion.identity;
            }
        }

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

        Vector3 up = Vector3.up;
        Vector3 dlrPlanarV = Vector3.ProjectOnPlane(dlr, up);
        planar = dlrPlanarV.magnitude;
        vert = Mathf.Abs(Vector3.Dot(dlr, up));

        if (dist3D < g.minHandDistanceM) return false;

        switch (g.relation)
        {
            case HandRelation.Any: return true;
            case HandRelation.SideBySide: return (vert <= g.verticalLevelToleranceM);
            case HandRelation.Stacked: return (vert >= g.minVerticalSeparationM) && (planar <= g.maxPlanarSeparationM);
            default: return true;
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

    void ForceGreenVisuals(BSLGesture g)
    {
        if (g.targetLeft) SwapMat(g.targetLeft, matchedMaterial);
        if (g.targetRight) SwapMat(g.targetRight, matchedMaterial);
        g.leftGreen = g.requireLeft ? true : g.leftGreen;
        g.rightGreen = g.requireRight ? true : g.rightGreen;
        g.leftT = g.rightT = Mathf.Max(g.leftT, g.rightT);
    }

    void MaintainLatchVisuals()
    {
        if (_latchedIndex < 0 || _latchedIndex >= gestures.Count) return;
        var g = gestures[_latchedIndex];
        if (g.targetLeft) SwapMat(g.targetLeft, matchedMaterial);
        if (g.targetRight) SwapMat(g.targetRight, matchedMaterial);
    }

    void ClearLatch()
    {
        if (_latchedIndex < 0 || _latchedIndex >= gestures.Count) { _latchedIndex = -1; return; }
        var g = gestures[_latchedIndex];

        if (g.targetLeft) SwapMat(g.targetLeft, unmatchedMaterial);
        if (g.targetRight) SwapMat(g.targetRight, unmatchedMaterial);

        g.leftT = g.rightT = 0f;
        g.leftGreen = g.rightGreen = false;

        _latchedIndex = -1;
        if (!suppressRecognizedTextOutput && recognizedText) recognizedText.text = noMatchText;

        // Cancel any pending auto-advance when latch is cleared
        _pendingAutoAdvance = false;
        _autoAdvanceAt = -1f;
    }

    public void SetSelectedGestureIndex(int index)
    {
        selectedGestureIndex = Mathf.Clamp(index, -1, gestures.Count - 1);
        if (_latchedIndex >= 0) ClearLatch();
    }

    public void SetRecognitionMode(RecognitionMode mode)
    {
        recognitionMode = mode;
        if (_latchedIndex >= 0) ClearLatch();
    }

    // Public carousel controls (manual buttons can call these)
    public void NextGesture()
    {
        if (gestures == null || gestures.Count == 0) return;
        int next = (selectedGestureIndex < 0) ? 0 : (selectedGestureIndex + 1) % gestures.Count;
        SetSelectedGestureIndex(next);
    }

    public void PrevGesture()
    {
        if (gestures == null || gestures.Count == 0) return;
        int prev = (selectedGestureIndex < 0) ? 0 : (selectedGestureIndex - 1 + gestures.Count) % gestures.Count;
        SetSelectedGestureIndex(prev);
    }

    void OnDisable()
    {
        foreach (var kv in _originalMat)
            if (kv.Key && kv.Key.skinnedMesh)
                kv.Key.skinnedMesh.sharedMaterial = kv.Value;

        foreach (var g in gestures)
        {
            g.leftT = g.rightT = 0f;
            g.leftGreen = g.rightGreen = false;
        }

        _latchedIndex = -1;
        if (!suppressRecognizedTextOutput && recognizedText) recognizedText.text = noMatchText;

        // Cancel any pending auto-advance when disabled
        _pendingAutoAdvance = false;
        _autoAdvanceAt = -1f;
    }
}
