using UnityEngine;

public class KartRaySensor : MonoBehaviour
{
    [Header("Ground Sensors")]
    [SerializeField] private float sensorHeight = 2f;
    [SerializeField] private float rayLength = 5f;

    [Header("Ground Sensor Positions")]
    [SerializeField] private float forwardDistance = 3f;
    [SerializeField] private float sideDistance = 2f;

    [Header("Forward Edge Sensors")]
    [SerializeField] private float forwardSensorHeight = 0.8f;
    [SerializeField] private float forwardSensorLength = 8f;
    [SerializeField] private float forwardSensorAngle = 30f;

    [Header("Track")]
    [SerializeField] private LayerMask trackLayer;

    public float[] GetTrackSensors()
    {
        // 9 sensores de suelo + 6 sensores frontales
        float[] values = new float[15];

        // =========================================================
        // 9 SENSORES DE SUELO
        // =========================================================

        Vector3[] groundPositions =
        {
            // Fila cercana
            transform.position
                + transform.forward * forwardDistance
                - transform.right * sideDistance,

            transform.position
                + transform.forward * forwardDistance,

            transform.position
                + transform.forward * forwardDistance
                + transform.right * sideDistance,

            // Fila central
            transform.position
                - transform.right * sideDistance,

            transform.position,

            transform.position
                + transform.right * sideDistance,

            // Fila lejana
            transform.position
                + transform.forward * (forwardDistance * 1.5f)
                - transform.right * sideDistance * 0.5f,

            transform.position
                + transform.forward * (forwardDistance * 1.5f),

            transform.position
                + transform.forward * (forwardDistance * 1.5f)
                + transform.right * sideDistance * 0.5f
        };

        for (int i = 0; i < groundPositions.Length; i++)
        {
            Vector3 start =
                groundPositions[i] + Vector3.up * sensorHeight;

            if (Physics.Raycast(
                start,
                Vector3.down,
                out RaycastHit hit,
                rayLength,
                trackLayer))
            {
                values[i] =
                    1f - (hit.distance / rayLength);
            }
            else
            {
                values[i] = 0f;
            }
        }

        // =========================================================
        // 6 SENSORES FRONTALES
        // =========================================================

        Vector3 sensorOrigin =
            transform.position +
            Vector3.up * forwardSensorHeight;

        Vector3[] directions =
        {
            // Izquierda lejana
            Quaternion.AngleAxis(
                -forwardSensorAngle,
                Vector3.up
            ) * transform.forward,

            // Izquierda cercana
            Quaternion.AngleAxis(
                -forwardSensorAngle * 0.5f,
                Vector3.up
            ) * transform.forward,

            // Centro
            transform.forward,

            // Derecha cercana
            Quaternion.AngleAxis(
                forwardSensorAngle * 0.5f,
                Vector3.up
            ) * transform.forward,

            // Derecha lejana
            Quaternion.AngleAxis(
                forwardSensorAngle,
                Vector3.up
            ) * transform.forward,

            // Centro largo
            transform.forward
        };

        float[] maxDistances =
        {
            forwardSensorLength,
            forwardSensorLength,
            forwardSensorLength,
            forwardSensorLength,
            forwardSensorLength,
            forwardSensorLength * 1.25f
        };

        for (int i = 0; i < directions.Length; i++)
        {
            int index = 9 + i;

            if (Physics.Raycast(
                sensorOrigin,
                directions[i],
                out RaycastHit hit,
                maxDistances[i],
                trackLayer))
            {
                values[index] =
                    1f - (hit.distance / maxDistances[i]);
            }
            else
            {
                values[index] = 0f;
            }
        }

        return values;
    }

    private void OnDrawGizmosSelected()
    {
        // Solo sirve para visualizar aproximadamente
        // dónde salen los sensores en el editor.

        Gizmos.color = Color.yellow;

        Vector3 origin =
            transform.position +
            Vector3.up * forwardSensorHeight;

        Vector3[] directions =
        {
            Quaternion.AngleAxis(
                -forwardSensorAngle,
                Vector3.up
            ) * transform.forward,

            Quaternion.AngleAxis(
                -forwardSensorAngle * 0.5f,
                Vector3.up
            ) * transform.forward,

            transform.forward,

            Quaternion.AngleAxis(
                forwardSensorAngle * 0.5f,
                Vector3.up
            ) * transform.forward,

            Quaternion.AngleAxis(
                forwardSensorAngle,
                Vector3.up
            ) * transform.forward,

            transform.forward
        };

        for (int i = 0; i < directions.Length; i++)
        {
            float length = forwardSensorLength;

            if (i == 5)
                length *= 1.25f;

            Gizmos.DrawRay(
                origin,
                directions[i] * length
            );
        }
    }
}