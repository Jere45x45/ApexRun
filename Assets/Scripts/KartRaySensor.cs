using UnityEngine;

public class KartRaySensor : MonoBehaviour
{
    [Header("Sensor Height")]
    [SerializeField] private float sensorHeight = 3f;

    [Header("Ray Length")]
    [SerializeField] private float rayLength = 7f;

    [Header("Sensor Width")]
    [SerializeField] private float sideDistance = 2.5f;

    [Header("Sensor Forward Distance")]
    [SerializeField] private float nearDistance = 2.5f;
    [SerializeField] private float middleDistance = 5f;
    [SerializeField] private float farDistance = 8f;

    [Header("Track")]
    [SerializeField] private LayerMask trackLayer;

    /// <summary>
    /// 9 sensores (3 filas a distintas distancias × izquierda, centro y
    /// derecha): 1 si debajo hay pista, 0 si no.
    /// </summary>
    public float[] GetTrackSensors()
    {
        float[] values = new float[9];

        Vector3[] positions =
        {

            transform.position
                + transform.forward * nearDistance
                - transform.right * sideDistance,

            transform.position
                + transform.forward * nearDistance,

            transform.position
                + transform.forward * nearDistance
                + transform.right * sideDistance,




            transform.position
                + transform.forward * middleDistance
                - transform.right * sideDistance,

            transform.position
                + transform.forward * middleDistance,

            transform.position
                + transform.forward * middleDistance
                + transform.right * sideDistance,




            transform.position
                + transform.forward * farDistance
                - transform.right * sideDistance,

            transform.position
                + transform.forward * farDistance,

            transform.position
                + transform.forward * farDistance
                + transform.right * sideDistance
        };

        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 start =
                positions[i] +
                Vector3.up * sensorHeight;

            if (Physics.Raycast(
                start,
                Vector3.down,
                out RaycastHit hit,
                rayLength,
                trackLayer))
            {

                // Hay pista o no hay: la distancia del rayo solo cambiaba con
                // las subidas y bajadas, y eso no le dice nada útil al bot.
                values[i] = 1f;

                Debug.DrawLine(
                    start,
                    hit.point,
                    Color.green
                );
            }
            else
            {
                values[i] = 0f;

                Debug.DrawRay(
                    start,
                    Vector3.down * rayLength,
                    Color.red
                );
            }
        }

        return values;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;

        Vector3[] positions =
        {
            transform.position
                + transform.forward * nearDistance
                - transform.right * sideDistance,

            transform.position
                + transform.forward * nearDistance,

            transform.position
                + transform.forward * nearDistance
                + transform.right * sideDistance,

            transform.position
                + transform.forward * middleDistance
                - transform.right * sideDistance,

            transform.position
                + transform.forward * middleDistance,

            transform.position
                + transform.forward * middleDistance
                + transform.right * sideDistance,

            transform.position
                + transform.forward * farDistance
                - transform.right * sideDistance,

            transform.position
                + transform.forward * farDistance,

            transform.position
                + transform.forward * farDistance
                + transform.right * sideDistance
        };

        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 start =
                positions[i] +
                Vector3.up * sensorHeight;

            Gizmos.DrawLine(
                start,
                start + Vector3.down * rayLength
            );
        }
    }
}