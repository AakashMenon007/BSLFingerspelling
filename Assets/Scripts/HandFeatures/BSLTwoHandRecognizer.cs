using UnityEngine;
using UnityEngine.XR.Hands;
using TMPro;

public class BSLTwoHandRecognizer : MonoBehaviour
{
    [Tooltip("Drag all your saved two-hand gesture assets here.")]
    public BSLTwoHandGesture[] gestures;

    [Header("Detection")]
    [Range(0, 1)] public float acceptThreshold = 0.65f;
    public float stableSeconds = 0.35f;

    [Header("UI (optional)")]
    public TMP_Text output;             // 3D or UI TMP
    public bool showConfidence = true;
    public bool logToConsole = false;

    [Header("Diagnostics")]
    public bool verbose = false;

    XRHandSubsystem hands;
    Transform head;
    float holdT;
    BSLTwoHandGesture lastBest;
    float lastConf;

    void Awake()
    {
        // Auto-find TMP if not assigned
        if (!output) output = GetComponentInChildren<TMP_Text>(true);

        // Find a camera for head-local comparisons
        if (Camera.main) head = Camera.main.transform;
        else
        {
            var anyCam = FindFirstObjectByType<Camera>();
            if (anyCam) head = anyCam.transform;
        }
    }

    void OnEnable()
    {
        var loader = UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager?.activeLoader;
        hands = loader != null ? loader.GetLoadedSubsystem<XRHandSubsystem>() : null;
        if (hands != null) hands.updatedHands += OnHandsUpdated;
        else if (verbose) Debug.LogWarning("[BSL] XRHandSubsystem not found. Will retry in Update().");
    }

    void OnDisable()
    {
        if (hands != null) hands.updatedHands -= OnHandsUpdated;
    }

    void Update()
    {
        // Retry grabbing hands if they weren’t ready on OnEnable
        if (hands == null)
        {
            var loader = UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager?.activeLoader;
            hands = loader != null ? loader.GetLoadedSubsystem<XRHandSubsystem>() : null;
            if (hands != null) hands.updatedHands += OnHandsUpdated;
        }

        // Retry finding a head camera if needed
        if (head == null)
        {
            if (Camera.main) head = Camera.main.transform;
            else
            {
                var anyCam = FindFirstObjectByType<Camera>();
                if (anyCam) head = anyCam.transform;
            }
        }
    }

    void OnHandsUpdated(XRHandSubsystem s, XRHandSubsystem.UpdateSuccessFlags success, XRHandSubsystem.UpdateType type)
    {
        // Only bail if NEITHER hand reported updates this frame (use &&, not ||)
        if ((success & XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints) == 0 &&
            (success & XRHandSubsystem.UpdateSuccessFlags.RightHandJoints) == 0)
            return;

        if (head == null) return;

        var lf = HandFeatureDefs.Extract(s.leftHand, true);
        var rf = HandFeatureDefs.Extract(s.rightHand, false);
        if (!lf.tracked || !rf.tracked)
        {
            if (verbose && logToConsole) Debug.Log("[BSL] Hands not tracked.");
            return;
        }

        BSLTwoHandGesture best = null;
        float bestConf = 0f;
        int bestPri = int.MinValue;

        for (int i = 0; i < gestures.Length; i++)
        {
            var g = gestures[i];
            if (!g) continue;
            float c = g.Score(in lf, in rf, head);
            if (c > bestConf || (Mathf.Approximately(c, bestConf) && g.priority > bestPri))
            { best = g; bestConf = c; bestPri = g.priority; }
        }

        if (best == lastBest && bestConf >= acceptThreshold)
        {
            holdT += Time.deltaTime;
            lastConf = Mathf.Max(lastConf, bestConf);
        }
        else
        {
            lastBest = best;
            lastConf = bestConf;
            holdT = 0f;
        }

        if (lastBest != null && holdT >= stableSeconds && lastConf >= acceptThreshold)
        {
            string text = !string.IsNullOrEmpty(lastBest.displayName) ? lastBest.displayName
                          : (lastBest.letter != '\0' ? lastBest.letter.ToString() : "Gesture");

            if (output) output.text = showConfidence ? $"{text} ({lastConf:0.00})" : text;
            if (logToConsole) Debug.Log($"[BSL] {text} ({lastConf:0.00})");
        }
        else
        {
            if (verbose && output) output.text = ""; // clear when nothing stable
        }
    }
}
