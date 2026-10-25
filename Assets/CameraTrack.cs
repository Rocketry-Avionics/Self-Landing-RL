using UnityEngine;

public class CameraTrack : MonoBehaviour
{
    public Transform rocket;
    private Vector3 offset;

    void Start()
    {
        if (rocket != null)
        {
            // Lock onto the rocket and step back slightly so we see the rocket + trail clearly
            offset = new Vector3(0f, 0.25f, -2.5f);
            transform.rotation = Quaternion.Euler(0f, 0f, 0f);
        }
    }

    void LateUpdate()
    {
        if (rocket != null)
        {
            // Exact position lock so the rocket NEVER flies out of the camera view!
            transform.position = rocket.position + offset;
        }
    }
}