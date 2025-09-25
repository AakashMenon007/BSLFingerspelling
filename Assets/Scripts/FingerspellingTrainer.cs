using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class FingerspellingTrainer : MonoBehaviour
{
    [Header("Core")]
    public XRHandPoseMatcherBSL matcher;

    [Tooltip("Words to practice in order (will be uppercased).")]
    public List<string> words = new List<string> { "CAR", "BUS", "DOG" };

    [Tooltip("Automatically advance to next word after finishing.")]
    public bool autoAdvanceToNextWord = true;

    [Tooltip("Delay before switching to next word (seconds).")]
    public float nextWordDelaySeconds = 1.0f;

    [Tooltip("Shuffle words at Start().")]
    public bool shuffleWords = false;

    [Tooltip("Loop back to first word after the last.")]
    public bool loopWords = true;

    [Header("Spawn / Layout")]
    [Tooltip("Where to spawn the letter prefabs (a horizontal row).")]
    public Transform spawnParent;

    [Tooltip("Gap between letters (meters).")]
    public float letterSpacing = 0.45f;

    [Header("Prefab Libraries")]
    [Tooltip("GLOBAL library: default prefabs per letter (used if no per-word override).")]
    public List<LetterPrefab> letterLibrary = new List<LetterPrefab>();

    [Tooltip("PER-WORD overrides: use these prefabs for this specific word (optional).")]
    public List<WordPrefabSet> wordPrefabSets = new List<WordPrefabSet>();

    [Tooltip("If true and multiple prefabs exist for a letter, pick a random one.")]
    public bool randomizeLetterVariants = false;

    [Header("Visuals")]
    public TMP_Text progressText;                 // Shows • • • then C • • etc.
    public TMP_Text wordCounterText;              // e.g., 2 / 10
    public Material unmatchedMaterial;
    public Material matchedMaterial;

    [Tooltip("Hide the matcher's A-Z ghost meshes so only the row is visible.")]
    public bool hideMatchersGhostMeshesOnStart = true;

    // --- runtime state ---
    readonly List<GameObject> _spawned = new List<GameObject>();
    List<string> _playlist = new List<string>();
    int _wordIndex = 0;
    string _currentWord = "";
    char[] _progress;
    int _pos = 0;
    bool _advancingLetter = false;
    bool _advancingWord = false;

    // Fast lookups
    Dictionary<char, List<GameObject>> _globalMap = new Dictionary<char, List<GameObject>>();
    Dictionary<string, Dictionary<char, List<GameObject>>> _perWordMap =
        new Dictionary<string, Dictionary<char, List<GameObject>>>();

    void Awake()
    {
        if (!matcher)
        {
            Debug.LogError("[FingerspellingTrainer] matcher is not assigned.");
            enabled = false;
            return;
        }
    }

    void Start()
    {
        // Normalize & build playlist
        _playlist.Clear();
        foreach (var w in words)
        {
            var cleaned = (w ?? "").Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(cleaned)) _playlist.Add(cleaned);
        }
        if (_playlist.Count == 0)
        {
            Debug.LogError("[FingerspellingTrainer] No words provided.");
            return;
        }
        if (shuffleWords) Shuffle(_playlist);

        BuildGlobalMap();
        BuildPerWordMap();

        matcher.SetRecognitionMode(XRHandPoseMatcherBSL.RecognitionMode.OnlySelected);

        if (hideMatchersGhostMeshesOnStart)
            HideMatcherGhostMeshes();

        matcher.OnGestureLatched += OnMatcherLatched;

        LoadWord(0); // start with first
    }

    void OnDestroy()
    {
        if (matcher != null) matcher.OnGestureLatched -= OnMatcherLatched;
    }

    // ---------- Maps ----------
    void BuildGlobalMap()
    {
        _globalMap.Clear();
        foreach (var lp in letterLibrary)
        {
            if (string.IsNullOrEmpty(lp.letter) || !lp.prefab) continue;
            char c = char.ToUpperInvariant(lp.letter[0]);
            if (!_globalMap.TryGetValue(c, out var list))
            {
                list = new List<GameObject>();
                _globalMap[c] = list;
            }
            list.Add(lp.prefab);
        }
    }

    void BuildPerWordMap()
    {
        _perWordMap.Clear();
        foreach (var set in wordPrefabSets)
        {
            if (string.IsNullOrEmpty(set.word)) continue;
            string key = set.word.Trim().ToUpperInvariant();
            var map = new Dictionary<char, List<GameObject>>();
            foreach (var lp in set.prefabs)
            {
                if (string.IsNullOrEmpty(lp.letter) || !lp.prefab) continue;
                char c = char.ToUpperInvariant(lp.letter[0]);
                if (!map.TryGetValue(c, out var list))
                {
                    list = new List<GameObject>();
                    map[c] = list;
                }
                list.Add(lp.prefab);
            }
            _perWordMap[key] = map;
        }
    }

    // ---------- Word lifecycle ----------
    public void LoadWord(int index)
    {
        if (_advancingWord) return;
        if (_playlist.Count == 0) return;

        _wordIndex = Mathf.Clamp(index, 0, _playlist.Count - 1);
        _currentWord = _playlist[_wordIndex];

        BuildRowForWord(_currentWord);

        _progress = new char[_currentWord.Length];
        for (int i = 0; i < _progress.Length; i++) _progress[i] = '•';
        UpdateProgressText();
        UpdateCounterText();

        _pos = 0;
        GateMatcherToCurrentLetter();
    }

    public void NextWord()
    {
        if (_playlist.Count == 0) return;
        int next = _wordIndex + 1;
        if (next >= _playlist.Count)
        {
            if (!loopWords) return;
            next = 0;
        }
        LoadWord(next);
    }

    public void PreviousWord()
    {
        if (_playlist.Count == 0) return;
        int prev = _wordIndex - 1;
        if (prev < 0)
        {
            if (!loopWords) return;
            prev = _playlist.Count - 1;
        }
        LoadWord(prev);
    }

    void BuildRowForWord(string word)
    {
        // clear spawned
        for (int i = _spawned.Count - 1; i >= 0; i--) if (_spawned[i]) Destroy(_spawned[i]);
        _spawned.Clear();

        float startX = -(word.Length - 1) * 0.5f * letterSpacing;
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            var prefab = ResolvePrefabForLetter(word, c);
            if (!prefab)
            {
                Debug.LogError($"[FingerspellingTrainer] No prefab found for letter '{c}' (word '{word}').");
                continue;
            }

            var go = Instantiate(prefab, spawnParent);
            go.name = $"{c}_Slot{i}";
            go.transform.localPosition = new Vector3(startX + i * letterSpacing, 0f, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            ApplyMaterial(go, unmatchedMaterial);
            _spawned.Add(go);
        }
    }

    GameObject ResolvePrefabForLetter(string word, char letter)
    {
        letter = char.ToUpperInvariant(letter);
        string key = (word ?? "").Trim().ToUpperInvariant();

        // 1) Per-word override available?
        if (_perWordMap.TryGetValue(key, out var map) && map.TryGetValue(letter, out var listW) && listW.Count > 0)
        {
            return randomizeLetterVariants ? listW[UnityEngine.Random.Range(0, listW.Count)] : listW[0];
        }

        // 2) Global fallback
        if (_globalMap.TryGetValue(letter, out var listG) && listG.Count > 0)
        {
            return randomizeLetterVariants ? listG[UnityEngine.Random.Range(0, listG.Count)] : listG[0];
        }

        return null;
    }

    // ---------- Recognition flow ----------
    void OnMatcherLatched(int gestureIndex, string label)
    {
        if (_advancingLetter || _advancingWord) return;
        if (_pos >= _currentWord.Length) return;

        char expected = _currentWord[_pos];
        char got = (string.IsNullOrEmpty(label) ? '?' : char.ToUpperInvariant(label[0]));
        if (got != expected) return; // OnlySelected should guarantee this

        if (_pos < _spawned.Count) ApplyMaterial(_spawned[_pos], matchedMaterial);
        _progress[_pos] = expected;
        UpdateProgressText();

        StartCoroutine(AdvanceAfterHold());
    }

    IEnumerator AdvanceAfterHold()
    {
        _advancingLetter = true;

        float wait = Mathf.Max(0.01f, matcher.recognitionLatchSeconds);
        yield return new WaitForSeconds(wait);

        _pos++;

        if (_pos >= _currentWord.Length)
        {
            StartCoroutine(HandleWordCompleted());
        }
        else
        {
            GateMatcherToCurrentLetter();
        }

        _advancingLetter = false;
    }

    IEnumerator HandleWordCompleted()
    {
        _advancingWord = true;

        // Ensure the full word is visible
        if (progressText) progressText.text = _currentWord;

        if (autoAdvanceToNextWord)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, nextWordDelaySeconds));
            int next = _wordIndex + 1;
            if (next >= _playlist.Count)
            {
                if (!loopWords) { _advancingWord = false; yield break; }
                next = 0;
            }
            LoadWord(next);
        }

        _advancingWord = false;
    }

    void GateMatcherToCurrentLetter()
    {
        char c = _currentWord[_pos];
        int gestureIndex = CharToGestureIndex(c); // A=0..25
        matcher.SetSelectedGestureIndex(gestureIndex);
    }

    // ---------- UI helpers ----------
    void UpdateProgressText()
    {
        if (!progressText) return;
        var parts = new List<string>(_progress.Length);
        for (int i = 0; i < _progress.Length; i++) parts.Add(_progress[i].ToString());
        progressText.text = string.Join(" ", parts);
    }

    void UpdateCounterText()
    {
        if (!wordCounterText) return;
        wordCounterText.text = $"{_wordIndex + 1} / {_playlist.Count}";
    }

    // ---------- Utility ----------
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

    int CharToGestureIndex(char c)
    {
        c = char.ToUpperInvariant(c);
        return Mathf.Clamp(c - 'A', 0, 25);
    }

    void HideMatcherGhostMeshes()
    {
        foreach (var g in matcher.gestures)
        {
            if (g.targetLeft && g.targetLeft.skinnedMesh) g.targetLeft.skinnedMesh.enabled = false;
            if (g.targetRight && g.targetRight.skinnedMesh) g.targetRight.skinnedMesh.enabled = false;
        }
    }

    void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    [Serializable]
    public class LetterPrefab
    {
        [Tooltip("Single character, e.g., A, B, C ...")]
        public string letter = "A";
        [Tooltip("Prefab with the letter’s ghost hands (visual only).")]
        public GameObject prefab;
    }

    [Serializable]
    public class WordPrefabSet
    {
        [Tooltip("Word this override applies to (UPPERCASE recommended).")]
        public string word = "CAR";
        [Tooltip("Per-letter prefab overrides for THIS word.")]
        public List<LetterPrefab> prefabs = new List<LetterPrefab>();
    }
}
