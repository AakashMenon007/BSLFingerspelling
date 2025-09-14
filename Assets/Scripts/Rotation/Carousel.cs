using System.Collections.Generic;
using UnityEngine;

public class CarouselSwitcher : MonoBehaviour
{
    [Tooltip("Assign your objects in display order. They will be grouped by 'groupSize'.")]
    public List<GameObject> items = new List<GameObject>();

    [Header("Startup")]
    [Tooltip("If true, uses the first ACTIVE object in any group as the start. Otherwise uses startGroupIndex.")]
    public bool detectActiveOnStart = true;
    [Tooltip("Used when detectActiveOnStart = false. This is the GROUP index, not the item index.")]
    public int startGroupIndex = 0;

    [Header("Behavior")]
    [Tooltip("Wrap around at ends (last -> first, first -> last).")]
    public bool loop = true;

    [Header("Grouping")]
    [Min(1)]
    [Tooltip("How many GameObjects should switch together as one 'slide'. Set to 2 for your case.")]
    public int groupSize = 2;

    // ---------- NEW: hook to XRHandPoseMatcherBSL ----------
    [Header("Hand Pose Matcher (optional)")]
    [Tooltip("If assigned, the matcher will be told which gesture is currently selected in the carousel.")]
    public XRHandPoseMatcherBSL matcher;

    [Tooltip("If ON, matcher recognizes ONLY the selected gesture. If OFF, it prioritizes the selection but can fall back.")]
    public bool onlySelected = true;

    [Tooltip("Optional mapping from GROUP index -> matcher gesture index. Leave empty to use groupIndex directly.")]
    public List<int> gestureIndexByGroup = new List<int>();

    int currentGroup = -1;

    int GroupCount => groupSize <= 0 ? 0 : (items.Count + groupSize - 1) / groupSize;

    void Start()
    {
        if (items.Count == 0 || groupSize <= 0) return;

        if (detectActiveOnStart)
        {
            currentGroup = FindFirstActiveGroupIndex();
            if (currentGroup < 0) currentGroup = Mathf.Clamp(startGroupIndex, 0, Mathf.Max(0, GroupCount - 1));
        }
        else
        {
            currentGroup = Mathf.Clamp(startGroupIndex, 0, Mathf.Max(0, GroupCount - 1));
        }

        // Ensure only the chosen group is active
        for (int g = 0; g < GroupCount; g++)
            SetGroupActive(g, g == currentGroup);

        // NEW: tell matcher which gesture is active now
        ApplyMatcherSelection();
    }

    public void Next()
    {
        if (GroupCount == 0) return;
        int next = currentGroup + 1;
        if (next >= GroupCount)
        {
            if (!loop) return;
            next = 0;
        }
        SwitchTo(next);
    }

    public void Previous()
    {
        if (GroupCount == 0) return;
        int prev = currentGroup - 1;
        if (prev < 0)
        {
            if (!loop) return;
            prev = GroupCount - 1;
        }
        SwitchTo(prev);
    }

    public void SwitchTo(int groupIndex)
    {
        if (groupIndex == currentGroup || groupIndex < 0 || groupIndex >= GroupCount) return;

        if (currentGroup >= 0 && currentGroup < GroupCount)
            SetGroupActive(currentGroup, false);

        currentGroup = groupIndex;
        SetGroupActive(currentGroup, true);

        // NEW: update matcher whenever slide changes
        ApplyMatcherSelection();
    }

    void SetGroupActive(int groupIndex, bool active)
    {
        int start = groupIndex * groupSize;
        int endExclusive = Mathf.Min(start + groupSize, items.Count);

        for (int i = start; i < endExclusive; i++)
            if (items[i]) items[i].SetActive(active);
    }

    int FindFirstActiveGroupIndex()
    {
        for (int g = 0; g < GroupCount; g++)
        {
            int start = g * groupSize;
            int endExclusive = Mathf.Min(start + groupSize, items.Count);
            for (int i = start; i < endExclusive; i++)
                if (items[i] && items[i].activeSelf) return g;
        }
        return -1;
    }

    // ---------- NEW: helper to drive the matcher ----------
    void ApplyMatcherSelection()
    {
        if (!matcher) return;

        var mode = onlySelected
            ? XRHandPoseMatcherBSL.RecognitionMode.OnlySelected
            : XRHandPoseMatcherBSL.RecognitionMode.PrioritizeSelected;
        matcher.SetRecognitionMode(mode);

        int gestureIndex = ResolveGestureIndex(currentGroup);
        matcher.SetSelectedGestureIndex(gestureIndex);
        // Note: XRHandPoseMatcherBSL clears its latch on selection change.
    }

    int ResolveGestureIndex(int groupIndex)
    {
        if (gestureIndexByGroup != null &&
            groupIndex >= 0 &&
            groupIndex < gestureIndexByGroup.Count &&
            gestureIndexByGroup[groupIndex] >= 0)
        {
            return gestureIndexByGroup[groupIndex];
        }
        // Default: assume gesture list order matches group order
        return groupIndex;
    }

    // Optional keyboard test
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.RightArrow)) Next();
        if (Input.GetKeyDown(KeyCode.LeftArrow)) Previous();
    }
}
