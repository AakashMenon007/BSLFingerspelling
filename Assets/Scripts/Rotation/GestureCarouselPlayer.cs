using UnityEngine;
using UnityEngine.UI;

public class BSLCarouselSimple : MonoBehaviour
{
    [Header("Refs")]
    public XRHandRecorder player;     // assign your XRHandRecorder
    public BSLGestureSet set;         // ScriptableObject with 26 TextAssets
    public Button nextButton;         // hook in Inspector
    public Button prevButton;         // hook in Inspector

    [Header("Options")]
    public int startIndex = 0;        // start at A (0)
    public bool playOnStart = false;

    int _index;

    void Awake()
    {
        if (nextButton) nextButton.onClick.AddListener(Next);
        if (prevButton) prevButton.onClick.AddListener(Prev);
    }

    void Start()
    {
        _index = Mathf.Clamp(startIndex, 0, (set?.entries?.Count ?? 1) - 1);
        if (playOnStart) PlayIndex(_index);
    }

    void PlayIndex(int i)
    {
        if (set == null || set.entries == null || set.entries.Count == 0) return;
        _index = Mathf.Clamp(i, 0, set.entries.Count - 1);
        var entry = set.entries[_index];
        if (entry?.recording != null)
            player.PlayAsset(entry.recording, loop: false);
    }

    public void Next()
    {
        if (set == null || set.entries == null || set.entries.Count == 0) return;
        int i = (_index + 1) % set.entries.Count;
        PlayIndex(i);
    }

    public void Prev()
    {
        if (set == null || set.entries == null || set.entries.Count == 0) return;
        int i = (_index - 1 + set.entries.Count) % set.entries.Count;
        PlayIndex(i);
    }
}
