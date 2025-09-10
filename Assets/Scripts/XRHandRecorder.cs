using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

public class XRHandRecorder : MonoBehaviour
{
    [Header("Controls")]
    public KeyCode recordKey = KeyCode.R;
    public KeyCode replayKey = KeyCode.P;

    [Header("Timing")]
    public float countdownSeconds = 5f;
    public float recordSeconds = 6f;

    [Header("Ghost visual")]
    [Tooltip("Transparent material swapped onto the hand meshes during replay.")]
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

    // Stable joint order and parents for local rotations
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
        -1,  // Wrist
        0,   // Palm <- Wrist
        1,2,3,4,        // Thumb
        1,6,7,8,9,      // Index
        1,11,12,13,14,  // Middle
        1,16,17,18,19,  // Ring
        1,21,22,23,24   // Little
    };

    // --------- Serializable data ---------
    [Serializable] public class PoseS { public bool tracked; public Vector3 p; public Quaternion q; }

    [Serializable] public class JointRotS { public int id; public bool tracked; public Quaternion localQ; }

    [Serializable]
    public class HandFrameS
    {
        // For analysis (not used to position rigs when bimanual-root mode is on)
        public PoseS wristWorld;  // world/origin-local wrist
        public PoseS palmWorld;   // world/origin-local palm

        // ❗ Local to the shared bimanual ROOT (this drives the rigs)
        public PoseS wristLocalToRoot;
    }

    [Serializable]
    public class FrameS
    {
        public float t;
        public PoseS root;     // shared bimanual root pose (space = World/OriginLocal)
        public HandFrameS left, right;
        public JointRotS[] leftJoints;   // local rotations (relative to XR joint parent)
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

    // ---------- Per-joint local rotation offsets (align rig bind with XR locals) ----------
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

    void Update()
    {
        if (Input.GetKeyDown(recordKey) && !_recording && !_countingDown) StartTimedRecordAndSave();
        if (Input.GetKeyDown(replayKey) && !_replaying) PlayLast();

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

    public void PlayLast()
    {
        if (_replaying) return;

        if (_activeRecording == null || _activeRecording.frames.Count == 0)
        {
            var path = FindLatestRecordingPath();
            if (string.IsNullOrEmpty(path)) { Debug.LogWarning("[XRHandRecorder] No recording found."); return; }
            _activeRecording = Load(path);
            if (_activeRecording == null) { Debug.LogWarning("[XRHandRecorder] Failed to load recording."); return; }
        }

        BeginReplay();
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
        var path = Save(_activeRecording);
        Debug.Log($"[XRHandRecorder] Recording saved to:\n{path}");
    }

    void SampleFrame(float t)
    {
        var f = new FrameS { t = t, left = new HandFrameS(), right = new HandFrameS() };

        // 1) sample world palms/wrists
        var lw = SamplePoseWorld(_handSub.leftHand, XRHandJointID.Wrist);
        var lp = SamplePoseWorld(_handSub.leftHand, XRHandJointID.Palm);
        var rw = SamplePoseWorld(_handSub.rightHand, XRHandJointID.Wrist);
        var rp = SamplePoseWorld(_handSub.rightHand, XRHandJointID.Palm);

        // 2) build bimanual ROOT from palms (fallbacks if one hand not tracked)
        var rootWorld = ComputeBimanualRoot(lp, rp, lw, rw);

        // 3) convert to configured space
        f.root = ToSpace(rootWorld);

        f.left.wristWorld = ToSpace(lw);
        f.left.palmWorld = ToSpace(lp);
        f.right.wristWorld = ToSpace(rw);
        f.right.palmWorld = ToSpace(rp);

        // 4) store each hand's wrist pose LOCAL to the shared root
        f.left.wristLocalToRoot = WorldToLocalPose(lw, rootWorld);
        f.right.wristLocalToRoot = WorldToLocalPose(rw, rootWorld);

        // 5) articulation (local joint rotations)
        f.leftJoints = SampleLocalRotations(_handSub.leftHand);
        f.rightJoints = SampleLocalRotations(_handSub.rightHand);

        _activeRecording.frames.Add(f);
    }

    PoseS SamplePoseWorld(XRHand hand, XRHandJointID id)
    {
        PoseS ps = new PoseS { tracked = false, p = Vector3.zero, q = Quaternion.identity };
        var j = hand.GetJoint(id);
        if (j.TryGetPose(out Pose pose)) { ps.tracked = true; ps.p = pose.position; ps.q = pose.rotation; }
        return ps;
    }

    // Build a stable root using both palms.
    PoseS ComputeBimanualRoot(PoseS leftPalm, PoseS rightPalm, PoseS leftWrist, PoseS rightWrist)
    {
        PoseS root = new PoseS { tracked = false, p = Vector3.zero, q = Quaternion.identity };

        bool haveL = leftPalm.tracked;
        bool haveR = rightPalm.tracked;

        if (haveL && haveR)
        {
            // Position = midpoint of palms
            Vector3 x = (rightPalm.p - leftPalm.p);
            float xmag = x.magnitude;
            Vector3 xAxis = xmag > 1e-5f ? x / xmag : Vector3.right;

            // Y ~ average of wrist->palm directions (fallback to world up)
            Vector3 yA = (leftPalm.p - leftWrist.p);
            Vector3 yB = (rightPalm.p - rightWrist.p);
            Vector3 yAxis = ((haveL ? yA : Vector3.up) + (haveR ? yB : Vector3.up)).normalized;
            if (yAxis.sqrMagnitude < 1e-6f) yAxis = Vector3.up;

            Vector3 zAxis = Vector3.Cross(xAxis, yAxis).normalized;
            yAxis = Vector3.Cross(zAxis, xAxis).normalized;

            root.tracked = true;
            root.p = (leftPalm.p + rightPalm.p) * 0.5f;
            root.q = Quaternion.LookRotation(zAxis, yAxis);
        }
        else if (haveL)
        {
            root.tracked = true;
            root.p = leftPalm.p;
            root.q = leftPalm.q;
        }
        else if (haveR)
        {
            root.tracked = true;
            root.p = rightPalm.p;
            root.q = rightPalm.q;
        }

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

            if (pi < 0) local = Quaternion.identity; // wrist local neutral
            else
            {
                if (tracked[i] && tracked[pi]) local = Quaternion.Inverse(worldQ[pi]) * worldQ[i];
                else if (tracked[i]) local = worldQ[i];
            }

            arr[i] = new JointRotS { id = (int)k_Joints[i], tracked = tracked[i], localQ = local };
        }
        return arr;
    }

    // ================== Replay ==================
    void BeginReplay()
    {
        if (_leftRig == null && leftHandRigPrefab != null) _leftRig = Instantiate(leftHandRigPrefab, transform);
        if (_rightRig == null && rightHandRigPrefab != null) _rightRig = Instantiate(rightHandRigPrefab, transform);

        ApplyGhostMaterial(_leftRig);
        ApplyGhostMaterial(_rightRig);

        _replaying = true;
        _playStartT = Time.time;
        _nextFrameIdx = 1;
        _recordedDuration = Mathf.Max(_activeRecording.duration, _activeRecording.frames[_activeRecording.frames.Count - 1].t);
        Debug.Log("[XRHandRecorder] Replay started (bimanual root).");
    }

    void StepReplay()
    {
        float t = Time.time - _playStartT;

        if (t >= _recordedDuration)
        {
            ApplyFrame(_activeRecording.frames[_activeRecording.frames.Count - 1]);
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
        // Interpolate ROOT (space → world)
        Vector3 rootP_local = Vector3.Lerp(a.root.p, b.root.p, t);
        Quaternion rootQ_local = Quaternion.Slerp(a.root.q, b.root.q, t);
        Vector3 rootPw = FromSpaceP(rootP_local);
        Quaternion rootQw = FromSpaceQ(rootQ_local);

        // Interpolate each hand's LOCAL-to-root wrist pose
        Vector3 lLp = Vector3.Lerp(a.left.wristLocalToRoot.p, b.left.wristLocalToRoot.p, t);
        Quaternion lLq = Quaternion.Slerp(a.left.wristLocalToRoot.q, b.left.wristLocalToRoot.q, t);

        Vector3 rLp = Vector3.Lerp(a.right.wristLocalToRoot.p, b.right.wristLocalToRoot.p, t);
        Quaternion rLq = Quaternion.Slerp(a.right.wristLocalToRoot.q, b.right.wristLocalToRoot.q, t);

        // Compose: world = root * local
        Vector3 lPw = rootPw + rootQw * lLp;
        Quaternion lQw = rootQw * lLq;

        Vector3 rPw = rootPw + rootQw * rLp;
        Quaternion rQw = rootQw * rLq;

        if (_leftRig && _leftRig.wristRoot) { _leftRig.wristRoot.position = lPw; _leftRig.wristRoot.rotation = lQw; }
        if (_rightRig && _rightRig.wristRoot) { _rightRig.wristRoot.position = rPw; _rightRig.wristRoot.rotation = rQw; }

        // Articulation (local joint rotations + optional offsets)
        ApplyLocalRotLerp(_leftRig, a.leftJoints, b.leftJoints, t, _leftJointOff);
        ApplyLocalRotLerp(_rightRig, a.rightJoints, b.rightJoints, t, _rightJointOff);
    }

    void ApplyFrame(FrameS f)
    {
        Vector3 rootPw = FromSpaceP(f.root.p);
        Quaternion rootQw = FromSpaceQ(f.root.q);

        if (_leftRig && _leftRig.wristRoot && f.left.wristLocalToRoot.tracked)
        {
            _leftRig.wristRoot.position = rootPw + rootQw * f.left.wristLocalToRoot.p;
            _leftRig.wristRoot.rotation = rootQw * f.left.wristLocalToRoot.q;
        }
        if (_rightRig && _rightRig.wristRoot && f.right.wristLocalToRoot.tracked)
        {
            _rightRig.wristRoot.position = rootPw + rootQw * f.right.wristLocalToRoot.p;
            _rightRig.wristRoot.rotation = rootQw * f.right.wristLocalToRoot.q;
        }

        ApplyLocalRotSet(_leftRig, f.leftJoints, _leftJointOff);
        ApplyLocalRotSet(_rightRig, f.rightJoints, _rightJointOff);
    }

    void ApplyLocalRotLerp(XRHandGhostRig rig, JointRotS[] A, JointRotS[] B, float t, PerJointOffsets off)
    {
        if (!rig || A == null || B == null) return;
        for (int i = 0; i < k_Joints.Length; i++)
        {
            var bone = rig.GetBone(k_Joints[i]);
            if (!bone) continue;

            var ja = A[i]; var jb = B[i];
            bool tracked = ja.tracked || jb.tracked;
            if (!tracked) continue;

            if (i == 0) { bone.localRotation = Quaternion.identity; continue; }

            var rec = Quaternion.Slerp(ja.localQ, jb.localQ, t);
            var ofs = useJointOffsets ? off.jointLocalOffsets[i] : Quaternion.identity;
            bone.localRotation = ofs * rec;
        }
    }

    void ApplyLocalRotSet(XRHandGhostRig rig, JointRotS[] J, PerJointOffsets off)
    {
        if (!rig || J == null) return;
        for (int i = 0; i < k_Joints.Length; i++)
        {
            var bone = rig.GetBone(k_Joints[i]);
            if (!bone) continue;

            var jr = J[i];
            if (!jr.tracked) continue;

            if (i == 0) { bone.localRotation = Quaternion.identity; continue; }
            bone.localRotation = (useJointOffsets ? off.jointLocalOffsets[i] : Quaternion.identity) * jr.localQ;
        }
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

    Vector3 FromSpaceP(Vector3 p)
    {
        return (spaceMode == SpaceMode.World || origin == null) ? p : origin.TransformPoint(p);
    }
    Quaternion FromSpaceQ(Quaternion q)
    {
        return (spaceMode == SpaceMode.World || origin == null) ? q : origin.rotation * q;
    }

    // ================== Save/Load ==================
    string Save(Recording r)
    {
        string dir = Application.persistentDataPath;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"hand_recording_mesh_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        string json = JsonUtility.ToJson(r, prettyPrint: false);
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

    string FindLatestRecordingPath()
    {
        string dir = Application.persistentDataPath;
        if (!Directory.Exists(dir)) return null;
        var files = Directory.GetFiles(dir, "hand_recording_mesh_*.json");
        if (files.Length == 0) return null;
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        return files[files.Length - 1];
    }

    void ApplyGhostMaterial(XRHandGhostRig rig)
    {
        if (!rig || !ghostMaterial || !rig.skinnedMesh) return;
        rig.skinnedMesh.sharedMaterial = ghostMaterial;
    }

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.UpperCenter };
        float w = Screen.width;

        if (_countingDown)
            GUI.Label(new Rect(0, 10, w, 30), $"Recording in {Mathf.CeilToInt(_countdownT)}...", style);

        if (_recording)
        {
            float t = Mathf.Clamp(recordSeconds - (Time.time - _recordStartT), 0f, recordSeconds);
            GUI.Label(new Rect(0, 40, w, 30), $"Recording… {t:0.0}s left", style);
        }

        if (_replaying)
        {
            float t = Mathf.Clamp(_recordedDuration - (Time.time - _playStartT), 0f, _recordedDuration);
            GUI.Label(new Rect(0, 70, w, 30), $"Replaying ghost… {t:0.0}s left", style);
        }
    }

    // ================== Calibration (joint offsets only) ==================
    [ContextMenu("Calibrate Joint Offsets (Current Pose)")]
    void CalibrateJointOffsets()
    {
        if (_handSub == null) { Debug.LogWarning("[XRHandRecorder] No XRHandSubsystem."); return; }
        if (_leftRig == null && leftHandRigPrefab != null) _leftRig = Instantiate(leftHandRigPrefab, transform);
        if (_rightRig == null && rightHandRigPrefab != null) _rightRig = Instantiate(rightHandRigPrefab, transform);

        // LEFT
        var lLoc = SampleLocalRotations(_handSub.leftHand);
        for (int i = 0; i < k_Joints.Length; i++)
        {
            var bone = _leftRig ? _leftRig.GetBone(k_Joints[i]) : null;
            _leftJointOff.jointLocalOffsets[i] = (i == 0 || bone == null) ? Quaternion.identity : bone.localRotation * Quaternion.Inverse(lLoc[i].localQ);
        }

        // RIGHT
        var rLoc = SampleLocalRotations(_handSub.rightHand);
        for (int i = 0; i < k_Joints.Length; i++)
        {
            var bone = _rightRig ? _rightRig.GetBone(k_Joints[i]) : null;
            _rightJointOff.jointLocalOffsets[i] = (i == 0 || bone == null) ? Quaternion.identity : bone.localRotation * Quaternion.Inverse(rLoc[i].localQ);
        }

        Debug.Log("[XRHandRecorder] Joint offset calibration complete.");
    }

    // ================== Helpers ==================
    XRHandSubsystem TryGetHandSubsystem()
    {
        try
        {
            var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
            return loader?.GetLoadedSubsystem<XRHandSubsystem>();
        }
        catch { return null; }
    }
}
