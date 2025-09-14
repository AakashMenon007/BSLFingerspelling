using UnityEngine;

public class SpinInPlace : MonoBehaviour
{
    // Speed of rotation in degrees per second
    public float spinSpeed = 90f;

    void Update()
    {
        // Rotate around the cylinder’s local Y-axis
        transform.Rotate(Vector3.up * spinSpeed * Time.deltaTime, Space.Self);
    }
}
