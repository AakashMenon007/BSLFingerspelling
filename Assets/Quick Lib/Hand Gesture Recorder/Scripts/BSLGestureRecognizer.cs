using UnityEngine;
using TMPro;
using UnityEngine.XR.Hands.Samples.GestureSample;
using System.Collections.Generic;
using UnityEngine.XR.Hands;

public class BSLTwoHandGestureDisplay : MonoBehaviour
{
    [System.Serializable]
    public class BSLLetterLink
    {
        public string letter;
        public StaticHandGesture leftGesture;
        public StaticHandGesture rightGesture;

        [Header("Distance Thresholds")]
        public float maxDistance = 0.15f; // overall allowed distance between palms

        [Header("Relative Position Requirement")]
        public bool requireLeftAboveRight = false;
        public bool requireRightAboveLeft = false;
        public bool requireHandsClose = true; // default proximity requirement
    }

    [Header("Gesture Configuration")]
    [SerializeField] private List<BSLLetterLink> letters = new List<BSLLetterLink>();

    [Header("Output Display")]
    [SerializeField] private TextMeshPro outputText;
    [SerializeField] private float displayDuration = 2f;

    [Header("XR Hands Reference")]
    [SerializeField] private XRHandSubsystem handSubsystem;

    private float displayTimer;
    private Dictionary<StaticHandGesture, bool> gestureDetected = new Dictionary<StaticHandGesture, bool>();

    private void Awake()
    {
        // Attempt to find and assign the hand subsystem if not manually assigned in inspector
        if (handSubsystem == null)
        {
            var subsystems = new List<XRHandSubsystem>();
            SubsystemManager.GetInstances(subsystems);
            if (subsystems.Count > 0)
                handSubsystem = subsystems[0];
            else
                Debug.LogError("XRHandSubsystem not found! Please assign one in the inspector.");
        }

        // Warn if outputText is missing
        if (outputText == null)
            Debug.LogError("outputText is not assigned! Assign a TextMeshPro component in the inspector.");
    }

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
        if (handSubsystem == null)
        {
            Debug.LogWarning("CheckForMatchingLetter: handSubsystem is null.");
            if (outputText != null) outputText.text = "";
            return;
        }

        XRHand leftHand = handSubsystem.leftHand;
        XRHand rightHand = handSubsystem.rightHand;

        if (leftHand == null || rightHand == null)
        {
            Debug.LogWarning("CheckForMatchingLetter: leftHand or rightHand is null.");
            if (outputText != null) outputText.text = "";
            return;
        }

        string matchedLetters = "";

        foreach (var link in letters)
        {
            if (link.leftGesture != null && link.rightGesture != null)
            {
                bool leftActive = gestureDetected.ContainsKey(link.leftGesture) && gestureDetected[link.leftGesture];
                bool rightActive = gestureDetected.ContainsKey(link.rightGesture) && gestureDetected[link.rightGesture];

                if (leftActive && rightActive && AreHandsWithinThreshold(leftHand, rightHand, link))
                {
                    matchedLetters += link.letter + " ";
                }
            }
        }

        if (outputText != null)
        {
            if (!string.IsNullOrEmpty(matchedLetters))
                ShowLetter(matchedLetters.Trim());
            else
                outputText.text = "";
        }
    }

    private bool AreHandsWithinThreshold(XRHand leftHand, XRHand rightHand, BSLLetterLink link)
    {
        if (leftHand == null || rightHand == null) return false;
        if (!leftHand.isTracked || !rightHand.isTracked) return false;

        XRHandJoint leftPalm = leftHand.GetJoint(XRHandJointID.Palm);
        XRHandJoint rightPalm = rightHand.GetJoint(XRHandJointID.Palm);

        if (!leftPalm.TryGetPose(out Pose leftPose) || !rightPalm.TryGetPose(out Pose rightPose))
            return false;

        Vector3 diff = leftPose.position - rightPose.position;
        float distance = diff.magnitude;

        // ✅ Check overall distance
        if (distance > link.maxDistance) return false;

        // ✅ Relative position checks
        if (link.requireLeftAboveRight && !(leftPose.position.y > rightPose.position.y))
            return false;

        if (link.requireRightAboveLeft && !(rightPose.position.y > leftPose.position.y))
            return false;

        // If "hands close" is required, ensure they're within the maxDistance already checked
        if (link.requireHandsClose && distance > link.maxDistance)
            return false;

        return true;
    }

    private void ShowLetter(string letter)
    {
        if (outputText != null)
        {
            outputText.text = letter;
            displayTimer = displayDuration;
        }
    }

    private void Update()
    {
        if (outputText == null)
            return;

        if (displayTimer > 0)
        {
            displayTimer -= Time.deltaTime;
            if (displayTimer <= 0)
                outputText.text = "";
        }
    }
}
