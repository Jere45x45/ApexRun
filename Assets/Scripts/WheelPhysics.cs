using System;
using UnityEngine;

public class WheelPhysics
{
    private readonly Rigidbody rigidbody;
    private readonly Transform wheelPoint;
    private readonly Quaternion initialLocalRotation;

    private float radius;
    private float suspensionDistance;
    private float springRate;
    private float damperRate;

    private float driveTorque;
    private float brakeTorque;
    private float steeringAngle;
    private float gripMultiplier = 1f;

    private float previousCompression;

    public bool IsGrounded { get; private set; }

    public float Compression { get; private set; }

    public Vector3 GroundPoint { get; private set; }

    public Vector3 GroundNormal { get; private set; }

    public Vector3 ContactVelocity { get; private set; }

    public float DriveTorque => driveTorque;

    public float BrakeTorque => brakeTorque;

    public float SteeringAngle => steeringAngle;

    public float GripMultiplier => gripMultiplier;

    public Transform WheelPoint => wheelPoint;

    public Vector3 VisualPosition
    {
        get
        {
            if (!IsGrounded)
                return wheelPoint.position;

            return GroundPoint +
                   GroundNormal * radius;
        }
    }

    public Quaternion VisualRotation =>
        wheelPoint.rotation;

    public WheelPhysics(
        Rigidbody rigidbody,
        Transform wheelPoint)
    {
        if (rigidbody == null)
            throw new ArgumentNullException(
                nameof(rigidbody)
            );

        if (wheelPoint == null)
            throw new ArgumentNullException(
                nameof(wheelPoint)
            );

        this.rigidbody = rigidbody;
        this.wheelPoint = wheelPoint;

        initialLocalRotation =
            wheelPoint.localRotation;

        GroundNormal = Vector3.up;
    }

    public void Configure(
        float radius,
        float suspensionDistance,
        float springRate,
        float damperRate)
    {
        if (radius <= 0f)
            throw new ArgumentOutOfRangeException(
                nameof(radius)
            );

        if (suspensionDistance < 0f)
            throw new ArgumentOutOfRangeException(
                nameof(suspensionDistance)
            );

        if (springRate < 0f)
            throw new ArgumentOutOfRangeException(
                nameof(springRate)
            );

        if (damperRate < 0f)
            throw new ArgumentOutOfRangeException(
                nameof(damperRate)
            );

        this.radius = radius;
        this.suspensionDistance = suspensionDistance;
        this.springRate = springRate;
        this.damperRate = damperRate;
    }

    public void SetDriveTorque(float torque)
    {
        driveTorque = torque;
    }

    public void ClearDriveTorque()
    {
        driveTorque = 0f;
    }

    public void SetBrakeTorque(float torque)
    {
        brakeTorque =
            Mathf.Max(0f, torque);
    }

    public void ClearBrakeTorque()
    {
        brakeTorque = 0f;
    }

    public void SetGripMultiplier(float multiplier)
    {
        gripMultiplier =
            Mathf.Max(0f, multiplier);
    }

    public void SetSteeringAngle(float angle)
    {
        steeringAngle = angle;

        wheelPoint.localRotation =
            initialLocalRotation *
            Quaternion.Euler(
                0f,
                steeringAngle,
                0f
            );
    }

    public void ResetSteering()
    {
        SetSteeringAngle(0f);
    }

    public void Update(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        float oldCompression =
            Compression;

        Vector3 origin =
            wheelPoint.position;

        float rayLength =
            suspensionDistance + radius;

        if (Physics.Raycast(
            origin,
            -wheelPoint.up,
            out RaycastHit hit,
            rayLength))
        {
            IsGrounded = true;

            GroundPoint = hit.point;
            GroundNormal = hit.normal;

            float suspensionLength =
                hit.distance - radius;

            suspensionLength =
                Mathf.Clamp(
                    suspensionLength,
                    0f,
                    suspensionDistance
                );

            Compression =
                suspensionDistance > 0f
                    ? 1f -
                      (
                          suspensionLength /
                          suspensionDistance
                      )
                    : 0f;

            Compression =
                Mathf.Clamp01(
                    Compression
                );

            ContactVelocity =
                rigidbody.GetPointVelocity(
                    hit.point
                );
        }
        else
        {
            IsGrounded = false;

            GroundPoint = Vector3.zero;
            GroundNormal = Vector3.up;
            ContactVelocity = Vector3.zero;

            Compression = 0f;
        }

        previousCompression = oldCompression;
    }

    public float GetSuspensionForce(float deltaTime)
    {
        if (!IsGrounded ||
            deltaTime <= 0f)
        {
            return 0f;
        }

        float compressionVelocity =
            (Compression - previousCompression) /
            deltaTime;

        float springForce =
            Compression * springRate;

        float damperForce =
            compressionVelocity * damperRate;

        return Mathf.Max(
            0f,
            springForce + damperForce
        );
    }

    public void ApplySuspensionForce(float deltaTime)
    {
        if (!IsGrounded)
            return;

        float force =
            GetSuspensionForce(deltaTime);

        if (force <= 0f)
            return;

        rigidbody.AddForceAtPosition(
            wheelPoint.up * force,
            GroundPoint,
            ForceMode.Force
        );
    }

    public Vector3 GetWheelForward()
    {
        Vector3 forward =
            Vector3.ProjectOnPlane(
                wheelPoint.forward,
                GroundNormal
            );

        if (forward.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        return forward.normalized;
    }

    public Vector3 GetWheelRight()
    {
        Vector3 right =
            Vector3.ProjectOnPlane(
                wheelPoint.right,
                GroundNormal
            );

        if (right.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        return right.normalized;
    }
}