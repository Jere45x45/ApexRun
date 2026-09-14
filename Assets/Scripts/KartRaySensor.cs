using UnityEngine;

public class KartRaySensor : MonoBehaviour
{
    [Header("Sensor")]
    [SerializeField] private float sensorHeight = 2f;
    [SerializeField] private float rayLength = 5f;

    [Header("Sensor Positions")]
    [SerializeField] private float forwardDistance = 2f;
    [SerializeField] private float sideDistance = 1.5f;

    [Header("Track")]
    [SerializeField] private LayerMask trackLayer;

    public float[] GetTrackSensors()
    {
        float[] values = new float[6];

        Vector3[] positions =
        {
            transform.position + transform.forward * forwardDistance
                - transform.right * sideDistance,

            transform.position + transform.forward * forwardDistance,

            transform.position + transform.forward * forwardDistance
                + transform.right * sideDistance,

            transform.position - transform.right * sideDistance,

            transform.position,

            transform.position + transform.right * sideDistance
        };

        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 start = positions[i] + Vector3.up * sensorHeight;
            if (Physics.Raycast(start, Vector3.down, out RaycastHit hit, rayLength, trackLayer))
            {
                values[i] = 1f;

                Debug.DrawLine
                (
                    start,
                    hit.point,
                    Color.green
                );
            }
            else
            {
                values[i] = 0f;

                Debug.DrawRay
                (
                    start,
                    Vector3.down * rayLength,
                    Color.red
                );
            }
        }

        return values;
    }
}