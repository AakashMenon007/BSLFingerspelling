using UnityEngine;

public enum PalmRelation { Any, Parallel, Opposed, Perpendicular }
public enum VerticalOrder { RightAboveLeft, LeftAboveRight }
public enum DepthOrder { RightInFrontOfLeft, LeftInFrontOfRight }

[System.Serializable]
public class HandSignature
{
    [Header("Curls (0..1 targets)")]
    [Range(0, 1)] public float thumb = 0.5f;
    [Range(0, 1)] public float index = 0.5f;
    [Range(0, 1)] public float middle = 0.5f;
    [Range(0, 1)] public float ring = 0.5f;
    [Range(0, 1)] public float little = 0.5f;

    [Header("Tolerances (per finger)")]
    [Min(0)] public float tolThumb = 0.15f;
    [Min(0)] public float tolIndex = 0.15f;
    [Min(0)] public float tolMiddle = 0.15f;
    [Min(0)] public float tolRing = 0.15f;
    [Min(0)] public float tolLittle = 0.15f;

    [Header("Palm")]
    public bool usePalmNormal = false;
    public Vector3 expectedPalmNormal = Vector3.forward;
    [Range(0, 180)] public float palmTolDeg = 25f;

    public float Score(in HandFeatureDefs.Features f, bool observedIsLeft)
    {
        var targets = new float[5] { thumb, index, middle, ring, little };
        var tols = new float[5] { tolThumb, tolIndex, tolMiddle, tolRing, tolLittle };

        float curlErr = 0f;
        for (int i = 0; i < 5; i++)
        {
            float e = Mathf.Abs(f.curl[i] - targets[i]) / Mathf.Max(1e-4f, tols[i]);
            curlErr += Mathf.Clamp01(e);
        }
        curlErr /= 5f;

        float palmErr = 0f;
        if (usePalmNormal)
        {
            float ang = Vector3.Angle(f.palmNormal.normalized, expectedPalmNormal.normalized);
            palmErr = Mathf.Clamp01(ang / Mathf.Max(1f, palmTolDeg));
        }

        const float wCurl = 0.8f, wPalm = 0.2f;
        float totalErr = wCurl * curlErr + wPalm * palmErr;
        return 1f - Mathf.Clamp01(totalErr);
    }
}

[CreateAssetMenu(menuName = "Hands/BSL Two-Hand Gesture")]
public class BSLTwoHandGesture : ScriptableObject
{
    [Header("Identity")]
    public string displayName = "A";
    public char letter = 'A';
    public int priority = 0;

    [Header("Per-hand signatures")]
    public HandSignature left = new HandSignature();
    public HandSignature right = new HandSignature();

    [Header("Inter-hand constraints (head-local)")]
    public bool useVerticalOrder = false;
    public VerticalOrder verticalOrder = VerticalOrder.RightAboveLeft;
    [Tooltip("Minimum vertical separation (in units of avg hand scale).")]
    public float minVerticalSepNorm = 0.4f;

    public bool useDepthOrder = false;
    public DepthOrder depthOrder = DepthOrder.RightInFrontOfLeft;
    [Tooltip("Minimum depth separation (in units of avg hand scale). Positive means further forward.")]
    public float minDepthSepNorm = 0.2f;

    public bool useWristDistance = false;
    [Tooltip("Allowed wrist distance range (in units of avg hand scale).")]
    public float minWristDistNorm = 0.5f, maxWristDistNorm = 2.5f;

    [Header("Palm relation")]
    public PalmRelation palmRelation = PalmRelation.Any;
    [Range(0, 180)] public float palmRelationTolDeg = 25f;

    // Score 0..1 for the whole two-hand pose. head is used for head-local comparisons.
    public float Score(in HandFeatureDefs.Features leftF, in HandFeatureDefs.Features rightF, Transform head)
    {
        if (!leftF.tracked || !rightF.tracked || head == null) return 0f;

        // 1) Single-hand scores
        float sL = left.Score(in leftF, true);
        float sR = right.Score(in rightF, false);
        float handsScore = (sL + sR) * 0.5f;

        // Prepare head-local positions
        Vector3 lH = head.InverseTransformPoint(leftF.wristPos);
        Vector3 rH = head.InverseTransformPoint(rightF.wristPos);

        float avgScale = Mathf.Max(1e-3f, (leftF.scale + rightF.scale) * 0.5f);

        // 2) Inter-hand: vertical order
        float interScore = 1f;
        if (useVerticalOrder)
        {
            float dy = (rH.y - lH.y) / avgScale; // right minus left
            bool ok = verticalOrder == VerticalOrder.RightAboveLeft ? dy >= minVerticalSepNorm
                                                                    : (-dy) >= minVerticalSepNorm;
            if (!ok) interScore *= 0f;
        }

        // 3) Inter-hand: depth order (head forward is +z)
        if (useDepthOrder)
        {
            float dz = (rH.z - lH.z) / avgScale; // right minus left
            bool ok = depthOrder == DepthOrder.RightInFrontOfLeft ? dz >= minDepthSepNorm
                                                                  : (-dz) >= minDepthSepNorm;
            if (!ok) interScore *= 0f;
        }

        // 4) Wrist distance band (normalized)
        if (useWristDistance)
        {
            float distN = Vector3.Distance(lH, rH) / avgScale;
            if (distN < minWristDistNorm || distN > maxWristDistNorm) interScore *= 0f;
        }

        // 5) Palm relation
        if (palmRelation != PalmRelation.Any)
        {
            float angle = Vector3.Angle(leftF.palmNormal, rightF.palmNormal);
            float target = palmRelation == PalmRelation.Parallel ? 0f
                         : palmRelation == PalmRelation.Opposed ? 180f
                         : /*Perpendicular*/                            90f;
            float d = Mathf.Abs(angle - target);
            if (d > palmRelationTolDeg) interScore *= 0f;
        }

        // Combine (if any inter-hand fails, interScore will be zero)
        return Mathf.Clamp01(handsScore * interScore);
    }
}
