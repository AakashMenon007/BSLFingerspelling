using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class XRHandRecorder : MonoBehaviour
{
    [Header("Controls")]
    public KeyCode recordKey = KeyCode.R;
    public KeyCode replayKey = KeyCode.P;

    [Header("Timing")]
    public float countdownSeconds = 3f;
    public float recordSeconds = 3f;

    [Header("Ghost visual")]
    [Tooltip("Transparent material swapped onto the hand meshes (preview & replay).")]
    public Material ghostMaterial;

    [Header("Rigs (prefabs or scene objects)")]
    public XRHandGhostRig leftHandRigPrefab;
    public XRHandGhostRig rightHandRigPrefab;

    public enum SpaceMode { World, OriginLocal }

    [Header("Space / Reference")]
    [Tooltip("OriginLocal recommended. Assign your XR Origin below.")]
    public SpaceMode spaceMode = SpaceMode.OriginLocal;
    [Tooltip("XR Origin transform used when SpaceMode=OriginLocal.")]
    public Transform origin;

    [Header("Articulation Offsets")]
    [Tooltip("If true, capture per-joint offsets so curls match your rig bind pose.")]
    public bool useJointOffsets = true;

    [Header("Playback")]
    public bool playOnStart = false;
    public bool loopPlayback = false;
    public TextAsset recordingAsset;
    public string streamingAssetsFileName = "";
    public bool fallbackToLatestInPersisted = true;

    [Header("BSL Authoring (Editor-only save to Assets)")]
    public bool saveBSLToAssetsOnStop = true;
    public string assetsFolder = "Assets/BSLGestures";
    public string currentGestureName = "A";

    [Header("Preview (overlay live while recording)")]
    [Tooltip("Exact XR-driven overlay so ghost fits your real hands during preview/recording.")]
    public bool exactOverlayDuringPreview = true;
    [Tooltip("Also show overlay when idle (helpful for tuning).")]
    public bool livePreviewWhenIdle = false;

    [Header("Exact Overlay (per hand, world space nudges)")]
    [Tooltip("Small world-space nudge for LEFT wrist (only used in Exact Overlay).")]
    public Vector3 leftWristWorldPosNudge = Vector3.zero;
    [Tooltip("Small world-space nudge for RIGHT wrist (only used in Exact Overlay).")]
    public Vector3 rightWristWorldPosNudge = Vector3.zero;
    [Tooltip("Extra world-space rotation for LEFT wrist (only used in Exact Overlay).")]
    public Vector3 leftWristWorldEulerNudge = Vector3.zero;
    [Tooltip("Extra world-space rotation for RIGHT wrist (only used in Exact Overlay).")]
    public Vector3 rightWristWorldEulerNudge = Vector3.zero;

    [Header("Root Tweaks (replay only)")]
    [Tooltip("World rotation added to the shared ROOT during replay only.")]
    public Vector3 rootRotationOffsetEuler = Vector3.zero;
    [Tooltip("World position added to the shared ROOT during replay only.")]
    public Vector3 rootPositionOffsetWorld = Vector3.zero;

    // -------- Right Rig Mirroring Fix --------
    public enum MirrorAxis { None, X, Y, Z }

    [Header("Right Rig Mirroring")]
    [Tooltip("Choose which axis is mirrored on the RIGHT rig (common: X).")]
    public MirrorAxis rightMirrorAxis = MirrorAxis.None;
    [Tooltip("If axis is None, auto-detect by negative lossy scale (assumes X).")]
    public bool autoDetectRightMirrorByScale = true;

    [Header("Realtime Calibrate (optional)")]
    public bool realtimeCalibrate = false;
    public bool realtimeIncludeRotation = true;
    public float realtimeSmoothingSeconds = 0.2f;

    [Header("Auto-Fit (Play Mode)")]
    public bool autoFitRightZOnStart = true;
    public float autoFitDelaySeconds = 0.25f;

    // runtime rigs
    XRHandGhostRig _leftRig, _rightRig;

    // XR
    XRHandSubsystem _handSub;

    // state
    bool _countingDown;
    bool _recording;
    float _countdownT;
    float _recordStartT;

    bool _replaying;
    float _playStartT;
    int _nextFrameIdx;
    float _recordedDuration;

    Recording _activeRecording;

    // Stable joint order and parents
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
        -1,  0,  1,2,3,4,  1,6,7,8,9,  1,11,12,13,14,  1,16,17,18,19,  1,21,22,23,24
    };

    // --------- Serializable data ---------
    [Serializable] public class PoseS { public bool tracked; public Vector3 p; public Quaternion q; }
    [Serializable] public class JointRotS { public int id; public bool tracked; public Quaternion localQ; }

    [Serializable]
    public class HandFrameS
    {
        public PoseS wristWorld;
        public PoseS palmWorld;
        public PoseS wristLocalToRoot;
    }
    [Serializable]
    public class FrameS
    {
        public float t;
        public PoseS root;
        public HandFrameS left, right;
        public JointRotS[] leftJoints;
        public JointRotS[] rightJoints;
    }
    [Serializable]
    public class Recording
    {
        public string version = "2.0";
        public string space = "OriginLocal";
        public float duration;
        public List<FrameS> frames = new List<FrameS>();
    }

    [Serializable]
    class PerJointOffsets
    {
        public Quaternion[] jointLocalOffsets = new Quaternion[k_Joints.Length];
        public PerJointOffsets() { for (int i = 0; i < jointLocalOffsets.Length; i++) jointLocalOffsets[i] = Quaternion.identity; }
    }
    PerJointOffsets _leftJointOff = new PerJointOffsets();
    PerJointOffsets _rightJointOff = new PerJointOffsets();

    // ================== Unity ==================
    void Awake()
    {
        _handSub = TryGetHandSubsystem();
    }

    void Start()
    {
        if (origin == null)
        {
            var maybe = GameObject.Find("XR Origin") ?? GameObject.Find("XROrigin");
            if (maybe) origin = maybe.transform;
            var byTag = GameObject.FindWithTag("XROrigin");
            if (!origin && byTag) origin = byTag.transform;
        }

        if (playOnStart)
        {
            if (EnsureRecordingLoaded()) BeginReplay();
            else Debug.LogWarning("[XRHandRecorder] playOnStart is true, but no recording could be loaded.");
        }

        if (autoFitRightZOnStart)
            StartCoroutine(AutoFitRightZOnce());
    }

    void Update()
    {
        if (Input.GetKeyDown(recordKey) && !_recording && !_countingDown) StartTimedRecordAndSave();

        if (Input.GetKeyDown(replayKey) && !_replaying)
        {
            if (!EnsureRecordingLoaded()) { Debug.LogWarning("[XRHandRecorder] No recording to play."); return; }
            BeginReplay();
        }

        if (_countingDown)
        {
            _countdownT -= Time.deltaTime;
            if (_countdownT <= 0f) { _countingDown = false; BeginRecording(); }
        }

        if (_recording)
        {
            float t = Time.time - _recordStartT;
            SampleFrame(t);
            if (t >= recordSeconds) StopRecordingAndSave();
        }

        // Live preview: exact XR-driven overlay → perfect fit
        bool wantPreview = exactOverlayDuringPreview && (_countingDown || _recording);
        if (!wantPreview && livePreviewWhenIdle) wantPreview = exactOverlayDuringPreview;
        if (wantPreview && _handSub != null) ApplyExactOverlayFromXR();

        if (realtimeCalibrate && _handSub != null) RealtimeCalibrateStep(Time.deltaTime);

        if (_replaying && _activeRecording != null && _activeRecording.frames.Count > 1)
            StepReplay();
    }

    // ================== Public ==================
    public void StartTimedRecordAndSave()
    {
        if (_countingDown || _recording) return;
        if (_handSub == null) { Debug.LogError("[XRHandRecorder] XRHandSubsystem not available."); return; }
        _activeRecording = new Recording { space = spaceMode.ToString() };
        _countingDown = true;
        _countdownT = Mathf.Max(0f, countdownSeconds);
        Debug.Log($"[XRHandRecorder] Countdown {Mathf.CeilToInt(_countdownT)}s...");
    }

    public void PlayAsset(TextAsset asset, bool loop = false)
    {
        loopPlayback = loop;
        LoadFromTextAsset(asset);
        if (_activeRecording != null) BeginReplay();
    }

    public void LoadFromTextAsset(TextAsset asset)
    {
        recordingAsset = asset;
        _activeRecording = null;
        if (asset == null || string.IsNullOrEmpty(asset.text)) return;
        try
        {
            _activeRecording = JsonUtility.FromJson<Recording>(asset.text);
            if (_activeRecording != null && Enum.TryParse(_activeRecording.space, out SpaceMode saved)) spaceMode = saved;
        }
        catch (Exception e)
        {
            Debug.LogError("[XRHandRecorder] Failed to parse TextAsset JSON: " + e.Message);
        }
    }

    // ================== Recording ==================
    void BeginRecording()
    {
        _activeRecording.frames.Clear();
        _recordStartT = Time.time;
        _recordedDuration = recordSeconds;
        _recording = true;
        Debug.Log("[XRHandRecorder] Recording started.");
    }

    void StopRecordingAndSave()
    {
        _recording = false;
        _activeRecording.duration = _recordedDuration;

        var persistentPath = SaveToPersistent(_activeRecording);
        Debug.Log($"[XRHandRecorder] Recording saved to persistent:\n{persistentPath}");

#if UNITY_EDITOR
        if (saveBSLToAssetsOnStop)
        {
            string assetsPath = SaveIntoAssets(_activeRecording, currentGestureName);
            Debug.Log($"[XRHandRecorder] Recording also saved to Assets:\n{assetsPath}");
        }
#endif
    }

    void SampleFrame(float t)
    {
        var f = BuildCurrentFrame(t);
        _activeRecording.frames.Add(f);
    }

    FrameS BuildCurrentFrame(float t = 0f)
    {
        var f = new FrameS { t = t, left = new HandFrameS(), right = new HandFrameS() };

        var lw = SamplePoseWorld(_handSub.leftHand, XRHandJointID.Wrist);
        var lp = SamplePoseWorld(_handSub.leftHand, XRHandJointID.Palm);
        var rw = SamplePoseWorld(_handSub.rightHand, XRHandJointID.Wrist);
        var rp = SamplePoseWorld(_handSub.rightHand, XRHandJointID.Palm);

        var rootWorld = ComputeBimanualRoot(lp, rp, lw, rw);

        f.root = ToSpace(rootWorld);

        f.left.wristWorld = ToSpace(lw);
        f.left.palmWorld = ToSpace(lp);
        f.right.wristWorld = ToSpace(rw);
        f.right.palmWorld = ToSpace(rp);

        f.left.wristLocalToRoot = WorldToLocalPose(lw, rootWorld);
        f.right.wristLocalToRoot = WorldToLocalPose(rw, rootWorld);

        f.leftJoints = SampleLocalRotations(_handSub.leftHand);
        f.rightJoints = SampleLocalRotations(_handSub.rightHand);

        return f;
    }

    // ============ EXACT OVERLAY (XR-driven, no root math) ============
    void ApplyExactOverlayFromXR()
    {
        EnsureRigs();
        AutoDetectRightMirrorIfNeeded();

        // LEFT wrist → XR pose (+ tiny world nudge)
        var lw = SamplePoseWorld(_handSub.leftHand, XRHandJointID.Wrist);
        if (lw.tracked && _leftRig && _leftRig.wristRoot)
        {
            _leftRig.wristRoot.position = lw.p + leftWristWorldPosNudge;
            _leftRig.wristRoot.rotation = lw.q * Quaternion.Euler(leftWristWorldEulerNudge);
        }

        // RIGHT wrist → XR pose (+ tiny world nudge)
        var rw = SamplePoseWorld(_handSub.rightHand, XRHandJointID.Wrist);
        if (rw.tracked && _rightRig && _rightRig.wristRoot)
        {
            _rightRig.wristRoot.position = rw.p + rightWristWorldPosNudge;
            _rightRig.wristRoot.rotation = rw.q * Quaternion.Euler(rightWristWorldEulerNudge);
        }

        // Drive articulation directly from XR locals (skip wrist bone)
        var lLoc = SampleLocalRotations(_handSub.leftHand);
        var rLoc = SampleLocalRotations(_handSub.rightHand);

        ApplyLocalRotSet(_leftRig, lLoc, _leftJointOff, false);
        ApplyLocalRotSet(_rightRig, rLoc, _rightJointOff, true);
    }

    // ================== Replay ==================
    void BeginReplay()
    {
        EnsureRigs();
        AutoDetectRightMirrorIfNeeded();

        _replaying = true;
        _playStartT = Time.time;
        _nextFrameIdx = 1;
        _recordedDuration = Mathf.Max(_activeRecording.duration, _activeRecording.frames[_activeRecording.frames.Count - 1].t);
        Debug.Log("[XRHandRecorder] Replay started.");
    }

    void StepReplay()
    {
        float t = Time.time - _playStartT;

        if (t >= _recordedDuration)
        {
            ApplyFrame(_activeRecording.frames[_activeRecording.frames.Count - 1]);

            if (loopPlayback)
            {
                _playStartT = Time.time;
                _nextFrameIdx = 1;
                return;
            }

            _replaying = false;
            Debug.Log("[XRHandRecorder] Replay finished.");
            return;
        }

        while (_nextFrameIdx < _activeRecording.frames.Count && _activeRecording.frames[_nextFrameIdx].t < t)
            _nextFrameIdx++;

        int i1 = Mathf.Clamp(_nextFrameIdx, 1, _activeRecording.frames.Count - 1);
        int i0 = i1 - 1;

        var f0 = _activeRecording.frames[i0];
        var f1 = _activeRecording.frames[i1];
        float a = Mathf.InverseLerp(f0.t, f1.t, t);

        ApplyInterpolatedFrame(f0, f1, a);
    }

    void ApplyInterpolatedFrame(FrameS a, FrameS b, float t)
    {
        Vector3 rootP_local = Vector3.Lerp(a.root.p, b.root.p, t);
        Quaternion rootQ_local = Quaternion.Slerp(a.root.q, b.root.q, t);
        Vector3 rootPw = FromSpaceP(rootP_local) + rootPositionOffsetWorld;
        Quaternion rootQw = Quaternion.Euler(rootRotationOffsetEuler) * FromSpaceQ(rootQ_local);

        // wrists (local-to-root → world)
        Vector3 lLp = Vector3.Lerp(a.left.wristLocalToRoot.p, b.left.wristLocalToRoot.p, t);
        Quaternion lLq = Quaternion.Slerp(a.left.wristLocalToRoot.q, b.left.wristLocalToRoot.q, t);
        Vector3 rLp = Vector3.Lerp(a.right.wristLocalToRoot.p, b.right.wristLocalToRoot.p, t);
        Quaternion rLq = Quaternion.Slerp(a.right.wristLocalToRoot.q, b.right.wristLocalToRoot.q, t);

        if (rightMirrorAxis != MirrorAxis.None)
        {
            rLp = MirrorVector(rLp, rightMirrorAxis);
            rLq = MirrorRotation(rLq, rightMirrorAxis);
        }

        if (_leftRig && _leftRig.wristRoot)
        {
            _leftRig.wristRoot.position = rootPw + rootQw * lLp;
            _leftRig.wristRoot.rotation = rootQw * lLq;
        }
        if (_rightRig && _rightRig.wristRoot)
        {
            _rightRig.wristRoot.position = rootPw + rootQw * rLp;
            _rightRig.wristRoot.rotation = rootQw * rLq;
        }

        ApplyLocalRotLerp(_leftRig, a.leftJoints, b.leftJoints, t, _leftJointOff, false);
        ApplyLocalRotLerp(_rightRig, a.rightJoints, b.rightJoints, t, _rightJointOff, true);
    }

    void ApplyFrame(FrameS f)
    {
        Vector3 rootPw = FromSpaceP(f.root.p) + rootPositionOffsetWorld;
        Quaternion rootQw = Quaternion.Euler(rootRotationOffsetEuler) * FromSpaceQ(f.root.q);

        if (_leftRig && _leftRig.wristRoot && f.left.wristLocalToRoot.tracked)
        {
            _leftRig.wristRoot.position = rootPw + rootQw * f.left.wristLocalToRoot.p;
            _leftRig.wristRoot.rotation = rootQw * f.left.wristLocalToRoot.q;
        }
        if (_rightRig && _rightRig.wristRoot && f.right.wristLocalToRoot.tracked)
        {
            Vector3 rP = f.right.wristLocalToRoot.p;
            Quaternion rQ = f.right.wristLocalToRoot.q;
            if (rightMirrorAxis != MirrorAxis.None) { rP = MirrorVector(rP, rightMirrorAxis); rQ = MirrorRotation(rQ, rightMirrorAxis); }

            _rightRig.wristRoot.position = rootPw + rootQw * rP;
            _rightRig.wristRoot.rotation = rootQw * rQ;
        }

        ApplyLocalRotSet(_leftRig, f.leftJoints, _leftJointOff, false);
        ApplyLocalRotSet(_rightRig, f.rightJoints, _rightJointOff, true);
    }

    // ======== ARTICULATION (skip wrist bone!) ========
    void ApplyLocalRotLerp(XRHandGhostRig rig, JointRotS[] A, JointRotS[] B, float t, PerJointOffsets off, bool isRight)
    {
        if (!rig || A == null || B == null) return;
        for (int i = 0; i < k_Joints.Length; i++)
        {
            var bone = rig.GetBone(k_Joints[i]);
            if (!bone) continue;
            if (i == 0 || bone == rig.wristRoot) continue;

            var ja = A[i]; var jb = B[i];
            bool tracked = ja.tracked || jb.tracked;
            if (!tracked) continue;

            var rec = Quaternion.Slerp(ja.localQ, jb.localQ, t);
            if (isRight && rightMirrorAxis != MirrorAxis.None)
                rec = MirrorRotation(rec, rightMirrorAxis);

            var ofs = useJointOffsets ? off.jointLocalOffsets[i] : Quaternion.identity;
            bone.localRotation = ofs * rec;
        }
    }

    void ApplyLocalRotSet(XRHandGhostRig rig, JointRotS[] J, PerJointOffsets off, bool isRight)
    {
        if (!rig || J == null) return;
        for (int i = 0; i < k_Joints.Length; i++)
        {
            var bone = rig.GetBone(k_Joints[i]);
            if (!bone) continue;
            if (i == 0 || bone == rig.wristRoot) continue;

            var jr = J[i];
            if (!jr.tracked) continue;

            var rot = jr.localQ;
            if (isRight && rightMirrorAxis != MirrorAxis.None)
                rot = MirrorRotation(rot, rightMirrorAxis);

            bone.localRotation = (useJointOffsets ? off.jointLocalOffsets[i] : Quaternion.identity) * rot;
        }
    }

    // ================== XR sampling & math ==================
    PoseS SamplePoseWorld(XRHand hand, XRHandJointID id)
    {
        PoseS ps = new PoseS { tracked = false, p = Vector3.zero, q = Quaternion.identity };
        var j = hand.GetJoint(id);
        if (j.TryGetPose(out Pose pose)) { ps.tracked = true; ps.p = pose.position; ps.q = pose.rotation; }
        return ps;
    }

    PoseS ComputeBimanualRoot(PoseS leftPalm, PoseS rightPalm, PoseS leftWrist, PoseS rightWrist)
    {
        PoseS root = new PoseS { tracked = false, p = Vector3.zero, q = Quaternion.identity };
        bool haveL = leftPalm.tracked, haveR = rightPalm.tracked;

        if (haveL && haveR)
        {
            root.tracked = true;
            root.p = (leftPalm.p + rightPalm.p) * 0.5f;
            root.q = Quaternion.Slerp(leftPalm.q, rightPalm.q, 0.5f);
        }
        else if (haveL) { root.tracked = true; root.p = leftPalm.p; root.q = leftPalm.q; }
        else if (haveR) { root.tracked = true; root.p = rightPalm.p; root.q = rightPalm.q; }

        return root;
    }

    PoseS WorldToLocalPose(PoseS world, PoseS rootWorld)
    {
        if (!world.tracked || !rootWorld.tracked)
            return new PoseS { tracked = false, p = Vector3.zero, q = Quaternion.identity };

        Quaternion invR = Quaternion.Inverse(rootWorld.q);
        Vector3 lp = invR * (world.p - rootWorld.p);
        Quaternion lq = invR * world.q;
        return new PoseS { tracked = true, p = lp, q = lq };
    }

    JointRotS[] SampleLocalRotations(XRHand hand)
    {
        var worldQ = new Quaternion[k_Joints.Length];
        var tracked = new bool[k_Joints.Length];

        for (int i = 0; i < k_Joints.Length; i++)
        {
            var j = hand.GetJoint(k_Joints[i]);
            if (j.TryGetPose(out Pose pose)) { tracked[i] = true; worldQ[i] = pose.rotation; }
            else { tracked[i] = false; worldQ[i] = Quaternion.identity; }
        }

        var arr = new JointRotS[k_Joints.Length];
        for (int i = 0; i < k_Joints.Length; i++)
        {
            int pi = k_Parent[i];
            Quaternion local = Quaternion.identity;

            if (pi < 0) local = Quaternion.identity; // wrist neutral
            else
            {
                if (tracked[i] && tracked[pi]) local = Quaternion.Inverse(worldQ[pi]) * worldQ[i];
                else if (tracked[i]) local = worldQ[i];
            }

            arr[i] = new JointRotS { id = (int)k_Joints[i], tracked = tracked[i], localQ = local };
        }
        return arr;
    }

    // ================== Space helpers ==================
    PoseS ToSpace(PoseS world)
    {
        if (spaceMode == SpaceMode.World || origin == null || !world.tracked) return world;
        return new PoseS
        {
            tracked = true,
            p = origin.InverseTransformPoint(world.p),
            q = Quaternion.Inverse(origin.rotation) * world.q
        };
    }

    Vector3 FromSpaceP(Vector3 p) => (spaceMode == SpaceMode.World || origin == null) ? p : origin.TransformPoint(p);
    Quaternion FromSpaceQ(Quaternion q) => (spaceMode == SpaceMode.World || origin == null) ? q : origin.rotation * q;

    // ================== Mirroring utilities ==================
    static Vector3 MirrorVector(Vector3 v, MirrorAxis axis)
    {
        switch (axis)
        {
            case MirrorAxis.X: return new Vector3(-v.x, v.y, v.z);
            case MirrorAxis.Y: return new Vector3(v.x, -v.y, v.z);
            case MirrorAxis.Z: return new Vector3(v.x, v.y, -v.z);
            default: return v;
        }
    }

    static Quaternion MirrorRotation(Quaternion q, MirrorAxis axis)
    {
        if (axis == MirrorAxis.None) return q;
        Matrix4x4 m = Matrix4x4.Rotate(q);
        Matrix4x4 s = Matrix4x4.identity;
        if (axis == MirrorAxis.X) s.m00 = -1f;
        else if (axis == MirrorAxis.Y) s.m11 = -1f;
        else if (axis == MirrorAxis.Z) s.m22 = -1f;
        Matrix4x4 m2 = s * m * s;
        return QuaternionFromMatrix(m2);
    }

    static Quaternion QuaternionFromMatrix(Matrix4x4 m)
    {
        Quaternion q = new Quaternion();
        float trace = m.m00 + m.m11 + m.m22;
        if (trace > 0f)
        {
            float s = Mathf.Sqrt(trace + 1f) * 2f;
            q.w = 0.25f * s;
            q.x = (m.m21 - m.m12) / s;
            q.y = (m.m02 - m.m20) / s;
            q.z = (m.m10 - m.m01) / s;
        }
        else if (m.m00 > m.m11 && m.m00 > m.m22)
        {
            float s = Mathf.Sqrt(1f + m.m00 - m.m11 - m.m22) * 2f;
            q.w = (m.m21 - m.m12) / s;
            q.x = 0.25f * s;
            q.y = (m.m01 + m.m10) / s;
            q.z = (m.m02 + m.m20) / s;
        }
        else if (m.m11 > m.m22)
        {
            float s = Mathf.Sqrt(1f - m.m00 + m.m11 - m.m22) * 2f;
            q.w = (m.m02 - m.m20) / s;
            q.x = (m.m01 + m.m10) / s;
            q.y = 0.25f * s;
            q.z = (m.m12 + m.m21) / s;
        }
        else
        {
            float s = Mathf.Sqrt(1f - m.m00 - m.m11 + m.m22) * 2f;
            q.w = (m.m10 - m.m01) / s;
            q.x = (m.m02 + m.m20) / s;
            q.y = (m.m12 + m.m21) / s;
            q.z = 0.25f * s;
        }
        float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        return (mag > 1e-8f) ? new Quaternion(q.x / mag, q.y / mag, q.z / mag, q.w / mag) : Quaternion.identity;
    }

    void AutoDetectRightMirrorIfNeeded()
    {
        if (!autoDetectRightMirrorByScale) return;
        if (rightMirrorAxis != MirrorAxis.None) return;
        if (_rightRig == null || _rightRig.wristRoot == null) return;

        Vector3 s = _rightRig.wristRoot.lossyScale;
        float parity = s.x * s.y * s.z; // negative means mirrored
        if (parity < 0f)
        {
            rightMirrorAxis = MirrorAxis.X; // common case
            Debug.LogWarning("[XRHandRecorder] Detected negative lossy scale on RIGHT rig; applying X-axis mirroring fix.");
        }
    }

    // ================== Save/Load ==================
    string SaveToPersistent(Recording r)
    {
        string dir = Application.persistentDataPath;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"hand_recording_mesh_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        string json = JsonUtility.ToJson(r, false);
        File.WriteAllText(path, json);
        return path;
    }

    Recording Load(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            var rec = JsonUtility.FromJson<Recording>(json);
            if (Enum.TryParse(rec.space, out SpaceMode saved)) spaceMode = saved;
            return rec;
        }
        catch (Exception e)
        {
            Debug.LogError($"[XRHandRecorder] Load failed: {e.Message}");
            return null;
        }
    }

    bool EnsureRecordingLoaded()
    {
        if (_activeRecording != null && _activeRecording.frames != null && _activeRecording.frames.Count > 0)
            return true;

        if (recordingAsset != null && !string.IsNullOrEmpty(recordingAsset.text))
        {
            _activeRecording = JsonUtility.FromJson<Recording>(recordingAsset.text);
            if (_activeRecording != null && Enum.TryParse(_activeRecording.space, out SpaceMode saved)) spaceMode = saved;
            return _activeRecording != null && _activeRecording.frames.Count > 0;
        }

        if (!string.IsNullOrEmpty(streamingAssetsFileName))
        {
            string path = Path.Combine(Application.streamingAssetsPath, streamingAssetsFileName);
            if (File.Exists(path))
            {
                _activeRecording = Load(path);
                return _activeRecording != null && _activeRecording.frames.Count > 0;
            }
        }

        if (fallbackToLatestInPersisted)
        {
            var path = FindLatestRecordingPath();
            if (!string.IsNullOrEmpty(path))
            {
                _activeRecording = Load(path);
                return _activeRecording != null && _activeRecording.frames.Count > 0;
            }
        }

        return false;
    }

    string FindLatestRecordingPath()
    {
        string dir = Application.persistentDataPath;
        if (!Directory.Exists(dir)) return null;
        var files = Directory.GetFiles(dir, "hand_recording_mesh_*.json");
        if (files.Length == 0) return null;
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        return files[files.Length - 1];
    }

    void EnsureRigs()
    {
        if (_leftRig == null && leftHandRigPrefab != null)
        {
            _leftRig = Instantiate(leftHandRigPrefab, transform);
            ApplyGhostMaterial(_leftRig);
            ValidateRigMapping(_leftRig, "LEFT");
        }
        if (_rightRig == null && rightHandRigPrefab != null)
        {
            _rightRig = Instantiate(rightHandRigPrefab, transform);
            ApplyGhostMaterial(_rightRig);
            ValidateRigMapping(_rightRig, "RIGHT");
        }
    }

    void ApplyGhostMaterial(XRHandGhostRig rig)
    {
        if (!rig || !ghostMaterial || !rig.skinnedMesh) return;
        rig.skinnedMesh.sharedMaterial = ghostMaterial;
    }

    void ValidateRigMapping(XRHandGhostRig rig, string side)
    {
        if (!rig) return;
        var wristBone = rig.GetBone(XRHandJointID.Wrist);
        if (wristBone == rig.wristRoot)
            Debug.LogWarning($"[XRHandRecorder] {side} rig wrist bone == wristRoot (expected). Skipping wrist in articulation to preserve world rotation.");
    }

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.UpperCenter };
        float w = Screen.width;

        if (_countingDown)
            GUI.Label(new Rect(0, 10, w, 30), $"Recording in {Mathf.CeilToInt(_countdownT)}s...", style);

        if (_recording)
        {
            float t = Mathf.Clamp(recordSeconds - (Time.time - _recordStartT), 0f, recordSeconds);
            GUI.Label(new Rect(0, 40, w, 30), $"Recording… {t:0.0}s left", style);
        }

        if (_replaying)
        {
            float t = Mathf.Clamp(_recordedDuration - (Time.time - _playStartT), 0f, _recordedDuration);
            GUI.Label(new Rect(0, 70, w, 30), $"Replaying… {t:0.0}s left", style);
        }
    }

    // ================== Calibration helpers ==================
    [ContextMenu("Calibrate Joint Offsets (Current Pose)")]
    void CalibrateJointOffsets()
    {
        if (_handSub == null) { Debug.LogWarning("[XRHandRecorder] No XRHandSubsystem."); return; }
        EnsureRigs();

        var lLoc = SampleLocalRotations(_handSub.leftHand);
        for (int i = 0; i < k_Joints.Length; i++)
        {
            var bone = _leftRig ? _leftRig.GetBone(k_Joints[i]) : null;
            _leftJointOff.jointLocalOffsets[i] = (i == 0 || bone == null) ? Quaternion.identity : bone.localRotation * Quaternion.Inverse(lLoc[i].localQ);
        }

        var rLoc = SampleLocalRotations(_handSub.rightHand);
        for (int i = 0; i < k_Joints.Length; i++)
        {
            var bone = _rightRig ? _rightRig.GetBone(k_Joints[i]) : null;
            _rightJointOff.jointLocalOffsets[i] = (i == 0 || bone == null) ? Quaternion.identity : bone.localRotation * Quaternion.Inverse(rLoc[i].localQ);
        }

        Debug.Log("[XRHandRecorder] Joint offset calibration complete.");
    }

    // Smoothly pull tiny per-hand world nudges back to zero, then re-apply overlay.
    private void RealtimeCalibrateStep(float dt)
    {
        if (dt <= 0f) return;

        float a = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, realtimeSmoothingSeconds));

        leftWristWorldPosNudge = Vector3.Lerp(leftWristWorldPosNudge, Vector3.zero, a);
        rightWristWorldPosNudge = Vector3.Lerp(rightWristWorldPosNudge, Vector3.zero, a);

        if (realtimeIncludeRotation)
        {
            leftWristWorldEulerNudge = Vector3.Lerp(leftWristWorldEulerNudge, Vector3.zero, a);
            rightWristWorldEulerNudge = Vector3.Lerp(rightWristWorldEulerNudge, Vector3.zero, a);
        }

        ApplyExactOverlayFromXR();
    }

    [ContextMenu("Fit Right Wrist Z-Only")]
    void FitRightWristZOnly()
    {
        AutoDetectRightMirrorIfNeeded();
        if (_handSub == null) { Debug.LogWarning("[XRHandRecorder] No XRHandSubsystem."); return; }

        var rw = SamplePoseWorld(_handSub.rightHand, XRHandJointID.Wrist);
        var lp = SamplePoseWorld(_handSub.leftHand, XRHandJointID.Palm);
        var rp = SamplePoseWorld(_handSub.rightHand, XRHandJointID.Palm);
        var lw = SamplePoseWorld(_handSub.leftHand, XRHandJointID.Wrist);
        var rootWorld = ComputeBimanualRoot(lp, rp, lw, rw);
        if (!rootWorld.tracked || !rw.tracked) return;

        var rLocalPose = WorldToLocalPose(rw, rootWorld);
        var rootPw = FromSpaceP(ToSpace(rootWorld).p);
        var rootQw = FromSpaceQ(ToSpace(rootWorld).q);

        float desiredZ = (Quaternion.Inverse(rootQw) * (rw.p - rootPw)).z;
        float recordedZ = rLocalPose.p.z;
        if (rightMirrorAxis != MirrorAxis.None)
            recordedZ = MirrorVector(rLocalPose.p, rightMirrorAxis).z;

        float deltaZ = desiredZ - recordedZ;

        rightWristWorldPosNudge += (_rightRig && _rightRig.wristRoot)
            ? _rightRig.wristRoot.rotation * new Vector3(0, 0, deltaZ)
            : new Vector3(0, 0, deltaZ);

        Debug.Log($"[XRHandRecorder] Overlay world Z nudge += {deltaZ:0.###}");
    }

    IEnumerator AutoFitRightZOnce()
    {
        float t = 0f;
        while (t < autoFitDelaySeconds) { t += Time.deltaTime; yield return null; }
        EnsureRigs();
        AutoDetectRightMirrorIfNeeded();
        FitRightWristZOnly();
    }

    // ================== XR Subsystem ==================
    XRHandSubsystem TryGetHandSubsystem()
    {
        try
        {
            var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
            return loader?.GetLoadedSubsystem<XRHandSubsystem>();
        }
        catch { return null; }
    }

#if UNITY_EDITOR
    // -------- Editor-only: Save JSON into Assets/BSLGestures ----------
    string SaveIntoAssets(Recording r, string gestureName)
    {
        if (string.IsNullOrEmpty(assetsFolder)) assetsFolder = "Assets/BSLGestures";
        EnsureFolderExists(assetsFolder);

        string safe = MakeSafeFileName(string.IsNullOrEmpty(gestureName) ? "Gesture" : gestureName);
        string path = Path.Combine(assetsFolder, $"BSL_{safe}.json").Replace("\\", "/");

        string json = JsonUtility.ToJson(r, false);
        File.WriteAllText(path, json);
        AssetDatabase.ImportAsset(path);
        recordingAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        EditorUtility.SetDirty(this);
        return path;
    }

    static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath)) return;

        string[] parts = folderPath.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    static string MakeSafeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c.ToString(), "_");
        return name.Trim();
    }

    [ContextMenu("Import Latest Recording Into Assets (as TextAsset)")]
    void ImportLatestIntoAssets()
    {
        string src = FindLatestRecordingPath();
        if (string.IsNullOrEmpty(src)) { Debug.LogWarning("[XRHandRecorder] No recording found."); return; }

        if (string.IsNullOrEmpty(currentGestureName)) currentGestureName = "Gesture";
        EnsureFolderExists(assetsFolder);

        string safe = MakeSafeFileName(currentGestureName);
        string dst = Path.Combine(assetsFolder, $"BSL_{safe}.json").Replace("\\", "/");

        File.WriteAllBytes(dst, File.ReadAllBytes(src));
        AssetDatabase.Refresh();

        recordingAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(dst);
        Debug.Log("[XRHandRecorder] Imported and assigned: " + dst);
    }

    [CustomEditor(typeof(XRHandRecorder))]
    class XRHandRecorderEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var rec = (XRHandRecorder)target;
            GUI.enabled = Application.isPlaying;
            EditorGUILayout.Space(8);

            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Calibration Helpers (Play Mode)", EditorStyles.boldLabel);
                if (GUILayout.Button("Calibrate Joint Offsets (Current Pose)")) rec.CalibrateJointOffsets();
                if (GUILayout.Button("Right Z Only (Quick Fix)")) rec.FitRightWristZOnly();
            }

            GUI.enabled = true;
        }
    }
#endif
}
