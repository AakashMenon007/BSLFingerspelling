using System.Collections.Generic;
using System.IO;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;

// Editor-only API guarded
#if UNITY_EDITOR
using UnityEditor;
#endif

public class XRHandGestureRecorder : MonoBehaviour
{
    #region Enumerations
    public enum HandBoneOrientation
    {
        None,
        Front,
        Back,
        Left,
        Right,
        Up,
        Down
    };
    #endregion

    #region Public Variables
    [Tooltip("The key on the keyboard to record the current hand gesture.")]
    public KeyCode keycodeToRecord = KeyCode.R;

    [Tooltip("The path to save the hand shape and hand pose (Editor only).")]
    public string folderPathToSave = "Assets/CustomXRGestures";

    [Tooltip("The name of the hand gesture to create.")]
    public string handGestureName = "customHandGesture";

    [Tooltip("Do you need a hand pose with orientation conditions?")]
    public bool UseOrientationConditions = true;
    #endregion

    #region Private Variables
    [SerializeField]
    [Tooltip("The handedness to get the finger states for.")]
    Handedness m_Handedness = Handedness.Right;

    private float upperTolerance = 0.25f;
    private float lowerTolerance = 0.25f;
    private Transform originTransfrom;
    private Transform headTransfrom;
    private float angleTolerance = 60;
    private string handShapeFolderPath;
    private string handPoseFolderPath;
    private XRHandSubsystem subsystem;
    private XRHand hand;
    private XRFingerShape[] m_XRFingerShapes;
    private static List<XRHandSubsystem> s_SubsystemsReuse = new List<XRHandSubsystem>();
    private XRHandShape handShapeCreated = null;
    private XRHandPose xRHandPose = null;

    // Editor-only fields (avoid warnings + player build references)
#if UNITY_EDITOR
    private ScriptableObject handShape_SO = null;
    private ScriptableObject handPose_SO = null;
#endif
    #endregion

    #region Internal Methods
    private void Start()
    {
        m_XRFingerShapes = new XRFingerShape[(int)XRHandFingerID.Little - (int)XRHandFingerID.Thumb + 1];

        var origin = GameObject.FindObjectOfType<XROrigin>();
        if (origin != null)
            originTransfrom = origin.transform;

        if (Camera.main != null)
            headTransfrom = Camera.main.transform;
    }

    void Update()
    {
        // Get Hands info
        subsystem = TryGetSubsystem();
        if (subsystem == null)
            return;

        // Record Gesture
        if (Input.GetKeyDown(keycodeToRecord))
        {
            // Verify paths exist - Create if they don't
            HandlePaths();

            // Create hand shape
            CreateHandShape();

            // Create hand pose
            if (UseOrientationConditions)
                CreateHandPose();
        }
    }
    #endregion

    #region Main Methods
    // Get or create the folder paths to save hand shape and pose.
    private void HandlePaths()
    {
#if UNITY_EDITOR
        if (!Directory.Exists(folderPathToSave))
            Directory.CreateDirectory(folderPathToSave);

        handShapeFolderPath = folderPathToSave + "/Hand Shapes/";
        if (!Directory.Exists(handShapeFolderPath))
            Directory.CreateDirectory(handShapeFolderPath);

        handPoseFolderPath = folderPathToSave + "/Hand Poses/";
        if (!Directory.Exists(handPoseFolderPath))
            Directory.CreateDirectory(handPoseFolderPath);
#else
        // In player builds, we can’t write into Assets/. Use persistentDataPath if you need runtime persistence.
        handShapeFolderPath = Application.persistentDataPath + "/HandShapes/";
        handPoseFolderPath = Application.persistentDataPath + "/HandPoses/";
        if (!Directory.Exists(handShapeFolderPath)) Directory.CreateDirectory(handShapeFolderPath);
        if (!Directory.Exists(handPoseFolderPath)) Directory.CreateDirectory(handPoseFolderPath);
#endif
    }

    private void CreateHandShape()
    {
        XRHandShape xRHandShape = new XRHandShape();
        string handShapeName = handGestureName + "_Shape";
        xRHandShape.name = handShapeName;
        xRHandShape.fingerShapeConditions = new List<XRFingerShapeCondition>();

        // Get current hand
        hand = m_Handedness == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;

        for (var fingerIndex = (int)XRHandFingerID.Thumb;
                 fingerIndex <= (int)XRHandFingerID.Little;
                 ++fingerIndex)
        {
            m_XRFingerShapes[fingerIndex] = hand.CalculateFingerShape(
                (XRHandFingerID)fingerIndex, XRFingerShapeTypes.FullCurl);

            UpdateFingerCondition(fingerIndex, xRHandShape);
        }

#if UNITY_EDITOR
        // Save as asset in Editor
        string assetPath = handShapeFolderPath + "/" + handShapeName + ".asset";
        AssetDatabase.CreateAsset(xRHandShape, assetPath);
        handShape_SO = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
        AssetDatabase.Refresh();
        handShapeCreated = AssetDatabase.LoadAssetAtPath(assetPath, typeof(XRHandShape)) as XRHandShape;

        Debug.Log("Hand Shape created (Editor asset)!");
#else
        // Player build: keep in memory (not saved as .asset)
        handShapeCreated = xRHandShape;
        Debug.Log("Hand Shape created (runtime only, not saved as asset).");
#endif
    }

    private void CreateHandPose()
    {
        xRHandPose = new XRHandPose();
        string handPoseName = handGestureName + "_Pose";
        xRHandPose.name = handPoseName;

        // Set the handshape previously created
        xRHandPose.handShape = handShapeCreated;

        // Create user conditions for each hand axis
        XRHandRelativeOrientation.UserCondition userConditionsPalmOrigin = new XRHandRelativeOrientation.UserCondition();
        XRHandRelativeOrientation.UserCondition userConditionsPalmHead = new XRHandRelativeOrientation.UserCondition();
        XRHandRelativeOrientation.UserCondition userConditionsThumbOrigin = new XRHandRelativeOrientation.UserCondition();
        XRHandRelativeOrientation.UserCondition userConditionsThumbHead = new XRHandRelativeOrientation.UserCondition();
        XRHandRelativeOrientation.UserCondition userConditionsFingers = new XRHandRelativeOrientation.UserCondition();

        // Hand axis for each user condition
        userConditionsPalmOrigin.handAxis = XRHandAxis.PalmDirection;
        userConditionsPalmHead.handAxis = XRHandAxis.PalmDirection;
        userConditionsThumbOrigin.handAxis = XRHandAxis.ThumbExtendedDirection;
        userConditionsThumbHead.handAxis = XRHandAxis.ThumbExtendedDirection;
        userConditionsFingers.handAxis = XRHandAxis.FingersExtendedDirection;

        // Reference directions
        userConditionsPalmOrigin.referenceDirection = XRHandUserRelativeDirection.OriginUp;
        userConditionsPalmHead.referenceDirection = XRHandUserRelativeDirection.HandToHead;
        userConditionsThumbOrigin.referenceDirection = XRHandUserRelativeDirection.OriginUp;
        userConditionsThumbHead.referenceDirection = XRHandUserRelativeDirection.HandToHead;
        userConditionsFingers.referenceDirection = XRHandUserRelativeDirection.HandToHead;

        // Detect the correct Alignment Condition and reference direction
        userConditionsPalmOrigin = SetDirectionAndAligment(userConditionsPalmOrigin);
        userConditionsPalmHead = SetDirectionAndAligment(userConditionsPalmHead);
        userConditionsThumbOrigin = SetDirectionAndAligment(userConditionsThumbOrigin);
        userConditionsThumbHead = SetDirectionAndAligment(userConditionsThumbHead);
        userConditionsFingers = SetDirectionAndAligment(userConditionsFingers);

        List<XRHandRelativeOrientation.UserCondition> userConditionsList = new List<XRHandRelativeOrientation.UserCondition>();
        if (userConditionsPalmOrigin != null) userConditionsList.Add(userConditionsPalmOrigin);
        if (userConditionsPalmHead != null) userConditionsList.Add(userConditionsPalmHead);
        if (userConditionsThumbOrigin != null) userConditionsList.Add(userConditionsThumbOrigin);
        if (userConditionsThumbHead != null) userConditionsList.Add(userConditionsThumbHead);
        if (userConditionsFingers != null) userConditionsList.Add(userConditionsFingers);

        XRHandRelativeOrientation relativeOrientation = new XRHandRelativeOrientation();
        relativeOrientation.userConditions = userConditionsList.ToArray();
        xRHandPose.relativeOrientation = relativeOrientation;

#if UNITY_EDITOR
        // Save as asset in Editor
        string assetPath = handPoseFolderPath + "/" + handPoseName + ".asset";
        AssetDatabase.CreateAsset(xRHandPose, assetPath);
        handPose_SO = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
        AssetDatabase.Refresh();
        Debug.Log("Hand Pose created (Editor asset)!");
#else
        // Player build: keep in memory (not saved as .asset)
        Debug.Log("Hand Pose created (runtime only, not saved as asset).");
#endif
    }
    #endregion

    #region Secondary private methods
    // Set the fingers states/curls inside the handshape
    private void UpdateFingerCondition(int fingerIndex, XRHandShape handShape)
    {
        XRFingerShapeCondition fingerCondition = new XRFingerShapeCondition();
        XRFingerShape shapes = m_XRFingerShapes[fingerIndex];

        // FingerID
        if (fingerIndex == (int)XRHandFingerID.Thumb)
            fingerCondition.fingerID = UnityEngine.XR.Hands.XRHandFingerID.Thumb;
        else if (fingerIndex == (int)XRHandFingerID.Index)
            fingerCondition.fingerID = UnityEngine.XR.Hands.XRHandFingerID.Index;
        else if (fingerIndex == (int)XRHandFingerID.Middle)
            fingerCondition.fingerID = UnityEngine.XR.Hands.XRHandFingerID.Middle;
        else if (fingerIndex == (int)XRHandFingerID.Ring)
            fingerCondition.fingerID = UnityEngine.XR.Hands.XRHandFingerID.Ring;
        else if (fingerIndex == (int)XRHandFingerID.Little)
            fingerCondition.fingerID = UnityEngine.XR.Hands.XRHandFingerID.Little;

        // Targets Values
        int nbTargets = 0;
        float fullCurl = -1;
        if (shapes.TryGetFullCurl(out fullCurl))
            nbTargets++;

        int targetIndex = 0;
        fingerCondition.targets = new XRFingerShapeCondition.Target[nbTargets];
        if (fullCurl != -1)
        {
            fingerCondition.targets[targetIndex].shapeType = XRFingerShapeType.FullCurl;
            fingerCondition.targets[targetIndex].upperTolerance = upperTolerance;
            fingerCondition.targets[targetIndex].lowerTolerance = lowerTolerance;
            fingerCondition.targets[targetIndex].desired = fullCurl;
        }
        handShape.fingerShapeConditions.Add(fingerCondition);
    }

    // Update direction and alignment of hand pose for palm, thumb and fingers
    private XRHandRelativeOrientation.UserCondition SetDirectionAndAligment(XRHandRelativeOrientation.UserCondition userCondition)
    {
        List<JointToTransformReference> jointsTransforms = new List<JointToTransformReference>();
        if (m_Handedness == Handedness.Left)
            jointsTransforms = GameObject.Find("Left Hand Tracking").GetComponent<XRHandSkeletonDriver>().jointTransformReferences;
        else
            jointsTransforms = GameObject.Find("Right Hand Tracking").GetComponent<XRHandSkeletonDriver>().jointTransformReferences;

        if (jointsTransforms == null)
        {
            Debug.LogWarning("Can't find Hands Skeleton Drivers");
            return userCondition;
        }

        switch (userCondition.handAxis)
        {
            case XRHandAxis.PalmDirection:
                XRHandJoint palmJoint = hand.GetJoint(XRHandJointID.Palm);
                Transform palmTransfrom = jointsTransforms[palmJoint.id.ToIndex()].jointTransform;

                if (userCondition.referenceDirection == XRHandUserRelativeDirection.OriginUp)
                {
                    userCondition.alignmentCondition = GetAlignmentCondition(palmTransfrom, originTransfrom, userCondition.handAxis, userCondition.referenceDirection, angleTolerance);
                }
                else if (userCondition.referenceDirection == XRHandUserRelativeDirection.HandToHead)
                {
                    userCondition.alignmentCondition = GetAlignmentCondition(palmTransfrom, headTransfrom, userCondition.handAxis, userCondition.referenceDirection, angleTolerance);
                }
                break;

            case XRHandAxis.ThumbExtendedDirection:
                XRHandJoint thumbJoint = hand.GetJoint(XRHandJointID.ThumbTip);
                Transform thumbTransfrom = jointsTransforms[thumbJoint.id.ToIndex()].jointTransform;

                if (userCondition.referenceDirection == XRHandUserRelativeDirection.HandToHead)
                {
                    userCondition.alignmentCondition = GetAlignmentCondition(thumbTransfrom, headTransfrom, userCondition.handAxis, userCondition.referenceDirection, angleTolerance);
                }
                else if (userCondition.referenceDirection == XRHandUserRelativeDirection.OriginUp)
                {
                    userCondition.alignmentCondition = GetAlignmentCondition(thumbTransfrom, originTransfrom, userCondition.handAxis, userCondition.referenceDirection, angleTolerance);
                }
                break;

            case XRHandAxis.FingersExtendedDirection:
                float allDesiredCurls = 0;
                for (int i = 1; i < handShapeCreated.fingerShapeConditions.Count; i++) // start at 1 to exclude thumb
                {
                    allDesiredCurls += handShapeCreated.fingerShapeConditions[i].targets[0].desired;
                }
                if (allDesiredCurls > 3f || allDesiredCurls < 0.5f) // most all curled or extended
                {
                    XRHandJoint indexJoint = hand.GetJoint(XRHandJointID.IndexTip);
                    XRHandJoint middleJoint = hand.GetJoint(XRHandJointID.MiddleTip);
                    XRHandJoint ringJoint = hand.GetJoint(XRHandJointID.RingTip);
                    XRHandJoint littleJoint = hand.GetJoint(XRHandJointID.LittleTip);
                    Transform indexTransfrom = jointsTransforms[indexJoint.id.ToIndex()].jointTransform;
                    Transform middleTransfrom = jointsTransforms[middleJoint.id.ToIndex()].jointTransform;
                    Transform ringTransfrom = jointsTransforms[ringJoint.id.ToIndex()].jointTransform;
                    Transform littleTransfrom = jointsTransforms[littleJoint.id.ToIndex()].jointTransform;

                    XRHandJoint palmJointFingers = hand.GetJoint(XRHandJointID.Palm);
                    Transform palmTransfromFingers = jointsTransforms[palmJointFingers.id.ToIndex()].jointTransform;

                    GameObject tempTransformGameObject = new GameObject();
                    Transform globalFingersTransform = tempTransformGameObject.transform;
                    globalFingersTransform.SetParent(palmTransfromFingers);
                    Vector3 mediumPosition = (indexTransfrom.position + middleTransfrom.position + ringTransfrom.position + littleTransfrom.position) / 4;
                    Vector3 mediumEuleurAngles = (indexTransfrom.eulerAngles + middleTransfrom.eulerAngles + ringTransfrom.eulerAngles + littleTransfrom.eulerAngles) / 4;
                    globalFingersTransform.position = mediumPosition;
                    globalFingersTransform.eulerAngles = mediumEuleurAngles;

                    if (userCondition.referenceDirection == XRHandUserRelativeDirection.HandToHead)
                    {
                        userCondition.alignmentCondition = GetAlignmentCondition(globalFingersTransform, headTransfrom, userCondition.handAxis, userCondition.referenceDirection, angleTolerance);
                    }
                    else if (userCondition.referenceDirection == XRHandUserRelativeDirection.OriginUp)
                    {
                        userCondition.alignmentCondition = GetAlignmentCondition(globalFingersTransform, originTransfrom, userCondition.handAxis, userCondition.referenceDirection, angleTolerance);
                    }
                    Destroy(tempTransformGameObject);
                }
                else
                    userCondition = null;
                break;

            default:
                break;
        }
        if (userCondition != null)
            userCondition.angleTolerance = angleTolerance;

        return userCondition;
    }

    private HandBoneOrientation PalmOrientation(Transform palmTransform, bool fromOrigin)
    {
        HandBoneOrientation orientation = HandBoneOrientation.None;
        Vector3 referencePosition = Vector3.zero;
        if (fromOrigin)
            referencePosition = originTransfrom.position;
        else
            referencePosition = headTransfrom.position;

        Vector3 palmPosition = palmTransform.position;
        Vector3 palmDirection;

        // Increase the magnitude (length) with a scalar factor
        float magnitudeIncrease = 5;
        // Adjust direction based on palm orientation
        palmDirection = palmPosition + (-palmTransform.up * magnitudeIncrease);

        Vector3 palmPositionScaled = (palmDirection * magnitudeIncrease);
        Vector3 directionPalmToReference = palmPositionScaled - referencePosition;

        Vector3 positiveDirection = directionPalmToReference;
        if (positiveDirection.x < 0)
            positiveDirection = new Vector3(0 - positiveDirection.x, positiveDirection.y, positiveDirection.z);
        if (positiveDirection.y < 0)
            positiveDirection = new Vector3(positiveDirection.x, 0 - positiveDirection.y, positiveDirection.z);
        if (positiveDirection.z < 0)
            positiveDirection = new Vector3(positiveDirection.x, positiveDirection.y, 0 - positiveDirection.z);

        if (positiveDirection.x > positiveDirection.y && positiveDirection.x > positiveDirection.z)
        {
            if (directionPalmToReference.x < 0)
                orientation = HandBoneOrientation.Left;
            else
                orientation = HandBoneOrientation.Right;
        }
        else if (positiveDirection.y > positiveDirection.x && positiveDirection.y > positiveDirection.z)
        {
            if (directionPalmToReference.y < 0)
                orientation = HandBoneOrientation.Down;
            else
                orientation = HandBoneOrientation.Up;
        }
        else if (positiveDirection.z > positiveDirection.x && positiveDirection.z > positiveDirection.y)
        {
            if (directionPalmToReference.z < 0)
                orientation = HandBoneOrientation.Back;
            else
                orientation = HandBoneOrientation.Front;
        }

        return orientation;
    }

    private HandBoneOrientation ThumbOrientationFromHead(Transform ThumbTransform)
    {
        HandBoneOrientation orientation = HandBoneOrientation.None;
        Vector3 headPosition = headTransfrom.position;

        Vector3 thumbPosition = ThumbTransform.position;
        Vector3 thumbDirection;

        // Increase the magnitude (length) with a scalar factor
        float magnitudeIncrease = 5;
        // Adjust direction based on palm orientation
        if (m_Handedness == Handedness.Left)
            thumbDirection = thumbPosition + (ThumbTransform.right * magnitudeIncrease);
        else
            thumbDirection = thumbPosition - ThumbTransform.right * (magnitudeIncrease);

        Vector3 thumbPositionScaled = (thumbDirection * magnitudeIncrease);
        Vector3 directionThumbToHead = thumbPositionScaled - headPosition;

        Vector3 positiveDirection = directionThumbToHead;
        if (positiveDirection.x < 0)
            positiveDirection = new Vector3(0 - positiveDirection.x, positiveDirection.y, positiveDirection.z);
        if (positiveDirection.y < 0)
            positiveDirection = new Vector3(positiveDirection.x, 0 - positiveDirection.y, positiveDirection.z);
        if (positiveDirection.z < 0)
            positiveDirection = new Vector3(positiveDirection.x, positiveDirection.y, 0 - positiveDirection.z);

        if (positiveDirection.x > positiveDirection.y && positiveDirection.x > positiveDirection.z)
        {
            if (directionThumbToHead.x < 0)
                orientation = HandBoneOrientation.Left;
            else
                orientation = HandBoneOrientation.Right;
        }
        else if (positiveDirection.y > positiveDirection.x && positiveDirection.y > positiveDirection.z)
        {
            if (directionThumbToHead.y < 0)
                orientation = HandBoneOrientation.Down;
            else
                orientation = HandBoneOrientation.Up;
        }
        else if (positiveDirection.z > positiveDirection.x && positiveDirection.z > positiveDirection.y)
        {
            if (directionThumbToHead.z < 0)
                orientation = HandBoneOrientation.Back;
            else
                orientation = HandBoneOrientation.Front;
        }

        return orientation;
    }

    private HandBoneOrientation FingersOrientationFromHead(Transform globalFingersTransform)
    {
        HandBoneOrientation orientation = HandBoneOrientation.None;
        // Normalize from palm
        List<JointToTransformReference> jointsTransforms = new List<JointToTransformReference>();
        if (m_Handedness == Handedness.Left)
            jointsTransforms = GameObject.Find("Left Hand Tracking").GetComponent<XRHandSkeletonDriver>().jointTransformReferences;
        else
            jointsTransforms = GameObject.Find("Right Hand Tracking").GetComponent<XRHandSkeletonDriver>().jointTransformReferences;

        if (jointsTransforms == null)
        {
            Debug.LogWarning("Can't find Hands Skeleton Drivers");
        }
        XRHandJoint palmJoint = hand.GetJoint(XRHandJointID.Palm);
        Transform palmTransform = jointsTransforms[palmJoint.id.ToIndex()].jointTransform;

        Vector3 fingersWorldPosition = globalFingersTransform.position;
        Vector3 palmWorldPosition = palmTransform.position;

        Vector3 fingersdirectionFromPalm = (fingersWorldPosition - palmWorldPosition).normalized;
        // Increase the magnitude (length) with a scalar factor
        float magnitudeIncrease = 5;
        // Adjust direction based on palm orientation
        Vector3 fingersDirection = fingersWorldPosition + (fingersdirectionFromPalm * magnitudeIncrease);
        Vector3 fingersPositionScaled = (fingersDirection * magnitudeIncrease);
        Vector3 directionFingersToReference = fingersPositionScaled - headTransfrom.position;

        Vector3 positiveDirection = directionFingersToReference;
        if (positiveDirection.x < 0)
            positiveDirection = new Vector3(0 - positiveDirection.x, positiveDirection.y, positiveDirection.z);
        if (positiveDirection.y < 0)
            positiveDirection = new Vector3(positiveDirection.x, 0 - positiveDirection.y, positiveDirection.z);
        if (positiveDirection.z < 0)
            positiveDirection = new Vector3(positiveDirection.x, positiveDirection.y, 0 - positiveDirection.z);

        if (positiveDirection.x > positiveDirection.y && positiveDirection.x > positiveDirection.z)
        {
            if (directionFingersToReference.x < 0)
                orientation = HandBoneOrientation.Left;
            else
                orientation = HandBoneOrientation.Right;
        }
        else if (positiveDirection.y > positiveDirection.x && positiveDirection.y > positiveDirection.z)
        {
            if (directionFingersToReference.y < 0)
                orientation = HandBoneOrientation.Down;
            else
                orientation = HandBoneOrientation.Up;
        }
        else if (positiveDirection.z > positiveDirection.x && positiveDirection.z > positiveDirection.y)
        {
            if (directionFingersToReference.z < 0)
                orientation = HandBoneOrientation.Back;
            else
                orientation = HandBoneOrientation.Front;
        }

        return orientation;

    }

    // Set the alignment by the orientation of the bone from an axis
    private XRHandAlignmentCondition GetAlignmentCondition(Transform jointTransform, Transform otherTransform, XRHandAxis handAxis, XRHandUserRelativeDirection refDirection, float threshold = 60f)
    {
        XRHandAlignmentCondition alignmentCondition = XRHandAlignmentCondition.OppositeTo;

        float angle = angleTolerance / 360;
        Vector3 jointDirection = Vector3.zero;
        Vector3 otherDirection = Vector3.zero;

        switch (handAxis)
        {
            case XRHandAxis.PalmDirection:
                bool fromOrigin = true;
                if (refDirection == XRHandUserRelativeDirection.HandToHead)
                    fromOrigin = false;
                HandBoneOrientation palmOrientation = PalmOrientation(jointTransform, fromOrigin);

                switch (palmOrientation)
                {
                    case HandBoneOrientation.Front:
                        if (refDirection == XRHandUserRelativeDirection.HandToHead)
                            alignmentCondition = XRHandAlignmentCondition.OppositeTo;
                        else if (refDirection == XRHandUserRelativeDirection.OriginUp)
                            alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        break;
                    case HandBoneOrientation.Back:
                        if (refDirection == XRHandUserRelativeDirection.HandToHead)
                            alignmentCondition = XRHandAlignmentCondition.AlignsWith;
                        else if (refDirection == XRHandUserRelativeDirection.OriginUp)
                            alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        break;
                    case HandBoneOrientation.Left:
                        if (refDirection == XRHandUserRelativeDirection.HandToHead)
                            alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        else if (refDirection == XRHandUserRelativeDirection.OriginUp)
                            alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        break;
                    case HandBoneOrientation.Right:
                        if (refDirection == XRHandUserRelativeDirection.HandToHead)
                            alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        else if (refDirection == XRHandUserRelativeDirection.OriginUp)
                            alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        break;
                    case HandBoneOrientation.Up:
                        if (refDirection == XRHandUserRelativeDirection.HandToHead)
                            alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        else if (refDirection == XRHandUserRelativeDirection.OriginUp)
                            alignmentCondition = XRHandAlignmentCondition.AlignsWith;
                        break;
                    case HandBoneOrientation.Down:
                        if (refDirection == XRHandUserRelativeDirection.HandToHead)
                            alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        else if (refDirection == XRHandUserRelativeDirection.OriginUp)
                            alignmentCondition = XRHandAlignmentCondition.OppositeTo;
                        break;
                    default:
                        break;
                }
                break;

            case XRHandAxis.ThumbExtendedDirection:
                HandBoneOrientation thumbOrientation = ThumbOrientationFromHead(jointTransform);
                switch (thumbOrientation)
                {
                    case HandBoneOrientation.Front:
                        if (refDirection == XRHandUserRelativeDirection.HandToHead)
                            alignmentCondition = XRHandAlignmentCondition.AlignsWith;
                        else if (refDirection == XRHandUserRelativeDirection.OriginUp)
                            alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        break;
                    case HandBoneOrientation.Back:
                        if (refDirection == XRHandUserRelativeDirection.HandToHead)
                            alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        else if (refDirection == XRHandUserRelativeDirection.OriginUp)
                            alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        break;
                    case HandBoneOrientation.Left:
                        if (refDirection == XRHandUserRelativeDirection.HandToHead)
                            alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        else if (refDirection == XRHandUserRelativeDirection.OriginUp)
                            alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        break;
                    case HandBoneOrientation.Right:
                        if (refDirection == XRHandUserRelativeDirection.HandToHead)
                            alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        else if (refDirection == XRHandUserRelativeDirection.OriginUp)
                            alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        break;
                    case HandBoneOrientation.Up:
                        if (refDirection == XRHandUserRelativeDirection.HandToHead)
                            alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        else if (refDirection == XRHandUserRelativeDirection.OriginUp)
                            alignmentCondition = XRHandAlignmentCondition.AlignsWith;
                        break;
                    case HandBoneOrientation.Down:
                        if (refDirection == XRHandUserRelativeDirection.HandToHead)
                            alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        else if (refDirection == XRHandUserRelativeDirection.OriginUp)
                            alignmentCondition = XRHandAlignmentCondition.OppositeTo;
                        break;
                    default:
                        break;
                }
                break;

            case XRHandAxis.FingersExtendedDirection:
                HandBoneOrientation fingersOrientation = FingersOrientationFromHead(jointTransform);
                switch (fingersOrientation)
                {
                    case HandBoneOrientation.Front:
                        alignmentCondition = XRHandAlignmentCondition.OppositeTo;
                        break;
                    case HandBoneOrientation.Back:
                        alignmentCondition = XRHandAlignmentCondition.AlignsWith;
                        break;
                    case HandBoneOrientation.Left:
                        alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        break;
                    case HandBoneOrientation.Right:
                        alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        break;
                    case HandBoneOrientation.Up:
                        alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        break;
                    case HandBoneOrientation.Down:
                        alignmentCondition = XRHandAlignmentCondition.PerpendicularTo;
                        break;
                    default:
                        break;
                }
                break;

            default:
                break;
        }
        return alignmentCondition;
    }
    #endregion

    #region Static Methods
    static XRHandSubsystem TryGetSubsystem()
    {
        SubsystemManager.GetSubsystems(s_SubsystemsReuse);
        return s_SubsystemsReuse.Count > 0 ? s_SubsystemsReuse[0] : null;
    }
    #endregion

    #region Debug directly in the scene
    // (Leaving your debug methods commented as-is. If you re-enable them,
    // wrap any AssetDatabase usage in #if UNITY_EDITOR as shown above.)
    #endregion
}
