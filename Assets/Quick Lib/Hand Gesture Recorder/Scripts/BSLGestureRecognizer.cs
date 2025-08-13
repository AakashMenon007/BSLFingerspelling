using UnityEngine;
using TMPro;
using UnityEngine.XR.Hands.Samples.GestureSample;
using System.Collections.Generic;

public class BSLTwoHandGestureDisplay : MonoBehaviour
{
    [System.Serializable]
    public class BSLLetterLink
    {
        public string letter;
        public StaticHandGesture leftGesture;   // Required
        public StaticHandGesture rightGesture;  // Required
    }

    [Header("Gesture Configuration")]
    [SerializeField] private List<BSLLetterLink> letters = new List<BSLLetterLink>();

    [Header("Output Display")]
    [SerializeField] private TextMeshPro outputText;
    [SerializeField] private float displayDuration = 2f;

    private float displayTimer;

    // Track which gestures are currently active
    private Dictionary<StaticHandGesture, bool> gestureDetected = new Dictionary<StaticHandGesture, bool>();

    private void OnEnable()
    {
        foreach (var link in letters)
        {
            if (link.leftGesture != null)
            {
                gestureDetected[link.leftGesture] = false;
                link.leftGesture.gesturePerformed.AddListener(() => OnGesturePerformed(link.leftGesture));
                link.leftGesture.gestureEnded.AddListener(() => OnGestureEnded(link.leftGesture));
            }

            if (link.rightGesture != null)
            {
                gestureDetected[link.rightGesture] = false;
                link.rightGesture.gesturePerformed.AddListener(() => OnGesturePerformed(link.rightGesture));
                link.rightGesture.gestureEnded.AddListener(() => OnGestureEnded(link.rightGesture));
            }
        }
    }

    private void OnDisable()
    {
        foreach (var link in letters)
        {
            if (link.leftGesture != null)
            {
                link.leftGesture.gesturePerformed.RemoveAllListeners();
                link.leftGesture.gestureEnded.RemoveAllListeners();
            }

            if (link.rightGesture != null)
            {
                link.rightGesture.gesturePerformed.RemoveAllListeners();
                link.rightGesture.gestureEnded.RemoveAllListeners();
            }
        }
        gestureDetected.Clear();
    }

    private void OnGesturePerformed(StaticHandGesture gesture)
    {
        gestureDetected[gesture] = true;
        CheckForMatchingLetter();
    }

    private void OnGestureEnded(StaticHandGesture gesture)
    {
        gestureDetected[gesture] = false;
        CheckForMatchingLetter();
    }

    private void CheckForMatchingLetter()
    {
        foreach (var link in letters)
        {
            if (link.leftGesture != null && link.rightGesture != null)
            {
                bool leftActive = gestureDetected.ContainsKey(link.leftGesture) && gestureDetected[link.leftGesture];
                bool rightActive = gestureDetected.ContainsKey(link.rightGesture) && gestureDetected[link.rightGesture];

                if (leftActive && rightActive)
                {
                    ShowLetter(link.letter);
                    return; // Stop checking once we found the match
                }
            }
        }

        // No match found → clear the text
        outputText.text = "";
    }

    private void ShowLetter(string letter)
    {
        outputText.text = letter;
        displayTimer = displayDuration;
    }

    private void Update()
    {
        if (displayTimer > 0)
        {
            displayTimer -= Time.deltaTime;
            if (displayTimer <= 0)
                outputText.text = "";
        }
    }
}
