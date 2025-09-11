using System.Collections.Generic;
using UnityEngine;

public class CarouselSwitcher : MonoBehaviour
{
    [Tooltip("Assign your objects in display order. Only one will be active at a time.")]
    public List<GameObject> items = new List<GameObject>();

    [Header("Startup")]
    [Tooltip("If true, uses the first ACTIVE object in the list as the start. Otherwise uses startIndex.")]
    public bool detectActiveOnStart = true;
    [Tooltip("Used when detectActiveOnStart = false.")]
    public int startIndex = 0;

    [Header("Behavior")]
    [Tooltip("Wrap around at ends (last -> first, first -> last).")]
    public bool loop = true;

    int current = -1;

    void Start()
    {
        if (items.Count == 0) return;

        if (detectActiveOnStart)
        {
            current = items.FindIndex(go => go && go.activeSelf);
            if (current < 0) current = Mathf.Clamp(startIndex, 0, items.Count - 1);
            // Ensure only the chosen one is active
            for (int i = 0; i < items.Count; i++)
                if (items[i]) items[i].SetActive(i == current);
        }
        else
        {
            current = Mathf.Clamp(startIndex, 0, items.Count - 1);
            for (int i = 0; i < items.Count; i++)
                if (items[i]) items[i].SetActive(i == current);
        }
    }

    public void Next()
    {
        if (items.Count == 0) return;
        int next = current + 1;
        if (next >= items.Count)
        {
            if (!loop) return;
            next = 0;
        }
        SwitchTo(next);
    }

    public void Previous()
    {
        if (items.Count == 0) return;
        int prev = current - 1;
        if (prev < 0)
        {
            if (!loop) return;
            prev = items.Count - 1;
        }
        SwitchTo(prev);
    }

    void SwitchTo(int index)
    {
        if (index == current || index < 0 || index >= items.Count) return;

        if (current >= 0 && current < items.Count && items[current])
            items[current].SetActive(false);

        current = index;

        if (items[current])
            items[current].SetActive(true);
    }

    // Optional keyboard test
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.RightArrow)) Next();
        if (Input.GetKeyDown(KeyCode.LeftArrow)) Previous();
    }
}
