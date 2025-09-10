using System;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;

[CreateAssetMenu(fileName = "DualHandGestureAsset", menuName = "XR Hands/BSL/Dual Hand Gesture Asset", order = 0)]
public class DualHandGestureAsset : ScriptableObject
{
    [Serializable]
    public struct PoseSnapshot
    {
        public Vector3 worldPosition;
        public Quaternion worldRotation;

        // Relative to head (camera)
        public Vector3 headLocalPosition;
        public Quaternion headLocalRotation;

        // Relative to origin (XROrigin)
        public Vector3 originLocalPosition;
        public Quaternion originLocalRotation;
    }

    [Serializable]
    public struct FingerCurls
    {
        public float thumb;
        public float index;
        public float middle;
        public float ring;
        public float little;
    }

    [Serializable]
    public struct HandSnapshot
    {
        public Handedness handedness;

        // Key joints/poses
        public PoseSnapshot palmPose;
        public PoseSnapshot wristPose;

        // High-level axes (approx)
        public Vector3 palmForwardWS;   // -up of palm transform
        public Vector3 fingersDirWS;    // from palm to avg fingertip
        public Vector3 thumbDirWS;      // outward from thumb tip basis

        // Shape
        public FingerCurls curls;

        // Alignment conditions (from XR Hands Gestures)
        public XRHandAlignmentCondition palmVsHead;
        public XRHandAlignmentCondition palmVsOrigin;
        public XRHandAlignmentCondition thumbVsHead;
        public XRHandAlignmentCondition thumbVsOrigin;
        public XRHandAlignmentCondition fingersVsHead;

        // Useful distances
        public float palmToHeadDistance;
    }

    [Serializable]
    public struct InterHandFeatures
    {
        public float palmsDistance;
        public Vector3 palmsOffsetWS;          // rightPalm - leftPalm
        public Quaternion rightRelativeToLeft; // rotation from left palm to right palm
    }

    [Serializable]
    public struct Frame
    {
        public float time;
        public HandSnapshot left;
        public HandSnapshot right;
        public InterHandFeatures interHand;
    }

    [Header("Metadata")]
    public string gestureName;
    [TextArea] public string notes;

    [Header("Single frame (instant)")]
    public Frame singleFrame;

    [Header("Optional motion (captured while key is held)")]
    public Frame[] sequence;
}
