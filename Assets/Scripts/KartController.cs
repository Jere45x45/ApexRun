using UnityEngine;

public class KartController : MonoBehaviour
{
    [SerializeField] private WheelCollider frontLeftWheel;
    [SerializeField] private WheelCollider frontRightWheel;
    [SerializeField] private WheelCollider rearLeftWheel;
    [SerializeField] private WheelCollider rearRightWheel;

    [SerializeField] private Transform frontLeftMesh;
    [SerializeField] private Transform frontRightMesh;
    [SerializeField] private Transform rearLeftMesh;
    [SerializeField] private Transform rearRightMesh;

    [SerializeField] private float motorTorque = 1000f;
    [SerializeField] private float maxSteeringAngle = 30f;
    [SerializeField] private float brakeTorque = 3000f;

    private float throttle;
    private float steering;

    void Update()
    {
        throttle = Input.GetAxis("Vertical");
        steering = Input.GetAxis("Horizontal");
    }

    void FixedUpdate()
    {
        rearLeftWheel.motorTorque = throttle * motorTorque;
        rearRightWheel.motorTorque = throttle * motorTorque;
    
        frontLeftWheel.steerAngle = steering * maxSteeringAngle;
        frontRightWheel.steerAngle = steering * maxSteeringAngle;
    }

}