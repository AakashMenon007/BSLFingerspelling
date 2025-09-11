using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(XRGrabInteractable))]
public class YAxisKnobSpinner : MonoBehaviour
{
    [Tooltip("1 = 1:1 wrist twist to object rotation")]
    public float sensitivity = 1.0f;

    XRGrabInteractable grab;
    IXRSelectInteractor interactor;
    Rigidbody rb;

    Vector3 initialLocalEuler;
    Vector3 initialWorldPos;

    Vector3 axisWorld;      // this object's local Y in world space
    Vector3 prevDirOnPlane; // last interactor direction projected on plane ⟂ axis
    float accumulatedAngle;

    void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();

        // Fixed-in-place & lock tilt via physics as a backstop
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.constraints = RigidbodyConstraints.FreezePosition
                        | RigidbodyConstraints.FreezeRotationX
                        | RigidbodyConstraints.FreezeRotationZ;
    }

    void OnEnable()
    {
        grab.selectEntered.AddListener(OnGrab);
        grab.selectExited.AddListener(OnRelease);
    }
    void OnDisable()
    {
        grab.selectEntered.RemoveListener(OnGrab);
        grab.selectExited.RemoveListener(OnRelease);
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        interactor = args.interactorObject;

        initialLocalEuler = transform.localEulerAngles;
        initialWorldPos = transform.position;

        axisWorld = transform.TransformDirection(Vector3.up).normalized;

        prevDirOnPlane = GetProjectedDir(interactor.transform);
        accumulatedAngle = 0f;
    }

    void OnRelease(SelectExitEventArgs args)
    {
        interactor = null;
    }

    void LateUpdate()
    {
        if (interactor == null) return;

        // keep exact center position
        transform.position = initialWorldPos;

        // accumulate wrist yaw around our Y axis
        Vector3 curDir = GetProjectedDir(interactor.transform);
        float step = Vector3.SignedAngle(prevDirOnPlane, curDir, axisWorld);
        accumulatedAngle += step * sensitivity;
        prevDirOnPlane = curDir;

        // only rotate Y; X/Z stay at their original values
        var e = initialLocalEuler;
        e.y = initialLocalEuler.y + accumulatedAngle;
        transform.localEulerAngles = e;
    }

    // Take a stable 2D direction from the interactor on plane ⟂ axis (uses forward, falls back to right)
    Vector3 GetProjectedDir(Transform t)
    {
        Vector3 d = Vector3.ProjectOnPlane(t.forward, axisWorld);
        if (d.sqrMagnitude < 1e-6f) d = Vector3.ProjectOnPlane(t.right, axisWorld);
        if (d.sqrMagnitude < 1e-6f) d = Vector3.forward;
        return d.normalized;
    }
}
