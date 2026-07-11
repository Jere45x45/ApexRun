using UnityEngine;

public class KartController : MonoBehaviour
{
    [Header("Wheel Colliders")]
    [SerializeField] private WheelCollider frontLeftWheel;
    [SerializeField] private WheelCollider frontRightWheel;
    [SerializeField] private WheelCollider rearLeftWheel;
    [SerializeField] private WheelCollider rearRightWheel;

    [Header("Wheel Meshes (Empty Parents)")]
    [SerializeField] private Transform frontLeftMesh;
    [SerializeField] private Transform frontRightMesh;
    [SerializeField] private Transform rearLeftMesh;
    [SerializeField] private Transform rearRightMesh;

    [Header("Vehicle Settings")]
    [SerializeField] private float motorTorque = 3000f;
    [SerializeField] private float maxSteeringAngle = 30f;
    [SerializeField] private float brakeTorque = 3000f;

    private float throttle;
    private float steering;

    private Rigidbody rb;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0f, -0.5f, 0f);
    }

    private void Update()
    {
        throttle = Input.GetAxis("Vertical");
        steering = Input.GetAxis("Horizontal");
    }

    private void FixedUpdate()
    {
        // Motor
        rearLeftWheel.motorTorque = throttle * motorTorque;
        rearRightWheel.motorTorque = throttle * motorTorque;

        // Dirección
        frontLeftWheel.steerAngle = steering * maxSteeringAngle;
        frontRightWheel.steerAngle = steering * maxSteeringAngle;

        // Freno
        if (Mathf.Abs(throttle) < 0.1f)
        {
            rearLeftWheel.brakeTorque = brakeTorque;
            rearRightWheel.brakeTorque = brakeTorque;
        }
        else
        {
            rearLeftWheel.brakeTorque = 0f;
            rearRightWheel.brakeTorque = 0f;
        }

        // Actualizar ruedas visuales
        UpdateWheel(frontLeftWheel, frontLeftMesh);
        UpdateWheel(frontRightWheel, frontRightMesh);
        UpdateWheel(rearLeftWheel, rearLeftMesh);
        UpdateWheel(rearRightWheel, rearRightMesh);
    }

    private void UpdateWheel(WheelCollider wheelCollider, Transform wheelMesh)
    {
        wheelCollider.GetWorldPose(out Vector3 position, out Quaternion rotation);

        wheelMesh.position = position;
        wheelMesh.rotation = rotation;
    }
}