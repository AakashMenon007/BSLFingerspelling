using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Hands.Gestures;

namespace UnityEngine.XR.Hands.Samples.GestureSample
{
    /// <summary>
    /// A gesture that detects when a hand is held in a static shape and orientation for a minimum amount of time.
    /// </summary>
    public class StaticHandGesture : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The hand tracking events component to subscribe to receive updated joint data to be used for gesture detection.")]
        XRHandTrackingEvents m_HandTrackingEvents;

        [SerializeField]
        [Tooltip("The hand shape or pose that must be detected for the gesture to be performed.")]
        ScriptableObject m_HandShapeOrPose;

        [SerializeField]
        [Tooltip("The target Transform to user for target conditions in the hand shape or pose.")]
        Transform m_TargetTransform;

        [SerializeField]
        [Tooltip("The image component that draws the background for gesture icons.")]
        Image m_Background;

        [SerializeField]
        [Tooltip("The event fired when the gesture is performed.")]
        UnityEvent m_GesturePerformed;

        [SerializeField]
        [Tooltip("The event fired when the gesture is ended.")]
        UnityEvent m_GestureEnded;

        [SerializeField]
        [Tooltip("The minimum amount of time the hand must be held in the required shape and orientation for the gesture to be performed.")]
        float m_MinimumHoldTime = 0.2f;

        [SerializeField]
        [Tooltip("The interval at which the gesture detection is performed.")]
        float m_GestureDetectionInterval = 0.1f;

        [SerializeField]
        [Tooltip("The static gestures associated with this gesture's handedness.")]
        StaticHandGesture[] m_StaticGestures;

        [SerializeField]
        [Tooltip("The image component that draws the highlighted gesture icon border.")]
        Image m_Highlight;

        XRHandShape m_HandShape;
        XRHandPose m_HandPose;
        bool m_WasDetected;
        bool m_PerformedTriggered;
        float m_TimeOfLastConditionCheck;
        float m_HoldStartTime;
        Color m_BackgroundDefaultColor;
        Color m_BackgroundHighlightColor = new Color(0f, 0.627451f, 1f);

        public XRHandTrackingEvents handTrackingEvents
        {
            get => m_HandTrackingEvents;
            set => m_HandTrackingEvents = value;
        }

        public ScriptableObject handShapeOrPose
        {
            get => m_HandShapeOrPose;
            set => m_HandShapeOrPose = value;
        }

        public Transform targetTransform
        {
            get => m_TargetTransform;
            set => m_TargetTransform = value;
        }

        public Image background
        {
            get => m_Background;
            set => m_Background = value;
        }

        public Image highlight
        {
            get => m_Highlight;
            set => m_Highlight = value;
        }

        public UnityEvent gesturePerformed
        {
            get => m_GesturePerformed;
            set => m_GesturePerformed = value;
        }

        public UnityEvent gestureEnded
        {
            get => m_GestureEnded;
            set => m_GestureEnded = value;
        }

        public float minimumHoldTime
        {
            get => m_MinimumHoldTime;
            set => m_MinimumHoldTime = value;
        }

        public float gestureDetectionInterval
        {
            get => m_GestureDetectionInterval;
            set => m_GestureDetectionInterval = value;
        }

        public bool highlightVisible
        {
            set
            {
                if (m_Highlight != null)
                    m_Highlight.enabled = value;
            }
        }

        void Awake()
        {
            if (m_Background != null)
                m_BackgroundDefaultColor = m_Background.color;

            if (m_Highlight != null)
            {
                m_Highlight.enabled = false;
                m_Highlight.gameObject.SetActive(true);
            }
        }

        void OnEnable()
        {
            if (m_HandTrackingEvents != null)
            {
                m_HandTrackingEvents.jointsUpdated.AddListener(OnJointsUpdated);
            }
            else
            {
                Debug.LogError($"[StaticHandGesture] Missing XRHandTrackingEvents reference on '{name}'. Gesture detection disabled.");
                enabled = false;
                return;
            }

            m_HandShape = m_HandShapeOrPose as XRHandShape;
            m_HandPose = m_HandShapeOrPose as XRHandPose;

            if (m_HandPose != null && m_HandPose.relativeOrientation != null)
                m_HandPose.relativeOrientation.targetTransform = m_TargetTransform;
        }

        void OnDisable()
        {
            if (m_HandTrackingEvents != null)
                m_HandTrackingEvents.jointsUpdated.RemoveListener(OnJointsUpdated);
        }

        void OnJointsUpdated(XRHandJointsUpdatedEventArgs eventArgs)
        {
            // Safety checks
            if (m_HandTrackingEvents == null || m_Background == null)
                return;

            if (!isActiveAndEnabled || Time.timeSinceLevelLoad < m_TimeOfLastConditionCheck + m_GestureDetectionInterval)
                return;

            bool detected = false;

            // Only check conditions if the hand is tracked
            if (m_HandTrackingEvents.handIsTracked)
            {
                if (m_HandShape != null && m_HandShape.CheckConditions(eventArgs))
                    detected = true;
                else if (m_HandPose != null && m_HandPose.CheckConditions(eventArgs))
                    detected = true;
            }

            if (!m_WasDetected && detected)
            {
                m_HoldStartTime = Time.timeSinceLevelLoad;
            }
            else if (m_WasDetected && !detected)
            {
                m_PerformedTriggered = false;
                m_GestureEnded?.Invoke();
                m_Background.color = m_BackgroundDefaultColor;
            }

            m_WasDetected = detected;

            // Trigger gesture performed event if held long enough
            if (!m_PerformedTriggered && detected)
            {
                var holdTimer = Time.timeSinceLevelLoad - m_HoldStartTime;
                if (holdTimer > m_MinimumHoldTime)
                {
                    m_GesturePerformed?.Invoke();
                    m_PerformedTriggered = true;
                    m_Background.color = m_BackgroundHighlightColor;

                    if (m_Highlight != null)
                        m_Highlight.enabled = true;

                    if (m_StaticGestures != null)
                    {
                        foreach (var gesture in m_StaticGestures)
                        {
                            if (gesture != null && gesture != this)
                                gesture.highlightVisible = false;
                        }
                    }
                }
            }

            m_TimeOfLastConditionCheck = Time.timeSinceLevelLoad;
        }
    }
}
