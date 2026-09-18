using UnityEngine;

public class CustomWheel : MonoBehaviour
{
    [Header("Configuración Base")]
    public float radius = 0.2f;
    public float suspensionDistance = 0.3f;

    [Header("Suspensión Arcade")]
    public float springForce = 15000f;
    public float damperForce = 3000f;

    [Header("Variables en Runtime")]
    public float motorTorque;
    public float brakeTorque;
    public float steerAngle;

    public bool IsGrounded { get; private set; }
    public RaycastHit GroundHit;

    private Rigidbody rb;
    private float lastLength;

    public void Initialize(Rigidbody kartRigidbody)
    {
        rb = kartRigidbody;
        lastLength = suspensionDistance;
    }

    private void FixedUpdate()
    {
        if (rb == null) return;

        // 1. Rotación de la dirección
        transform.localRotation = Quaternion.Euler(0f, steerAngle, 0f);

        Vector3 rayOrigin = transform.position;
        Vector3 rayDir = -transform.up;
        float maxLength = suspensionDistance + radius;

        // 2. Raycast hacia el suelo
        IsGrounded = Physics.Raycast(rayOrigin, rayDir, out GroundHit, maxLength);

        if (IsGrounded)
        {
            // --- SUSPENSIÓN (Ley de Hooke) ---
            float currentLength = GroundHit.distance - radius;
            float springCompression = (suspensionDistance - currentLength) / suspensionDistance;
            float springVelocity = (currentLength - lastLength) / Time.fixedDeltaTime;
            lastLength = currentLength;

            float force = (springCompression * springForce) - (springVelocity * damperForce);
            rb.AddForceAtPosition(transform.up * force, transform.position);

            // --- TRACCIÓN Y FRENADO ---
            if (Mathf.Abs(motorTorque) > 0.01f)
            {
                rb.AddForceAtPosition(transform.forward * motorTorque, transform.position);
            }

            if (brakeTorque > 0.01f)
            {
                Vector3 wheelVelocity = rb.GetPointVelocity(transform.position);
                rb.AddForceAtPosition(-wheelVelocity.normalized * brakeTorque, transform.position);
            }

            // --- FRICCIÓN LATERAL (Agarre Arcade) ---
            Vector3 localVelocity = rb.GetPointVelocity(transform.position);
            float sidewaysVelocity = Vector3.Dot(localVelocity, transform.right);
            float gripFactor = rb.mass * 15f;
            rb.AddForceAtPosition(-transform.right * (sidewaysVelocity * gripFactor), transform.position);
        }
        else
        {
            lastLength = suspensionDistance;
        }
    }

    // Herramienta visual: Dibuja la suspensión y la rueda en la vista de Escena
    private void OnDrawGizmos()
    {
        Gizmos.color = IsGrounded ? Color.green : Color.red;
        Vector3 start = transform.position;
        Vector3 end = start - transform.up * (suspensionDistance + radius);
        Gizmos.DrawLine(start, end);
        Gizmos.DrawWireSphere(end, radius);
    }
}