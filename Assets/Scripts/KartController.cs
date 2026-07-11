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
    [SerializeField] private float minSteeringAngle = 10f;
    [SerializeField] private float steeringReductionSpeed = 20f;
    [SerializeField] private float brakeTorque = 3000f;

    private float throttle;
    private float steering;

    private Rigidbody rb;

    private bool braking;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0f, -0.5f, 0f);
    }

    private void Update()
    {
        throttle = Input.GetAxis("Vertical");
        steering = Input.GetAxis("Horizontal");

        braking = Input.GetKey(KeyCode.Space);
    }

    private void FixedUpdate()
    {
        HandleMotor();
        HandleSteering();
        HandleBraking();
        UpdateWheels();
    }

    private void UpdateWheels()
    {
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
    
    private void HandleMotor()
    {
        rearLeftWheel.motorTorque = throttle * motorTorque;
        rearRightWheel.motorTorque = throttle * motorTorque;
    }

    private void HandleSteering()
    {
        float speed = rb.velocity.magnitude;
    
        float currentSteeringAngle = Mathf.Lerp(
            maxSteeringAngle,
            minSteeringAngle,
            Mathf.Clamp01(speed / steeringReductionSpeed)
        );
    
        frontLeftWheel.steerAngle = steering * currentSteeringAngle;
        frontRightWheel.steerAngle = steering * currentSteeringAngle;
    }

    private void HandleBraking()
    {
        float brake = braking ? brakeTorque : 0f;

        frontLeftWheel.brakeTorque = brake;
        frontRightWheel.brakeTorque = brake;
        rearLeftWheel.brakeTorque = brake;
        rearRightWheel.brakeTorque = brake;
    }


}