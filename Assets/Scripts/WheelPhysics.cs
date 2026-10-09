using System;
using UnityEngine;

public class WheelPhysics
{
    private readonly Rigidbody rigidbody;
    private readonly WheelCollider wheelCollider;

    private WheelFrictionCurve baseForwardFriction;
    private WheelFrictionCurve baseSidewaysFriction;

    private float gripMultiplier = 1f;
    private float surfaceGripMultiplier = 1f;

    public bool IsGrounded { get; private set; }

    public Vector3 GroundPoint { get; private set; }

    public Vector3 GroundNormal { get; private set; } = Vector3.up;

    public Vector3 ContactVelocity { get; private set; }

    public float GripMultiplier => gripMultiplier;

    public float SurfaceGripMultiplier => surfaceGripMultiplier;

    public float CombinedGripMultiplier =>
        gripMultiplier * surfaceGripMultiplier;

    public WheelCollider WheelCollider => wheelCollider;

    public TrackSurfaceData CurrentSurface { get; private set; }

    public bool HasSurface => CurrentSurface != null;

    public bool IsOnValidTrack =>
        CurrentSurface == null || CurrentSurface.IsValidForTrack;

    public bool IsOnInvalidSurface =>
        CurrentSurface != null && !CurrentSurface.IsValidForTrack;

    public WheelPhysics(
        Rigidbody rigidbody,
        WheelCollider wheelCollider)
    {
        if (rigidbody == null)
            throw new ArgumentNullException(nameof(rigidbody));

        if (wheelCollider == null)
            throw new ArgumentNullException(nameof(wheelCollider));

        this.rigidbody = rigidbody;
        this.wheelCollider = wheelCollider;
    }

    public void Configure(
        float radius,
        float suspensionDistance,
        float springRate,
        float damperRate,
        float suspensionTargetPosition,
        WheelFrictionCurve forwardFriction,
        WheelFrictionCurve sidewaysFriction)
    {
        if (radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius));

        if (suspensionDistance < 0f)
            throw new ArgumentOutOfRangeException(nameof(suspensionDistance));

        if (springRate < 0f)
            throw new ArgumentOutOfRangeException(nameof(springRate));

        if (damperRate < 0f)
            throw new ArgumentOutOfRangeException(nameof(damperRate));

        wheelCollider.radius = radius;
        wheelCollider.suspensionDistance = suspensionDistance;

        JointSpring spring = wheelCollider.suspensionSpring;
        spring.spring = springRate;
        spring.damper = damperRate;
        spring.targetPosition = Mathf.Clamp01(suspensionTargetPosition);
        wheelCollider.suspensionSpring = spring;

        baseForwardFriction = forwardFriction;
        baseSidewaysFriction = sidewaysFriction;

        ApplyFrictionCurves();
    }

    public void SetDriveTorque(float torque)
    {
        wheelCollider.motorTorque = torque;
    }

    public void ClearDriveTorque()
    {
        wheelCollider.motorTorque = 0f;
    }

    public void SetBrakeTorque(float torque)
    {
        wheelCollider.brakeTorque = Mathf.Max(0f, torque);
    }

    public void ClearBrakeTorque()
    {
        wheelCollider.brakeTorque = 0f;
    }

    public void SetSteeringAngle(float angle)
    {
        wheelCollider.steerAngle = angle;
    }

    public void ResetSteering()
    {
        SetSteeringAngle(0f);
    }

    public void SetGripMultiplier(float multiplier)
    {
        gripMultiplier = Mathf.Max(0f, multiplier);
        ApplyFrictionCurves();
    }

    private void SetSurfaceGripMultiplier(float multiplier)
    {
        surfaceGripMultiplier = Mathf.Max(0f, multiplier);
        ApplyFrictionCurves();
    }

    private void ApplyFrictionCurves()
    {
        float combined = gripMultiplier * surfaceGripMultiplier;

        WheelFrictionCurve forward = baseForwardFriction;
        forward.stiffness = baseForwardFriction.stiffness * combined;
        wheelCollider.forwardFriction = forward;

        WheelFrictionCurve sideways = baseSidewaysFriction;
        sideways.stiffness = baseSidewaysFriction.stiffness * combined;
        wheelCollider.sidewaysFriction = sideways;
    }

    public void Update(float deltaTime)
    {
        if (wheelCollider.GetGroundHit(out WheelHit hit))
        {
            IsGrounded = true;

            GroundPoint = hit.point;
            GroundNormal = hit.normal;

            ContactVelocity =
                rigidbody.GetPointVelocity(hit.point);

            UpdateSurfaceInformation(hit.collider);
        }
        else
        {
            IsGrounded = false;

            GroundPoint = Vector3.zero;
            GroundNormal = Vector3.up;
            ContactVelocity = Vector3.zero;

            ClearSurfaceInformation();
        }
    }

    private void UpdateSurfaceInformation(Collider collider)
    {
        TrackSurface surface =
            collider.GetComponentInParent<TrackSurface>();

        if (surface == null)
        {
            ClearSurfaceInformation();
            return;
        }

        CurrentSurface = surface.SurfaceData;

        SetSurfaceGripMultiplier(
            CurrentSurface != null
                ? CurrentSurface.GripMultiplier
                : 1f
        );
    }

    private void ClearSurfaceInformation()
    {
        CurrentSurface = null;
        SetSurfaceGripMultiplier(1f);
    }

    public void GetVisualPose(
        out Vector3 position,
        out Quaternion rotation)
    {
        wheelCollider.GetWorldPose(out position, out rotation);
    }

    public Vector3 GetWheelForward()
    {
        Vector3 forward =
            Vector3.ProjectOnPlane(
                wheelCollider.transform.forward,
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
                wheelCollider.transform.right,
                GroundNormal
            );

        if (right.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        return right.normalized;
    }
}