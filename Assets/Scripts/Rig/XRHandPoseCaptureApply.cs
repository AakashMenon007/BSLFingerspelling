using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class XRHandPoseKeyedBoth : MonoBehaviour
{
    // Joint order shared everywhere
    static readonly XRHandJointID[] k_Joints = new XRHandJointID[]
    {
        XRHandJointID.Wrist, XRHandJointID.Palm,
        XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip,
        XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip,
        XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip,
        XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip,
        XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip,
    };

    static readonly int[] k_Parent = new int[]
    {
        -1, 0,  1,2,3,4,  1,6,7,8,9,  1,11,12,13,14,  1,16,17,18,19,  1,21,22,23,24
    };

    public enum SpaceMode { World, OriginLocal }

    [Header("Capture (with timer)")]
    [Tooltip("Seconds to wait before taking the snapshot (for BOTH hands at once).")]
    public float countdownSeconds = 3f;

    [Header("Tracking Reliability")]
    [Tooltip("Require both wrists to be tracked; otherwise do NOT save.")]
    public bool requireBothHandsTracked = true;
    [Tooltip("After the countdown ends, wait up to this many seconds for both wrists to become tracked.")]
    public float waitSecondsForTracking = 2f;

    [Header("Keys")]
    [Tooltip("Start timed capture for BOTH hands; saves L & R JSON with the SAME timestamp.")]
    public KeyCode captureKey = KeyCode.C;
    [Tooltip("Load the newest L+R files that share the SAME timestamp (a true pair).")]
    public KeyCode loadKey = KeyCode.L;

    [Header("Space")]
    public SpaceMode spaceMode = SpaceMode.OriginLocal;
    [Tooltip("Assign your XR Origin when using OriginLocal.")]
    public Transform origin;

    [Header("Ghost rigs used when Loading")]
    public XRHandGhostRig leftHandRigPrefab;
    public XRHandGhostRig rightHandRigPrefab;
    [Tooltip("Optional translucent material applied to both ghost rigs.")]
    public Material ghostMaterial;

    [Header("Orientation Fixes")]
    [Tooltip("Use PALM rotation (instead of wrist) for the LEFT hand when applying the pose.")]
    public bool usePalmRotationForLeft = true;     // common fix for left tilt
    [Tooltip("Extra Euler rotation (degrees) added to LEFT wrist after apply.")]
    public Vector3 leftWristRotationOffsetEuler = Vector3.zero;
    [Tooltip("Use PALM rotation (instead of wrist) for the RIGHT hand when applying the pose.")]
    public bool usePalmRotationForRight = false;
    [Tooltip("Extra Euler rotation (degrees) added to RIGHT wrist after apply.")]
    public Vector3 rightWristRotationOffsetEuler = Vector3.zero;

    [Header("Loader Behavior")]
    [Tooltip("If ON, only load pairs where BOTH L & R exist for the same timestamp. If no pair exists, nothing loads.")]
    public bool loadStrictPair = true;

    [Header("UI")]
    public bool showCountdownUI = true;

    // XR
    XRHandSubsystem _handSub;

    // Countdown state
    bool _countingDown;
    float _countdownT;

    // Spawned rigs (for Load)
    XRHandGhostRig _spawnedLeft;
    XRHandGhostRig _spawnedRight;

    // Regex to pull the shared timestamp part out of file names: pose_L_20250910_14-22-35_123.json
    static readonly Regex k_StampRegex = new Regex(@"^pose_[LR]_(.+)\.json$", RegexOptions.Compiled);

    void Awake()
    {
        var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
        _handSub = loader?.GetLoadedSubsystem<XRHandSubsystem>();
        if (_handSub == null)
            Debug.LogError("[XRHandPoseKeyedBoth] XRHandSubsystem not available. Check XR Hands setup.");
    }

    void Update()
    {
        // Start timed capture for BOTH hands
        if (Input.GetKeyDown(captureKey) && !_countingDown)
        {
            if (_handSub == null) return;
            _countingDown = true;
            _countdownT = Mathf.Max(0f, countdownSeconds);
            Debug.Log($"[XRHandPoseKeyedBoth] Capturing BOTH in {Mathf.CeilToInt(_countdownT)}s… hold your pose.");
        }

        // Countdown tick
        if (_countingDown)
        {
            _countdownT -= Time.deltaTime;
            if (_countdownT <= 0f)
            {
                _countingDown = false;
                StartCoroutine(CaptureAndSaveBothWithWait());
            }
        }

        // Load newest matched pair (L+R with SAME timestamp)
        if (Input.GetKeyDown(loadKey))
        {
            LoadLatestMatchedPair();
        }
    }

    // ===== Capture & Save (BOTH) with a short wait for tracking =====
    System.Collections.IEnumerator CaptureAndSaveBothWithWait()
    {
        float deadline = Time.time + Mathf.Max(0f, waitSecondsForTracking);

        XRHandPoseData leftPose = null;
        XRHandPoseData rightPose = null;

        // Try immediately, and if needed, wait a tiny bit for both wrists to be tracked
        while (Time.time <= deadline)
        {
            var lWrist = SamplePoseWorld(_handSub.leftHand, XRHandJointID.Wrist);
            var rWrist = SamplePoseWorld(_handSub.rightHand, XRHandJointID.Wrist);

            bool haveL = lWrist.tracked;
            bool haveR = rWrist.tracked;

            if ((!requireBothHandsTracked && (haveL || haveR)) || (haveL && haveR))
            {
                // Build the poses now (same frame)
                var lPalm = SamplePoseWorld(_handSub.leftHand, XRHandJointID.Palm);
                var rPalm = SamplePoseWorld(_handSub.rightHand, XRHandJointID.Palm);

                if (haveL)
                {
                    leftPose = ScriptableObject.CreateInstance<XRHandPoseData>();
                    leftPose.isLeftHand = true;
                    leftPose.spaceMode = (XRHandPoseData.SpaceMode)spaceMode;
                    leftPose.wrist = ToSpace(lWrist);
                    leftPose.palm = ToSpace(lPalm);
                    leftPose.joints = SampleLocalRotations(_handSub.leftHand);
                }

                if (haveR)
                {
                    rightPose = ScriptableObject.CreateInstance<XRHandPoseData>();
                    rightPose.isLeftHand = false;
                    rightPose.spaceMode = (XRHandPoseData.SpaceMode)spaceMode;
                    rightPose.wrist = ToSpace(rWrist);
                    rightPose.palm = ToSpace(rPalm);
                    rightPose.joints = SampleLocalRotations(_handSub.rightHand);
                }
                break;
            }

            yield return null; // wait a frame and try again
        }

        if (requireBothHandsTracked && (leftPose == null || rightPose == null))
        {
            Debug.LogWarning("[XRHandPoseKeyedBoth] Capture aborted: both wrists weren’t tracked within the wait window.");
            yield break;
        }

        if (leftPose == null && rightPose == null)
        {
            Debug.LogWarning("[XRHandPoseKeyedBoth] Nothing to save (no wrists tracked).");
            yield break;
        }

        // Use ONE shared timestamp for both files (millisecond precision)
        string stamp = DateTime.Now.ToString("yyyyMMdd_HH-mm-ss_fff");
        string dir = Application.persistentDataPath + "/HandPoses";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        if (leftPose != null)
        {
            string lp = Path.Combine(dir, $"pose_L_{stamp}.json");
            File.WriteAllText(lp, JsonUtility.ToJson(leftPose, false));
            Debug.Log($"[XRHandPoseKeyedBoth] Saved LEFT pose:\n{lp}");
        }

        if (rightPose != null)
        {
            string rp = Path.Combine(dir, $"pose_R_{stamp}.json");
            File.WriteAllText(rp, JsonUtility.ToJson(rightPose, false));
            Debug.Log($"[XRHandPoseKeyedBoth] Saved RIGHT pose:\n{rp}");
        }
    }

    // ===== Load newest matched pair (L+R with SAME timestamp) =====
    void LoadLatestMatchedPair()
    {
        string dir = Application.persistentDataPath + "/HandPoses";
        if (!Directory.Exists(dir))
        {
            Debug.LogWarning("[XRHandPoseKeyedBoth] No pose folder to load from.");
            return;
        }

        // Collect files
        var files = Directory.GetFiles(dir, "pose_*.json");
        if (files.Length == 0)
        {
            Debug.LogWarning("[XRHandPoseKeyedBoth] No pose files found.");
            return;
        }

        // Build stamp maps for L and R
        var leftByStamp = new Dictionary<string, string>();
        var rightByStamp = new Dictionary<string, string>();

        foreach (var path in files)
        {
            string file = Path.GetFileName(path);
            var m = k_StampRegex.Match(file);
            if (!m.Success) continue;
            string stamp = m.Groups[1].Value;

            if (file.StartsWith("pose_L_")) leftByStamp[stamp] = path;
            else if (file.StartsWith("pose_R_")) rightByStamp[stamp] = path;
        }

        // Find newest common stamp (lexicographic works for our timestamp format)
        var commonStamps = leftByStamp.Keys.Intersect(rightByStamp.Keys).ToList();
        if (commonStamps.Count == 0)
        {
            if (loadStrictPair)
            {
                Debug.LogWarning("[XRHandPoseKeyedBoth] No matched L+R pair found. Nothing loaded.");
                return;
            }
            else
            {
                // Fallback: load newest individual files (may mismatch)
                string newestL = leftByStamp.OrderBy(kv => kv.Key).LastOrDefault().Value;
                string newestR = rightByStamp.OrderBy(kv => kv.Key).LastOrDefault().Value;
                if (newestL == null && newestR == null)
                {
                    Debug.LogWarning("[XRHandPoseKeyedBoth] No loadable files.");
                    return;
                }
                if (newestL != null) LoadOne(newestL, isLeft: true);
                if (newestR != null) LoadOne(newestR, isLeft: false);
                Debug.Log("[XRHandPoseKeyedBoth] Loaded newest (unpaired) files (fallback mode).");
                return;
            }
        }

        string newestStamp = commonStamps.OrderBy(s => s).Last();
        string leftPath = leftByStamp[newestStamp];
        string rightPath = rightByStamp[newestStamp];

        LoadOne(leftPath, isLeft: true);
        LoadOne(rightPath, isLeft: false);

        Debug.Log($"[XRHandPoseKeyedBoth] Loaded matched pair @ {newestStamp}.");
    }

    void LoadOne(string path, bool isLeft)
    {
        var pose = ScriptableObject.CreateInstance<XRHandPoseData>();
        JsonUtility.FromJsonOverwrite(File.ReadAllText(path), pose);

        if (isLeft)
        {
            EnsureLeftRig();
            ApplyPoseWithOptions(pose, _spawnedLeft, usePalmRotationForLeft, leftWristRotationOffsetEuler);
        }
        else
        {
            EnsureRightRig();
            ApplyPoseWithOptions(pose, _spawnedRight, usePalmRotationForRight, rightWristRotationOffsetEuler);
        }
    }

    void ApplyPoseWithOptions(XRHandPoseData pose, XRHandGhostRig rig, bool usePalmRotation, Vector3 extraEuler)
    {
        if (pose == null || rig == null) return;

        // Apply normally (uses wrist pose from pose data)
        pose.ApplyToRig(rig, origin, useJointOffsets: false, jointOffsetsOrNull: null);

        // Optionally override rotation with PALM rotation (often fixes left tilt)
        if (usePalmRotation && pose.palm != null && pose.palm.tracked && rig.wristRoot != null)
        {
            Quaternion palmQw = (spaceMode == SpaceMode.World || origin == null) ? pose.palm.q : origin.rotation * pose.palm.q;
            rig.wristRoot.rotation = palmQw;
        }

        // Extra Euler correction if needed
        if (rig.wristRoot != null && extraEuler != Vector3.zero)
        {
            rig.wristRoot.rotation = rig.wristRoot.rotation * Quaternion.Euler(extraEuler);
        }
    }

    void EnsureLeftRig()
    {
        if (_spawnedLeft == null && leftHandRigPrefab != null)
        {
            _spawnedLeft = Instantiate(leftHandRigPrefab, transform);
            if (ghostMaterial && _spawnedLeft.skinnedMesh) _spawnedLeft.skinnedMesh.sharedMaterial = ghostMaterial;
            _spawnedLeft.transform.localScale = Vector3.one; // avoid negative/mirrored scales
        }
    }

    void EnsureRightRig()
    {
        if (_spawnedRight == null && rightHandRigPrefab != null)
        {
            _spawnedRight = Instantiate(rightHandRigPrefab, transform);
            if (ghostMaterial && _spawnedRight.skinnedMesh) _spawnedRight.skinnedMesh.sharedMaterial = ghostMaterial;
            _spawnedRight.transform.localScale = Vector3.one;
        }
    }

    // ===== Helpers =====
    XRHandPoseData.PoseS SamplePoseWorld(XRHand xr, XRHandJointID id)
    {
        var ps = new XRHandPoseData.PoseS { tracked = false, p = Vector3.zero, q = Quaternion.identity };
        var j = xr.GetJoint(id);
        if (j.TryGetPose(out Pose pose)) { ps.tracked = true; ps.p = pose.position; ps.q = pose.rotation; }
        return ps;
    }

    XRHandPoseData.PoseS ToSpace(XRHandPoseData.PoseS world)
    {
        if (spaceMode == SpaceMode.World || origin == null || !world.tracked) return world;
        return new XRHandPoseData.PoseS
        {
            tracked = true,
            p = origin.InverseTransformPoint(world.p),
            q = Quaternion.Inverse(origin.rotation) * world.q
        };
    }

    XRHandPoseData.JointRotS[] SampleLocalRotations(XRHand xr)
    {
        int n = k_Joints.Length;
        var worldQ = new Quaternion[n];
        var tracked = new bool[n];

        for (int i = 0; i < n; i++)
        {
            var j = xr.GetJoint(k_Joints[i]);
            if (j.TryGetPose(out Pose p)) { tracked[i] = true; worldQ[i] = p.rotation; }
            else { tracked[i] = false; worldQ[i] = Quaternion.identity; }
        }

        var arr = new XRHandPoseData.JointRotS[n];
        for (int i = 0; i < n; i++)
        {
            int pi = k_Parent[i];
            Quaternion local;
            if (pi < 0) local = Quaternion.identity; // wrist local neutral
            else
            {
                if (tracked[i] && tracked[pi]) local = Quaternion.Inverse(worldQ[pi]) * worldQ[i];
                else if (tracked[i]) local = worldQ[i];
                else local = Quaternion.identity;
            }
            arr[i] = new XRHandPoseData.JointRotS { id = (int)k_Joints[i], tracked = tracked[i], localQ = local };
        }
        return arr;
    }

    // Simple on-screen countdown
    void OnGUI()
    {
        if (!_countingDown || !showCountdownUI) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.UpperCenter };
        string msg = $"Capturing BOTH hands in {Mathf.CeilToInt(Mathf.Max(0f, _countdownT))}… Hold your pose";
        GUI.Label(new Rect(0, 10, Screen.width, 30), msg, style);
    }
}
