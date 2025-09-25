using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class SequentialFingerspelling : MonoBehaviour
{
    [Header("Matcher (XR Hands)")]
    public XRHandPoseMatcherBSL matcher;

    public enum CounterStyle { Bullets, ProgressiveLetters, Letters, Words }

    [Header("Display")]
    [Tooltip("Bullets: '• • •'. ProgressiveLetters: 'C • •' (bullets flip to letters as recognized). Letters: 'i / N'. Words: 'wordIndex / wordCount'.")]
    public CounterStyle counterStyle = CounterStyle.ProgressiveLetters;

    [System.Serializable]
    public class WordSetup
    {
        public string word = "CAR";
        public GameObject wordRoot;
        public List<GameObject> letterSlots = new List<GameObject>();
        public GameObject imageForWord;
    }

    [Header("Playlist")]
    public List<WordSetup> words = new List<WordSetup>();
    public bool autoAdvanceToNextWord = true;
    [Min(0f)] public float nextWordDelay = 1.0f;
    public bool loopWords = true;

    [Header("Visuals")]
    public TMP_Text progressText;     // cumulative text: C → CA → CAR (optional; can be left empty)
    public TMP_Text wordCounterText;  // bullets / progressive letters / numbers based on counterStyle
    public Material unmatchedMaterial;
    public Material matchedMaterial;

    [Header("Letter pacing")]
    [Min(0f)] public float nextLetterDelay = 0.15f;

    // --- runtime state ---
    int _wordIndex = 0;
    int _pos = 0;
    char[] _progress;             // per-letter display: starts '•' ... replaced with real letters when matched
    WordSetup _current;
    bool _advancingLetter = false;
    bool _advancingWord = false;
    int _lastLatchedFrame = -9999;

    // Sticky cumulative text we will keep asserting
    string _builtWord = "";
    bool _forceRefresh = false;

    void Awake()
    {
        if (!matcher)
        {
            Debug.LogError("[SequentialFingerspelling] matcher is not assigned.");
            enabled = false;
            return;
        }
    }

    void OnEnable() { matcher.OnGestureLatched += OnGestureLatched; }
    void OnDisable() { matcher.OnGestureLatched -= OnGestureLatched; }

    void Start()
    {
        if (words == null || words.Count == 0)
        {
            Debug.LogError("[SequentialFingerspelling] No words configured.");
            return;
        }

        // Ensure matcher doesn't fight our TMP
        matcher.SetRecognitionMode(XRHandPoseMatcherBSL.RecognitionMode.OnlySelected);
        matcher.suppressRecognizedTextOutput = true;

        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (w != null && !string.IsNullOrWhiteSpace(w.word))
                w.word = w.word.Trim().ToUpperInvariant();
            SetWordActive(w, false);
        }

        LoadWord(0);
    }

    // ------------ Core flow ------------
    public void LoadWord(int index)
    {
        if (index < 0 || index >= words.Count) return;

        // turn OFF all first
        for (int i = 0; i < words.Count; i++)
            SetWordActive(words[i], false);

        _wordIndex = index;
        _current = words[_wordIndex];
        if (_current == null || string.IsNullOrEmpty(_current.word))
        {
            Debug.LogError("[SequentialFingerspelling] Invalid word entry.");
            return;
        }

        SetWordActive(_current, true);
        SetSlotsMaterial(_current.letterSlots, unmatchedMaterial);

        // per-letter tracker starts as bullets
        _progress = new char[_current.word.Length];
        for (int i = 0; i < _progress.Length; i++) _progress[i] = '•';

        // Reset cumulative word & text
        _builtWord = "";
        _forceRefresh = true;
        _pos = 0;
        if (progressText) progressText.text = _builtWord;

        UpdateCounterText();       // shows bullets or placeholders for the whole word
        GateMatcherToCurrentLetter();

        if (_current.letterSlots.Count < _current.word.Length)
            Debug.LogWarning($"[SequentialFingerspelling] Fewer slots ({_current.letterSlots.Count}) than letters ({_current.word.Length}) for '{_current.word}'.");
    }

    public void NextWord()
    {
        int next = _wordIndex + 1;
        if (next >= words.Count)
        {
            if (!loopWords) return;
            next = 0;
        }
        LoadWord(next);
    }

    public void PreviousWord()
    {
        int prev = _wordIndex - 1;
        if (prev < 0)
        {
            if (!loopWords) return;
            prev = words.Count - 1;
        }
        LoadWord(prev);
    }

    public void RestartCurrentWord() => LoadWord(_wordIndex);

    // ------------ Matcher callback ------------
    void OnGestureLatched(int gestureIndex, string label)
    {
        // debounce multi-fire per frame
        if (Time.frameCount == _lastLatchedFrame) return;
        _lastLatchedFrame = Time.frameCount;

        if (_advancingLetter || _advancingWord) return;
        if (_current == null || _pos >= _current.word.Length) return;

        char expected = _current.word[_pos];
        char got = (!string.IsNullOrEmpty(label)) ? char.ToUpperInvariant(label[0]) : '?';
        if (got != expected) return;

        if (_pos < _current.letterSlots.Count)
            ApplyMaterial(_current.letterSlots[_pos], matchedMaterial);

        // 1) Cumulative text: append and pin
        _builtWord += expected;
        _forceRefresh = true;

        // 2) Progressive counter: flip this slot from • to the actual letter
        _progress[_pos] = expected;
        UpdateCounterText(); // update immediately so you see the flip this frame

        StartCoroutine(AdvanceNextLetter());
    }

    IEnumerator AdvanceNextLetter()
    {
        _advancingLetter = true;
        if (nextLetterDelay > 0f) yield return new WaitForSeconds(nextLetterDelay);

        _pos++;

        // If you use numerical letter counters, refresh here too
        UpdateCounterText();

        if (_pos >= _current.word.Length)
            StartCoroutine(HandleWordCompleted());
        else
            GateMatcherToCurrentLetter();

        _advancingLetter = false;
    }

    IEnumerator HandleWordCompleted()
    {
        _advancingWord = true;

        // Keep full word visible; do NOT overwrite progressText here.
        if (autoAdvanceToNextWord)
        {
            if (nextWordDelay > 0f) yield return new WaitForSeconds(nextWordDelay);
            NextWord();
        }

        _advancingWord = false;
    }

    // ------------ Helpers ------------
    void GateMatcherToCurrentLetter()
    {
        // Skip non A–Z defensively
        while (_pos < _current.word.Length && !IsAZ(_current.word[_pos]))
        {
            _builtWord += _current.word[_pos]; // reveal non-letter immediately
            _progress[_pos] = _current.word[_pos];
            _pos++;
            _forceRefresh = true;
            UpdateCounterText();
        }

        if (_pos >= _current.word.Length)
        {
            StartCoroutine(HandleWordCompleted());
            return;
        }

        int gestureIndex = Mathf.Clamp(_current.word[_pos] - 'A', 0, 25);
        matcher.SetSelectedGestureIndex(gestureIndex);
    }

    static bool IsAZ(char c) => c >= 'A' && c <= 'Z';

    void LateUpdate()
    {
        // Hard-pin the cumulative text so nothing else can clear it
        if (progressText && (progressText.text != _builtWord || _forceRefresh))
        {
            progressText.text = _builtWord;
            _forceRefresh = false;
        }
    }

    void UpdateCounterText()
    {
        if (!wordCounterText) return;

        switch (counterStyle)
        {
            case CounterStyle.Bullets:
                {
                    int total = _current != null ? _current.word.Length : 0;
                    if (total <= 0) { wordCounterText.text = ""; return; }
                    var parts = new List<string>(total);
                    for (int i = 0; i < total; i++) parts.Add("•");
                    wordCounterText.text = string.Join(" ", parts);
                    break;
                }

            case CounterStyle.ProgressiveLetters:
                {
                    // Show exactly one slot per letter: '•' for not-yet, actual letter for matched
                    int total = _current != null ? _current.word.Length : 0;
                    if (total <= 0) { wordCounterText.text = ""; return; }

                    var parts = new List<string>(total);
                    for (int i = 0; i < total; i++)
                    {
                        char c = (i < _progress.Length) ? _progress[i] : '•';
                        parts.Add(c.ToString());
                    }
                    wordCounterText.text = string.Join(" ", parts);
                    break;
                }

            case CounterStyle.Letters:
                {
                    int total = _current != null ? _current.word.Length : 0;
                    int currentLetterOneBased = Mathf.Clamp(_pos + 1, 1, Mathf.Max(1, total));
                    if (total <= 0) wordCounterText.text = "";
                    else wordCounterText.text = $"{currentLetterOneBased} / {total}";
                    break;
                }

            case CounterStyle.Words:
            default:
                {
                    wordCounterText.text = $"{_wordIndex + 1} / {Mathf.Max(1, words.Count)}";
                    break;
                }
        }
    }

    void SetWordActive(WordSetup w, bool active)
    {
        if (w == null) return;

        if (w.wordRoot)
        {
            w.wordRoot.SetActive(active);
            if (w.imageForWord && w.imageForWord.transform.IsChildOf(w.wordRoot.transform) == false)
                w.imageForWord.SetActive(active);
        }
        else
        {
            foreach (var go in w.letterSlots)
                if (go) go.SetActive(active);
            if (w.imageForWord) w.imageForWord.SetActive(active);
        }
    }

    void SetSlotsMaterial(List<GameObject> slots, Material mat)
    {
        if (slots == null) return;
        foreach (var go in slots) ApplyMaterial(go, mat);
    }

    void ApplyMaterial(GameObject root, Material mat)
    {
        if (!root || !mat) return;
        var rends = root.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends)
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }
    }
}

