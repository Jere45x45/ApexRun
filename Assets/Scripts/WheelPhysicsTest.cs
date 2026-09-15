using UnityEngine;

public class WheelPhysicsTest : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private Transform wheelPoint;

    [Header("Wheel")]
    [SerializeField]
    [Min(0.01f)]
    private float radius = 0.25f;

    [SerializeField]
    [Min(0f)]
    private float suspensionDistance = 0.2f;

    [SerializeField]
    [Min(0f)]
    private float springRate = 6000f;

    [SerializeField]
    [Min(0f)]
    private float damperRate = 600f;

    private Rigidbody rb;
    private WheelPhysics wheelPhysics;

    public WheelPhysics Wheel =>
        wheelPhysics;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (rb == null)
        {
            Debug.LogError(
                "WheelPhysicsTest necesita un Rigidbody.",
                this
            );

            enabled = false;
            return;
        }

        if (wheelPoint == null)
        {
            Debug.LogError(
                "WheelPhysicsTest no tiene un Wheel Point asignado.",
                this
            );

            enabled = false;
            return;
        }

        wheelPhysics =
            new WheelPhysics(
                rb,
                wheelPoint
            );

        wheelPhysics.Configure(
            radius,
            suspensionDistance,
            springRate,
            damperRate
        );
    }

    private void FixedUpdate()
    {
        if (wheelPhysics == null)
            return;

        float deltaTime =
            Time.fixedDeltaTime;

        wheelPhysics.Update(
            deltaTime
        );

        wheelPhysics.ApplySuspensionForce(
            deltaTime
        );
    }

    private void OnDrawGizmos()
    {
        if (wheelPoint == null)
            return;

        float rayLength =
            suspensionDistance + radius;

        Gizmos.color =
            wheelPhysics != null &&
            wheelPhysics.IsGrounded
                ? Color.green
                : Color.red;

        Gizmos.DrawLine(
            wheelPoint.position,
            wheelPoint.position -
            wheelPoint.up * rayLength
        );

        if (wheelPhysics != null &&
            wheelPhysics.IsGrounded)
        {
            Gizmos.DrawSphere(
                wheelPhysics.GroundPoint,
                0.04f
            );

            Gizmos.DrawLine(
                wheelPhysics.GroundPoint,
                wheelPhysics.GroundPoint +
                wheelPhysics.GroundNormal * 0.25f
            );
        }
    }

    private void OnGUI()
    {
        if (wheelPhysics == null)
            return;

        GUILayout.BeginArea(
            new Rect(
                20f,
                20f,
                300f,
                160f
            )
        );

        GUILayout.Label(
            $"Grounded: {wheelPhysics.IsGrounded}"
        );

        GUILayout.Label(
            $"Compression: {wheelPhysics.Compression:F3}"
        );

        GUILayout.Label(
            $"Ray Length: {wheelPhysics.RayLength:F3}"
        );

        GUILayout.Label(
            $"Ground Point: {wheelPhysics.GroundPoint}"
        );

        GUILayout.Label(
            $"Ground Normal: {wheelPhysics.GroundNormal}"
        );

        GUILayout.EndArea();
    }
}