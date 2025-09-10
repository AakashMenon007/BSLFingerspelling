using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(-200)] // run early so references are ready
public class XRHandDualGestureRecorder : MonoBehaviour
{
    [Header("Recording")]
    public KeyCode recordKey = KeyCode.R;
    [Min(0.1f)] public float maxCaptureSeconds = 2.0f;
    [Min(1f)] public float captureFPS = 30f;

    [Tooltip("Editor-only save path for recorded assets")]
    public string folderPathToSave = "Assets/CustomXRGestures";

    [Tooltip("Name to embed in saved assets")]
    public string gestureName = "BSL_Custom";

    [Header("Orientation / Conditions")]
    [Range(1f, 180f)] public float angleTolerance = 60f;

    [Header("Auto-Assign")]
    [Tooltip("If true, the component will auto-fill missing references in Editor and at runtime.")]
    public bool autoAssign = true;

    [Tooltip("Optional override. Auto-filled if left empty.")]
    [SerializeField] private Transform originTransform;

    [Tooltip("Optional override. Auto-filled if left empty (XROrigin.Camera or Camera.main).")]
    [SerializeField] private Transform headTransform;

    [Tooltip("Optional override. Auto-filled if left empty.")]
    [SerializeField] private XRHandSkeletonDriver leftSkeletonDriver;

    [Tooltip("Optional override. Auto-filled if left empty.")]
    [SerializeField] private XRHandSkeletonDriver rightSkeletonDriver;

    // Runtime
    private XRHandSubsystem _subsystem;
    private static readonly List<XRHandSubsystem> s_SubsystemsReuse = new();

    private List<DualHandGestureAsset.Frame> _frames;
    private float _accum;
    private float _frameInterval;
    private bool _capturing;

    private XRHand _leftHand;
    private XRHand _rightHand;

    private List<JointToTransformReference> _leftJoints;
    private List<JointToTransformReference> _rightJoints;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (autoAssign && !Application.isPlaying)
            TryAutoAssignReferences(logInfo: false);
    }
#endif

    private void Awake()
    {
        if (autoAssign)
            TryAutoAssignReferences(logInfo: true);

        _frameInterval = 1f / Mathf.Max(1f, captureFPS);
        _frames = new List<DualHandGestureAsset.Frame>(Mathf.CeilToInt(maxCaptureSeconds * captureFPS));
    }

    private void Update()
    {
        _subsystem = TryGetSubsystem();
        if (_subsystem == null) return;

        _leftHand = _subsystem.leftHand;
        _rightHand = _subsystem.rightHand;

        // Require both hands tracked for a dual-hand capture.
        if (!_leftHand.isTracked || !_rightHand.isTracked) return;

        // Cache joint arrays from drivers (once)
        if (_leftJoints == null && leftSkeletonDriver != null) _leftJoints = leftSkeletonDriver.jointTransformReferences;
        if (_rightJoints == null && rightSkeletonDriver != null) _rightJoints = rightSkeletonDriver.jointTransformReferences;

        if (Input.GetKeyDown(recordKey))
        {
            _capturing = true;
            _frames.Clear();
            _accum = 0f;
        }

        if (_capturing)
        {
            _accum += Time.deltaTime;
            while (_accum >= _frameInterval)
            {
                _accum -= _frameInterval;
                CaptureOneFrame();
            }

            if (Input.GetKeyUp(recordKey) || (_frames.Count >= maxCaptureSeconds * captureFPS))
            {
                _capturing = false;
                SaveGestureAsset();
            }
        }
    }

    // ---------- Auto-assign logic ----------
    private void TryAutoAssignReferences(bool logInfo)
    {
        // XROrigin + Head
        if (originTransform == null || headTransform == null)
        {
            var xrOrigin = FindFirstObjectByType<XROrigin>();
            if (xrOrigin != null)
            {
                if (originTransform == null)
                {
                    originTransform = xrOrigin.transform;
                    if (logInfo) Debug.Log("[XRHandDualGestureRecorder] Auto-assigned XROrigin as originTransform.");
                }
#if UNITY_XR_CORE_UTILS // XROrigin usually has Camera property; guard anyway
                if (headTransform == null && xrOrigin.Camera != null)
                {
                    headTransform = xrOrigin.Camera.transform;
                    if (logInfo) Debug.Log("[XRHandDualGestureRecorder] Auto-assigned XROrigin.Camera as headTransform.");
                }
#endif
            }
        }
        if (headTransform == null && Camera.main != null)
        {
            headTransform = Camera.main.transform;
            if (logInfo) Debug.Log("[XRHandDualGestureRecorder] Auto-assigned Camera.main as headTransform.");
        }

        // Skeleton drivers
        if (leftSkeletonDriver == null || rightSkeletonDriver == null)
        {
            var drivers = FindObjectsByType<XRHandSkeletonDriver>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (drivers != null && drivers.Length > 0)
            {
                // Heuristic: choose by name containing "Left"/"Right"
                XRHandSkeletonDriver left = null, right = null;

                foreach (var d in drivers)
                {
                    var n = d.gameObject.name.ToLowerInvariant();
                    if (left == null && (n.Contains("left") || n.Contains("_l") || n.EndsWith(" (l)")))
                        left = d;
                    else if (right == null && (n.Contains("right") || n.Contains("_r") || n.EndsWith(" (r)")))
                        right = d;
                }

                // If heuristics failed, just pick first two distinct drivers
                if ((left == null || right == null) && drivers.Length >= 2)
                {
                    if (left == null) left = drivers[0];
                    if (right == null) right = drivers.FirstOrDefault(d => d != left) ?? drivers[0];
                    if (logInfo) Debug.LogWarning("[XRHandDualGestureRecorder] Could not determine Left/Right by name; picked two drivers arbitrarily.");
                }

                if (leftSkeletonDriver == null && left != null)
                {
                    leftSkeletonDriver = left;
                    if (logInfo) Debug.Log($"[XRHandDualGestureRecorder] Auto-assigned leftSkeletonDriver: {left.gameObject.name}");
                }
                if (rightSkeletonDriver == null && right != null)
                {
                    rightSkeletonDriver = right;
                    if (logInfo) Debug.Log($"[XRHandDualGestureRecorder] Auto-assigned rightSkeletonDriver: {right.gameObject.name}");
                }
            }
        }

        // Final hints
        if (originTransform == null)
            Debug.LogWarning("[XRHandDualGestureRecorder] originTransform is still null. Assign your XROrigin manually.");
        if (headTransform == null)
            Debug.LogWarning("[XRHandDualGestureRecorder] headTransform is still null. Assign your XR rig camera or Main Camera.");
        if (leftSkeletonDriver == null || rightSkeletonDriver == null)
            Debug.LogWarning("[XRHandDualGestureRecorder] Could not auto-find both XRHandSkeletonDriver components. Assign them manually for best results.");
    }

    // ---------- Capture ----------
    private void CaptureOneFrame()
    {
        var frameTime = Time.time;

        var left = BuildHandSnapshot(_leftHand, _leftJoints, Handedness.Left);
        var right = BuildHandSnapshot(_rightHand, _rightJoints, Handedness.Right);

        var inter = new DualHandGestureAsset.InterHandFeatures
        {
            palmsDistance = Vector3.Distance(left.palmPose.worldPosition, right.palmPose.worldPosition),
            palmsOffsetWS = right.palmPose.worldPosition - left.palmPose.worldPosition,
            rightRelativeToLeft = Quaternion.Inverse(left.palmPose.worldRotation) * right.palmPose.worldRotation
        };

        _frames.Add(new DualHandGestureAsset.Frame
        {
            time = frameTime,
            left = left,
            right = right,
            interHand = inter
        });
    }

    private DualHandGestureAsset.HandSnapshot BuildHandSnapshot(XRHand hand, List<JointToTransformReference> joints, Handedness handedness)
    {
        var palmJoint = hand.GetJoint(XRHandJointID.Palm);
        var wristJoint = hand.GetJoint(XRHandJointID.Wrist);
        var thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);
        var indexTip = hand.GetJoint(XRHandJointID.IndexTip);
        var middleTip = hand.GetJoint(XRHandJointID.MiddleTip);
        var ringTip = hand.GetJoint(XRHandJointID.RingTip);
        var littleTip = hand.GetJoint(XRHandJointID.LittleTip);

        Transform palmTf = JointTF(joints, palmJoint);
        Transform wristTf = JointTF(joints, wristJoint);
        Transform thumbTf = JointTF(joints, thumbTip);
        Transform indexTf = JointTF(joints, indexTip);
        Transform middleTf = JointTF(joints, middleTip);
        Transform ringTf = JointTF(joints, ringTip);
        Transform littleTf = JointTF(joints, littleTip);

        // PALM pose (Transform → TryGetPose → fallback)
        Vector3 palmWorldPos; Quaternion palmWorldRot;
        if (palmTf != null)
        {
            palmWorldPos = palmTf.position;
            palmWorldRot = palmTf.rotation;
        }
        else if (palmJoint.TryGetPose(out Pose palmPose))
        {
            palmWorldPos = palmPose.position;
            palmWorldRot = palmPose.rotation;
        }
        else
        {
            palmWorldPos = Vector3.zero;
            palmWorldRot = Quaternion.identity;
        }

        // WRIST pose
        Vector3 wristWorldPos; Quaternion wristWorldRot;
        if (wristTf != null)
        {
            wristWorldPos = wristTf.position;
            wristWorldRot = wristTf.rotation;
        }
        else if (wristJoint.TryGetPose(out Pose wristPose))
        {
            wristWorldPos = wristPose.position;
            wristWorldRot = wristPose.rotation;
        }
        else
        {
            wristWorldPos = Vector3.zero;
            wristWorldRot = Quaternion.identity;
        }

        // Relative coords
        var palmHeadLocalPos = headTransform ? headTransform.InverseTransformPoint(palmWorldPos) : Vector3.zero;
        var palmHeadLocalRot = headTransform ? Quaternion.Inverse(headTransform.rotation) * palmWorldRot : Quaternion.identity;
        var palmOriginLocalPos = originTransform ? originTransform.InverseTransformPoint(palmWorldPos) : Vector3.zero;
        var palmOriginLocalRot = originTransform ? Quaternion.Inverse(originTransform.rotation) * palmWorldRot : Quaternion.identity;

        var wristHeadLocalPos = headTransform ? headTransform.InverseTransformPoint(wristWorldPos) : Vector3.zero;
        var wristHeadLocalRot = headTransform ? Quaternion.Inverse(headTransform.rotation) * wristWorldRot : Quaternion.identity;
        var wristOriginLocalPos = originTransform ? originTransform.InverseTransformPoint(wristWorldPos) : Vector3.zero;
        var wristOriginLocalRot = originTransform ? Quaternion.Inverse(originTransform.rotation) * wristWorldRot : Quaternion.identity;

        // Axes
        Vector3 palmForwardWS = palmTf ? (-palmTf.up) : (palmWorldRot * Vector3.forward);

        // Avg fingertip direction
        Vector3 avgTipsPos = Vector3.zero;
        int tipCount = 0;
        if (indexTf) { avgTipsPos += indexTf.position; tipCount++; }
        if (middleTf) { avgTipsPos += middleTf.position; tipCount++; }
        if (ringTf) { avgTipsPos += ringTf.position; tipCount++; }
        if (littleTf) { avgTipsPos += littleTf.position; tipCount++; }
        if (tipCount > 0) avgTipsPos /= Mathf.Max(1, tipCount);
        Vector3 fingersDirWS = (tipCount > 0) ? (avgTipsPos - palmWorldPos).normalized : palmForwardWS;

        // Thumb outward
        Vector3 thumbDirWS = thumbTf
            ? (handedness == Handedness.Left ? thumbTf.right : -thumbTf.right)
            : palmWorldRot * (handedness == Handedness.Left ? Vector3.right : Vector3.left);

        // Curls
        var curls = new DualHandGestureAsset.FingerCurls
        {
            thumb = CalcFullCurl(hand, XRHandFingerID.Thumb),
            index = CalcFullCurl(hand, XRHandFingerID.Index),
            middle = CalcFullCurl(hand, XRHandFingerID.Middle),
            ring = CalcFullCurl(hand, XRHandFingerID.Ring),
            little = CalcFullCurl(hand, XRHandFingerID.Little),
        };

        // Alignment (simple heuristic)
        var palmVsHead = GetAlignmentConditionBSL(palmTf, headTransform, XRHandAxis.PalmDirection, XRHandUserRelativeDirection.HandToHead);
        var palmVsOrigin = GetAlignmentConditionBSL(palmTf, originTransform, XRHandAxis.PalmDirection, XRHandUserRelativeDirection.OriginUp);
        var thumbVsHead = GetAlignmentConditionBSL(thumbTf ? thumbTf : palmTf, headTransform, XRHandAxis.ThumbExtendedDirection, XRHandUserRelativeDirection.HandToHead);
        var thumbVsOrigin = GetAlignmentConditionBSL(thumbTf ? thumbTf : palmTf, originTransform, XRHandAxis.ThumbExtendedDirection, XRHandUserRelativeDirection.OriginUp);

        // Fingers alignment with a temp point
        var tempGO = new GameObject("FingersAggregateTMP");
        var tempTF = tempGO.transform;
        tempTF.position = (tipCount > 0) ? avgTipsPos : palmWorldPos + fingersDirWS * 0.05f;
        tempTF.rotation = palmWorldRot;
        var fingersVsHead = GetAlignmentConditionBSL(tempTF, headTransform, XRHandAxis.FingersExtendedDirection, XRHandUserRelativeDirection.HandToHead);
        Destroy(tempGO);

        return new DualHandGestureAsset.HandSnapshot
        {
            handedness = handedness,

            palmPose = new DualHandGestureAsset.PoseSnapshot
            {
                worldPosition = palmWorldPos,
                worldRotation = palmWorldRot,
                headLocalPosition = palmHeadLocalPos,
                headLocalRotation = palmHeadLocalRot,
                originLocalPosition = palmOriginLocalPos,
                originLocalRotation = palmOriginLocalRot
            },

            wristPose = new DualHandGestureAsset.PoseSnapshot
            {
                worldPosition = wristWorldPos,
                worldRotation = wristWorldRot,
                headLocalPosition = wristHeadLocalPos,
                headLocalRotation = wristHeadLocalRot,
                originLocalPosition = wristOriginLocalPos,
                originLocalRotation = wristOriginLocalRot
            },

            palmForwardWS = palmForwardWS,
            fingersDirWS = fingersDirWS,
            thumbDirWS = thumbDirWS,

            curls = curls,

            palmVsHead = palmVsHead,
            palmVsOrigin = palmVsOrigin,
            thumbVsHead = thumbVsHead,
            thumbVsOrigin = thumbVsOrigin,
            fingersVsHead = fingersVsHead,

            palmToHeadDistance = headTransform ? Vector3.Distance(palmWorldPos, headTransform.position) : 0f
        };
    }

    private Transform JointTF(List<JointToTransformReference> refs, XRHandJoint joint)
    {
        if (refs == null) return null;
        int idx = joint.id.ToIndex();
        if (idx < 0 || idx >= refs.Count) return null;
        return refs[idx].jointTransform;
    }

    private float CalcFullCurl(XRHand hand, XRHandFingerID finger)
    {
        var shape = hand.CalculateFingerShape(finger, XRFingerShapeTypes.FullCurl);
        float v;
        return shape.TryGetFullCurl(out v) ? v : -1f;
    }

    private XRHandAlignmentCondition GetAlignmentConditionBSL(Transform jointTf, Transform refTf, XRHandAxis axis, XRHandUserRelativeDirection refDir)
    {
        if (jointTf == null || refTf == null) return XRHandAlignmentCondition.PerpendicularTo;

        Vector3 jointForward = axis switch
        {
            XRHandAxis.PalmDirection => -(jointTf.up),
            XRHandAxis.ThumbExtendedDirection => jointTf.right,
            XRHandAxis.FingersExtendedDirection => jointTf.forward,
            _ => jointTf.forward
        };

        Vector3 targetDir = (refDir == XRHandUserRelativeDirection.HandToHead)
            ? (refTf.position - jointTf.position).normalized
            : refTf.up;

        float angle = Vector3.Angle(jointForward, targetDir);
        float tol = Mathf.Clamp(angleTolerance * 0.5f, 1f, 90f);

        if (angle < tol) return XRHandAlignmentCondition.AlignsWith;
        if (Mathf.Abs(angle - 90f) < tol) return XRHandAlignmentCondition.PerpendicularTo;
        return XRHandAlignmentCondition.OppositeTo;
    }

    private void SaveGestureAsset()
    {
#if UNITY_EDITOR
        EnsureFolders();

        var asset = ScriptableObject.CreateInstance<DualHandGestureAsset>();
        asset.gestureName = gestureName;

        if (_frames.Count == 0) CaptureOneFrame();

        int mid = Mathf.Clamp(_frames.Count / 2, 0, Mathf.Max(0, _frames.Count - 1));
        asset.singleFrame = _frames[mid];

        if (_frames.Count > 1)
            asset.sequence = _frames.ToArray();

        var dir = Path.Combine(folderPathToSave, "Dual Hand Gestures");
        var path = Path.Combine(dir, $"{gestureName}_Dual_{DateTime.Now:yyyyMMdd_HHmmss}.asset").Replace("\\", "/");

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[XRHandDualGestureRecorder] Saved: {path}  (frames: {_frames.Count})");
#else
        Debug.LogWarning("Saving DualHandGestureAsset requires the Unity Editor.");
#endif
    }

    private void EnsureFolders()
    {
#if UNITY_EDITOR
        if (!Directory.Exists(folderPathToSave))
            Directory.CreateDirectory(folderPathToSave);

        var dualDir = Path.Combine(folderPathToSave, "Dual Hand Gestures");
        if (!Directory.Exists(dualDir))
            Directory.CreateDirectory(dualDir);
#endif
    }

    private static XRHandSubsystem TryGetSubsystem()
    {
        SubsystemManager.GetSubsystems(s_SubsystemsReuse);
        return s_SubsystemsReuse.Count > 0 ? s_SubsystemsReuse[0] : null;
    }
}
