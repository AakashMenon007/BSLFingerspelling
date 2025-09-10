#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using System;

public class BSLTwoHandRecorderWindow : EditorWindow
{
    // --- Config ---
    const float DEFAULT_COUNTDOWN = 5f;           // as requested
    const float DEFAULT_TOL = 0.12f;              // default per-finger tolerance
    const float MIN_SIG_VERTICAL = 0.20f;         // consider vertical relation if >= this (normalized)
    const float MIN_SIG_DEPTH = 0.15f;         // consider depth relation if >= this (normalized)

    // --- UI state ---
    [SerializeField] BSLTwoHandGesture target;     // where we save the snapshot
    [SerializeField] string newAssetName = "NewGesture";
    [SerializeField] char letter = 'A';
    [SerializeField] float countdownSeconds = DEFAULT_COUNTDOWN;
    [SerializeField] float curlTolerance = DEFAULT_TOL;

    // runtime for countdown
    bool waiting;
    double captureAt; // Editor time (seconds)
    string status = "Idle.";

    // cached subsystem
    XRHandSubsystem hands;

    // Menu
    [MenuItem("Tools/BSL/Two-Hand Recorder")]
    static void Open() => GetWindow<BSLTwoHandRecorderWindow>("BSL Two-Hand Recorder");

    void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    void OnGUI()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Capture a two-hand BSL gesture into an asset after a 5s countdown.", EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space(6);

        // Target asset row
        using (new EditorGUILayout.HorizontalScope())
        {
            target = (BSLTwoHandGesture)EditorGUILayout.ObjectField("Target Asset", target, typeof(BSLTwoHandGesture), false);
            if (GUILayout.Button("Ping", GUILayout.Width(60)) && target != null)
                EditorGUIUtility.PingObject(target);
        }

        // Quick create asset
        EditorGUILayout.LabelField("Quick Create New Asset", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            newAssetName = EditorGUILayout.TextField("Name", newAssetName);
            letter = EditorGUILayout.TextField("Letter", letter.ToString()).Length > 0
                ? EditorGUILayout.TextField("", letter.ToString())[0]
                : letter;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Create in Assets/Gestures"))
            {
                EnsureFolder("Assets/Gestures");
                var created = ScriptableObject.CreateInstance<BSLTwoHandGesture>();
                created.displayName = newAssetName;
                created.letter = letter;
                var path = $"Assets/Gestures/{Sanitize(newAssetName)}.asset";
                AssetDatabase.CreateAsset(created, path);
                AssetDatabase.SaveAssets();
                target = created;
                EditorGUIUtility.PingObject(created);
                status = $"Created new asset at {path}";
            }
            if (GUILayout.Button("Select Created"))
            {
                if (target) Selection.activeObject = target;
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Capture Settings", EditorStyles.boldLabel);
        countdownSeconds = EditorGUILayout.FloatField("Countdown (s)", countdownSeconds);
        curlTolerance = EditorGUILayout.Slider("Curl Tolerance", curlTolerance, 0.05f, 0.35f);

        EditorGUILayout.Space(8);
        using (new EditorGUI.DisabledGroupScope(!EditorApplication.isPlaying))
        {
            if (GUILayout.Button(waiting ? "Cancel" : $"Record (Wait {countdownSeconds:0}s)"))
            {
                if (waiting) Cancel();
                else BeginCountdown();
            }
        }

        // Progress / status
        EditorGUILayout.Space(8);
        if (waiting)
        {
            double now = EditorApplication.timeSinceStartup;
            float remain = Mathf.Max(0f, (float)(captureAt - now));
            var rect = GUILayoutUtility.GetRect(18, 18, "TextField");
            EditorGUI.ProgressBar(rect, 1f - (remain / Mathf.Max(0.0001f, countdownSeconds)), $"Capturing in {Mathf.CeilToInt(remain)}…");
        }
        EditorGUILayout.HelpBox(status, MessageType.Info);

        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to access live XR Hands data.", MessageType.Warning);
        }
    }

    void OnEditorUpdate()
    {
        if (!waiting) return;
        if (!EditorApplication.isPlaying)
        {
            status = "Cancelled: left Play Mode.";
            waiting = false;
            return;
        }

        if (EditorApplication.timeSinceStartup >= captureAt)
        {
            waiting = false;
            CaptureOnce();
        }
        else
        {
            Repaint(); // refresh countdown UI
        }
    }

    void BeginCountdown()
    {
        if (target == null)
        {
            status = "Assign or create a BSLTwoHandGesture asset first.";
            return;
        }
        hands = GetHands();
        if (hands == null)
        {
            status = "XRHandSubsystem not found. Check XR Hands & active loader.";
            return;
        }
        waiting = true;
        captureAt = EditorApplication.timeSinceStartup + Math.Max(0.01, countdownSeconds);
        status = $"Countdown started ({countdownSeconds:0}s)… Hold your pose at the beep.";
    }

    void Cancel()
    {
        waiting = false;
        status = "Cancelled.";
    }

    void CaptureOnce()
    {
        // Extract both hands
        var lf = HandFeatureDefs.Extract(hands.leftHand, true);
        var rf = HandFeatureDefs.Extract(hands.rightHand, false);

        if (!lf.tracked || !rf.tracked)
        {
            status = "Hands not tracked at capture moment. Try again.";
            return;
        }

        // Write curls
        target.left.thumb = lf.curl[0];
        target.left.index = lf.curl[1];
        target.left.middle = lf.curl[2];
        target.left.ring = lf.curl[3];
        target.left.little = lf.curl[4];

        target.right.thumb = rf.curl[0];
        target.right.index = rf.curl[1];
        target.right.middle = rf.curl[2];
        target.right.ring = rf.curl[3];
        target.right.little = rf.curl[4];

        // Set uniform tolerances (user-configurable)
        target.left.tolThumb = target.left.tolIndex = target.left.tolMiddle = target.left.tolRing = target.left.tolLittle = curlTolerance;
        target.right.tolThumb = target.right.tolIndex = target.right.tolMiddle = target.right.tolRing = target.right.tolLittle = curlTolerance;

        // Palms
        target.left.usePalmNormal = true;
        target.right.usePalmNormal = true;
        target.left.expectedPalmNormal = lf.palmNormal.normalized;
        target.right.expectedPalmNormal = rf.palmNormal.normalized;

        // Inter-hand rules (head-local)
        var head = Camera.main ? Camera.main.transform : null;
        if (head != null)
        {
            Vector3 lH = head.InverseTransformPoint(lf.wristPos);
            Vector3 rH = head.InverseTransformPoint(rf.wristPos);

            float avgScale = Mathf.Max(1e-3f, (lf.scale + rf.scale) * 0.5f);
            float dyN = (rH.y - lH.y) / avgScale; // right - left
            float dzN = (rH.z - lH.z) / avgScale; // forward is +z
            float distN = Vector3.Distance(lH, rH) / avgScale;

            // Vertical order (e.g., right arm over left)
            if (Mathf.Abs(dyN) >= MIN_SIG_VERTICAL)
            {
                target.useVerticalOrder = true;
                target.verticalOrder = dyN >= 0 ? VerticalOrder.RightAboveLeft : VerticalOrder.LeftAboveRight;
                target.minVerticalSepNorm = Mathf.Clamp(Mathf.Abs(dyN) * 0.65f, 0.25f, 1.2f);
            }
            else target.useVerticalOrder = false;

            // Depth order (right in front of left, etc.)
            if (Mathf.Abs(dzN) >= MIN_SIG_DEPTH)
            {
                target.useDepthOrder = true;
                target.depthOrder = dzN >= 0 ? DepthOrder.RightInFrontOfLeft : DepthOrder.LeftInFrontOfRight;
                target.minDepthSepNorm = Mathf.Clamp(Mathf.Abs(dzN) * 0.65f, 0.18f, 1.0f);
            }
            else target.useDepthOrder = false;

            // Distance band (helps letters that require contact/close proximity vs apart)
            target.useWristDistance = true;
            target.minWristDistNorm = Mathf.Clamp(distN * 0.6f, 0.25f, 1.0f);
            target.maxWristDistNorm = Mathf.Clamp(distN * 1.6f, 1.2f, 3.0f);

            // Palm relation (parallel / opposed / perpendicular)
            float ang = Vector3.Angle(lf.palmNormal, rf.palmNormal);
            float dPar = Mathf.Abs(ang - 0f);
            float dOpp = Mathf.Abs(ang - 180f);
            float dPer = Mathf.Abs(ang - 90f);
            if (Mathf.Min(dPar, dOpp, dPer) > 35f) target.palmRelation = PalmRelation.Any;
            else if (dPar <= dOpp && dPar <= dPer) { target.palmRelation = PalmRelation.Parallel; target.palmRelationTolDeg = 25f; }
            else if (dOpp <= dPar && dOpp <= dPer) { target.palmRelation = PalmRelation.Opposed; target.palmRelationTolDeg = 25f; }
            else { target.palmRelation = PalmRelation.Perpendicular; target.palmRelationTolDeg = 25f; }
        }
        else
        {
            // No camera found — keep inter-hand options off except distance band (optional)
            target.useVerticalOrder = false;
            target.useDepthOrder = false;
            target.useWristDistance = false;
            target.palmRelation = PalmRelation.Any;
        }

        // Save asset
        EditorUtility.SetDirty(target);
        AssetDatabase.SaveAssets();

        status = $"Captured '{(string.IsNullOrEmpty(target.displayName) ? newAssetName : target.displayName)}' (letter {(target.letter == '\0' ? letter : target.letter)})";
        Repaint();
        EditorGUIUtility.PingObject(target);
    }

    // --- helpers ---
    XRHandSubsystem GetHands()
    {
        var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
        return loader ? loader.GetLoadedSubsystem<XRHandSubsystem>() : null;
    }

    static void EnsureFolder(string path)
    {
        if (!AssetDatabase.IsValidFolder(path))
        {
            var parts = path.Split('/');
            string curr = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = curr + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(curr, parts[i]);
                curr = next;
            }
        }
    }

    static string Sanitize(string name)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim();
    }
}
#endif
