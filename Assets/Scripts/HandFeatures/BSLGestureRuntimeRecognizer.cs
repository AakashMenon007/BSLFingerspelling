using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using TMPro;
using UnityEngine.XR.Hands.Gestures;

public class BSLGestureRuntimeRecognizer : MonoBehaviour
{
    [Serializable]
    public class GestureEntry
    {
        [Tooltip("The recorded DualHandGestureAsset (e.g., A_Dual_...).")]
        public DualHandGestureAsset asset;

        [Tooltip("Text to display when this gesture is recognized (e.g., 'A').")]
        public string outputText = "A";
    }

    [Header("Mappings")]
    [Tooltip("List of recorded gestures and the text to show for each.")]
    public List<GestureEntry> gestures = new List<GestureEntry>();

    [Header("Output")]
    [Tooltip("Assign your TextMeshPro (3D) component here.")]
    public TMP_Text outputTMP;

    [Header("References (auto if left empty)")]
    [SerializeField] private Transform originTransform;
    [SerializeField] private Transform headTransform;

    [Header("Tolerances")]
    [Tooltip("Max distance error (in meters) between current palm (head-local) and template.")]
    [Min(0.005f)] public float positionToleranceMeters = 0.12f; // a bit more forgiving
    [Tooltip("Max rotation error (in degrees) between current palm rotation and template (head-local).")]
    [Range(5f, 90f)] public float rotationToleranceDeg = 30f;
    [Tooltip("Max absolute finger curl difference per finger.")]
    [Range(0.05f, 1.0f)] public float curlTolerance = 0.30f;
    [Tooltip("Max difference in inter-hand palm distance (meters).")]
    [Min(0.01f)] public float interHandDistanceTolerance = 0.12f;

    [Header("Stability")]
    [Tooltip("Min seconds a candidate must be continuously matched before showing it.")]
    [Min(0f)] public float holdTimeRequired = 0.10f;
    [Tooltip("Cooldown before a new gesture can replace the current one (seconds).")]
    [Min(0f)] public float switchCooldown = 0.25f;

    [Header("Debug")]
    public bool debugLogs = true;
    [Tooltip("Throttle debug mismatch logs to avoid spam (seconds).")]
    [Min(0.05f)] public float debugLogInterval = 0.5f;

    // XR
    private XRHandSubsystem _subsystem;
    private static readonly List<XRHandSubsystem> _reuse = new();

    // Stability state
    private string _currentShown = "";
    private string _candidate = "";
    private float _candidateSince = 0f;
    private float _lastSwitchTime = -999f;

    // Caching
    private Transform _cachedOrigin;
    private Transform _cachedHead;

    // Debug throttle
    private float _lastDebugLogTime = -999f;

    private void Awake()
    {
        // Auto-wire origin/head if not set
        if (originTransform == null || headTransform == null)
        {
            var xrOrigin = FindAnyObjectByType<XROrigin>();
            if (xrOrigin)
            {
                if (originTransform == null) originTransform = xrOrigin.transform;
#if UNITY_XR_CORE_UTILS
                if (headTransform == null && xrOrigin.Camera != null) headTransform = xrOrigin.Camera.transform;
#endif
            }
            if (headTransform == null && Camera.main != null) headTransform = Camera.main.transform;
        }

        _cachedOrigin = originTransform;
        _cachedHead = headTransform;

        // Fallback: try to find any TMP in children to avoid a null reference
        if (outputTMP == null)
            outputTMP = GetComponentInChildren<TMP_Text>();

        if (outputTMP == null)
            Debug.LogWarning("[BSLGestureRuntimeRecognizer] Assign a TMP_Text (3D) object to display letters.");
        if (gestures == null || gestures.Count == 0)
            Debug.LogWarning("[BSLGestureRuntimeRecognizer] No gestures configured. Add your saved assets to the 'gestures' list.");
    }

    private void Update()
    {
        _subsystem = TryGetSubsystem();
        if (_subsystem == null) return;

        var left = _subsystem.leftHand;
        var right = _subsystem.rightHand;
        if (!left.isTracked || !right.isTracked) return;

        // Build current live frame (minimal subset needed for matching)
        var live = BuildLiveFrame(left, right);

        // Find best matching gesture within thresholds
        string bestLabel = null;
        float bestScore = float.MaxValue; // lower is better
        string bestReason = "";

        foreach (var entry in gestures)
        {
            if (entry.asset == null) continue;

            var tpl = entry.asset.singleFrame;
            if (!TemplateHasBothHands(tpl)) continue;

            if (MatchesWithinTolerance(live, tpl, out float score, out string reason))
            {
                if (score < bestScore)
                {
                    bestScore = score;
                    bestLabel = entry.outputText;
                    bestReason = reason;
                }
            }
            else
            {
                // Throttled debug for non-matching candidates
                if (debugLogs && Time.time - _lastDebugLogTime >= debugLogInterval)
                {
                    _lastDebugLogTime = Time.time;
                    Debug.Log($"[BSLGestureRuntimeRecognizer] No match: {entry.outputText} → {reason}");
                }
            }
        }

        // Handle stability (hold time & cooldown) to reduce flicker
        float now = Time.time;

        if (!string.IsNullOrEmpty(bestLabel))
        {
            if (_candidate != bestLabel)
            {
                _candidate = bestLabel;
                _candidateSince = now;
                if (debugLogs)
                    Debug.Log($"[BSLGestureRuntimeRecognizer] Candidate: {_candidate} (score {bestScore:F3})");
            }
            else
            {
                // Candidate stable long enough and cooldown passed?
                if ((_currentShown != _candidate) &&
                    (now - _candidateSince >= holdTimeRequired) &&
                    (now - _lastSwitchTime >= switchCooldown))
                {
                    _currentShown = _candidate;
                    _lastSwitchTime = now;
                    ShowText(_currentShown, bestScore);
                }
            }
        }
        else
        {
            _candidate = "";
        }
    }

    private void ShowText(string text, float score)
    {
        if (outputTMP != null)
            outputTMP.text = text;

        Debug.Log($"[BSLGestureRuntimeRecognizer] RECOGNIZED: \"{text}\" (score {score:F3})");
    }

    // -------- Matching --------

    private bool MatchesWithinTolerance(
        DualHandGestureAsset.Frame live,
        DualHandGestureAsset.Frame tpl,
        out float score,
        out string reason)
    {
        score = 0f;
        reason = "OK";

        // 1) Compare left/right palms in HEAD-LOCAL coordinates (positions)
        var posErrL = (live.left.palmPose.headLocalPosition - tpl.left.palmPose.headLocalPosition).magnitude;
        var posErrR = (live.right.palmPose.headLocalPosition - tpl.right.palmPose.headLocalPosition).magnitude;
        if (posErrL > positionToleranceMeters) { reason = $"Left pos err {posErrL:F3} > {positionToleranceMeters}"; return false; }
        if (posErrR > positionToleranceMeters) { reason = $"Right pos err {posErrR:F3} > {positionToleranceMeters}"; return false; }
        score += posErrL + posErrR;

        // 2) Compare palm rotations **HEAD-LOCAL** (robust to rig/world)
        var rotErrL = Quaternion.Angle(live.left.palmPose.headLocalRotation, tpl.left.palmPose.headLocalRotation);
        var rotErrR = Quaternion.Angle(live.right.palmPose.headLocalRotation, tpl.right.palmPose.headLocalRotation);
        if (rotErrL > rotationToleranceDeg) { reason = $"Left rot err {rotErrL:F1}° > {rotationToleranceDeg}°"; return false; }
        if (rotErrR > rotationToleranceDeg) { reason = $"Right rot err {rotErrR:F1}° > {rotationToleranceDeg}°"; return false; }
        score += (rotErrL + rotErrR) * 0.01f; // small influence

        // 3) Inter-hand distance in WORLD
        var liveDist = Vector3.Distance(live.left.palmPose.worldPosition, live.right.palmPose.worldPosition);
        var tplDist = Vector3.Distance(tpl.left.palmPose.worldPosition, tpl.right.palmPose.worldPosition);
        var distErr = Mathf.Abs(liveDist - tplDist);
        if (distErr > interHandDistanceTolerance) { reason = $"Inter-hand dist err {distErr:F3} > {interHandDistanceTolerance}"; return false; }
        score += distErr;

        // 4) Finger curls (ignore if -1)
        if (!CurlsClose(live.left.curls, tpl.left.curls, out string lc)) { reason = $"Left curls: {lc}"; return false; }
        if (!CurlsClose(live.right.curls, tpl.right.curls, out string rc)) { reason = $"Right curls: {rc}"; return false; }

        // (Optional) could add dot-product checks for fingers/thumb directions here

        return true;
    }

    private bool CurlsClose(DualHandGestureAsset.FingerCurls a, DualHandGestureAsset.FingerCurls b, out string detail)
    {
        detail = "OK";
        if (!CloseCurl(a.thumb, b.thumb, "thumb", ref detail)) return false;
        if (!CloseCurl(a.index, b.index, "index", ref detail)) return false;
        if (!CloseCurl(a.middle, b.middle, "middle", ref detail)) return false;
        if (!CloseCurl(a.ring, b.ring, "ring", ref detail)) return false;
        if (!CloseCurl(a.little, b.little, "little", ref detail)) return false;
        return true;
    }

    private bool CloseCurl(float live, float tpl, string name, ref string detail)
    {
        if (tpl < 0f || live < 0f) return true; // ignore unknowns
        float d = Mathf.Abs(live - tpl);
        if (d > curlTolerance) { detail = $"{name} Δ={d:F2} > {curlTolerance:F2}"; return false; }
        return true;
    }

    private bool TemplateHasBothHands(DualHandGestureAsset.Frame f)
    {
        // sanity: at least one palm pose not default
        bool leftOk = f.left.palmPose.worldRotation != Quaternion.identity || f.left.palmPose.worldPosition != Vector3.zero;
        bool rightOk = f.right.palmPose.worldRotation != Quaternion.identity || f.right.palmPose.worldPosition != Vector3.zero;
        return leftOk && rightOk;
    }

    // -------- Live frame capture (subset) --------

    private DualHandGestureAsset.Frame BuildLiveFrame(XRHand left, XRHand right)
    {
        var leftSnap = BuildHandSnapshot(left, Handedness.Left);
        var rightSnap = BuildHandSnapshot(right, Handedness.Right);

        var inter = new DualHandGestureAsset.InterHandFeatures
        {
            palmsDistance = Vector3.Distance(leftSnap.palmPose.worldPosition, rightSnap.palmPose.worldPosition),
            palmsOffsetWS = rightSnap.palmPose.worldPosition - leftSnap.palmPose.worldPosition,
            rightRelativeToLeft = Quaternion.Inverse(leftSnap.palmPose.worldRotation) * rightSnap.palmPose.worldRotation
        };

        return new DualHandGestureAsset.Frame
        {
            time = Time.time,
            left = leftSnap,
            right = rightSnap,
            interHand = inter
        };
    }

    private DualHandGestureAsset.HandSnapshot BuildHandSnapshot(XRHand hand, Handedness handedness)
    {
        var palmJoint = hand.GetJoint(XRHandJointID.Palm);
        var wristJoint = hand.GetJoint(XRHandJointID.Wrist);

        // PALM
        Vector3 palmPosWS; Quaternion palmRotWS;
        if (palmJoint.TryGetPose(out Pose palmPose))
        {
            palmPosWS = palmPose.position;
            palmRotWS = palmPose.rotation;
        }
        else
        {
            palmPosWS = Vector3.zero;
            palmRotWS = Quaternion.identity;
        }

        // WRIST
        Vector3 wristPosWS; Quaternion wristRotWS;
        if (wristJoint.TryGetPose(out Pose wristPose))
        {
            wristPosWS = wristPose.position;
            wristRotWS = wristPose.rotation;
        }
        else
        {
            wristPosWS = Vector3.zero;
            wristRotWS = Quaternion.identity;
        }

        // Head/Origin relative
        var head = _cachedHead ? _cachedHead : headTransform;
        var org = _cachedOrigin ? _cachedOrigin : originTransform;

        Vector3 palmHeadLocalPos = head ? head.InverseTransformPoint(palmPosWS) : Vector3.zero;
        Quaternion palmHeadLocalRot = head ? Quaternion.Inverse(head.rotation) * palmRotWS : Quaternion.identity;
        Vector3 palmOriginLocalPos = org ? org.InverseTransformPoint(palmPosWS) : Vector3.zero;
        Quaternion palmOriginLocalRot = org ? Quaternion.Inverse(org.rotation) * palmRotWS : Quaternion.identity;

        Vector3 wristHeadLocalPos = head ? head.InverseTransformPoint(wristPosWS) : Vector3.zero;
        Quaternion wristHeadLocalRot = head ? Quaternion.Inverse(head.rotation) * wristRotWS : Quaternion.identity;
        Vector3 wristOriginLocalPos = org ? org.InverseTransformPoint(wristPosWS) : Vector3.zero;
        Quaternion wristOriginLocalRot = org ? Quaternion.Inverse(org.rotation) * wristRotWS : Quaternion.identity;

        // Curls (quick)
        var curls = new DualHandGestureAsset.FingerCurls
        {
            thumb = CurlOf(hand, XRHandFingerID.Thumb),
            index = CurlOf(hand, XRHandFingerID.Index),
            middle = CurlOf(hand, XRHandFingerID.Middle),
            ring = CurlOf(hand, XRHandFingerID.Ring),
            little = CurlOf(hand, XRHandFingerID.Little),
        };

        return new DualHandGestureAsset.HandSnapshot
        {
            handedness = handedness,

            palmPose = new DualHandGestureAsset.PoseSnapshot
            {
                worldPosition = palmPosWS,
                worldRotation = palmRotWS,
                headLocalPosition = palmHeadLocalPos,
                headLocalRotation = palmHeadLocalRot,
                originLocalPosition = palmOriginLocalPos,
                originLocalRotation = palmOriginLocalRot
            },

            wristPose = new DualHandGestureAsset.PoseSnapshot
            {
                worldPosition = wristPosWS,
                worldRotation = wristRotWS,
                headLocalPosition = wristHeadLocalPos,
                headLocalRotation = wristHeadLocalRot,
                originLocalPosition = wristOriginLocalPos,
                originLocalRotation = wristOriginLocalRot
            },

            // Not used by matcher currently (kept for completeness)
            palmForwardWS = -(palmRotWS * Vector3.up),
            fingersDirWS = (palmRotWS * Vector3.forward),
            thumbDirWS = (palmRotWS * (handedness == Handedness.Left ? Vector3.right : Vector3.left)),

            curls = curls,

            palmVsHead = 0,
            palmVsOrigin = 0,
            thumbVsHead = 0,
            thumbVsOrigin = 0,
            fingersVsHead = 0,

            palmToHeadDistance = head ? Vector3.Distance(palmPosWS, head.position) : 0f
        };
    }

    private float CurlOf(XRHand hand, XRHandFingerID finger)
    {
        var shape = hand.CalculateFingerShape(finger, XRFingerShapeTypes.FullCurl);
        float v;
        return shape.TryGetFullCurl(out v) ? v : -1f;
    }

    private XRHandSubsystem TryGetSubsystem()
    {
        SubsystemManager.GetSubsystems(_reuse);
        return _reuse.Count > 0 ? _reuse[0] : null;
    }
}
