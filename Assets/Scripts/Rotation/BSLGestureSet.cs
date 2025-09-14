using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "BSLGestureSet", menuName = "XRHands/BSL Gesture Set")]
public class BSLGestureSet : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string name;          // e.g., "A", "B", ...
        public TextAsset recording;  // the .json saved in Assets/BSLGestures
    }

    public List<Entry> entries = new List<Entry>();

#if UNITY_EDITOR
    [ContextMenu("Sort Entries A→Z")]
    void SortAZ()
    {
        entries.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        EditorUtility.SetDirty(this);
    }
#endif
}
