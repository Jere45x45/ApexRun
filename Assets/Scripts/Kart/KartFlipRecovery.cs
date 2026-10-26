using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Endereza el kart si quedó volcado (dado vuelta o de costado): cuando lleva
/// un rato inclinado de más y casi quieto, lo pone derecho sobre la pista, en
/// el mismo lugar y mirando hacia donde iba, y se sigue manejando.
/// - Espera a que el kart se frene: mientras rueda o da vueltas no lo toca,
///   porque puede caer derecho solo.
/// - Volcado, frena enseguida: la carrocería tiene un material casi sin
///   rozamiento (para no engancharse en las paredes ni en los otros karts),
///   y dado vuelta patinaba sobre el asfalto como sobre hielo, cientos de
///   metros. Mientras está volcado, la carrocería pasa a rozar como plástico
///   y metal contra el asfalto; al enderezarse vuelve a su material.
/// - Solo en la computadora que simula el kart (el dueño); a las demás les
///   llega por red como un salto (NetworkTransform.Teleport).
/// </summary>
[RequireComponent(typeof(KartBehaviour))]
public class KartFlipRecovery : MonoBehaviour
{
    [SerializeField] private KartBehaviour kart;

    [SerializeField] private Rigidbody body;

    [Header("Cuándo está volcado")]
    [Tooltip("Inclinación respecto de la vertical a partir de la cual se lo considera volcado (grados). 90 = de costado.")]
    [SerializeField, Range(30f, 180f)] private float tiltAngle = 70f;

    [Tooltip("Velocidad por debajo de la cual se lo considera quieto (m/s).")]
    [SerializeField, Min(0f)] private float maxSpeed = 2f;

    [Tooltip("Velocidad de giro por debajo de la cual se lo considera quieto (rad/s).")]
    [SerializeField, Min(0f)] private float maxAngularSpeed = 1.5f;

    [Tooltip("Cuánto tiempo tiene que estar volcado y quieto antes de enderezarlo (s).")]
    [SerializeField, Min(0f)] private float delay = 1.5f;

    [Header("Volcado: rozamiento")]
    [Tooltip("Rozamiento de la carrocería contra el piso mientras está volcado (0 = hielo, 1 = goma). " +
             "El material normal de la carrocería tiene 0,05.")]
    [SerializeField, Range(0f, 1.5f)] private float flippedFriction = 0.8f;

    [Tooltip("Inclinación (grados) por debajo de la cual vuelve a estar derecho y la carrocería recupera su material.")]
    [SerializeField, Range(0f, 90f)] private float uprightAngle = 50f;

    [Header("Enderezado")]
    [Tooltip("Altura sobre la pista a la que se lo deja (m). El kart en reposo tiene el centro a 0,185 m: un poco más y cae suave sobre las ruedas.")]
    [SerializeField, Min(0.2f)] private float heightAboveGround = 0.4f;

    [Tooltip("Hasta dónde se busca la pista debajo del kart (m).")]
    [SerializeField, Min(0.5f)] private float groundSearchDistance = 4f;

    private float flippedTime;

    // Carrocería: sus colliders, el material que traen y el que usan volcados.
    private Collider[] bodyColliders = new Collider[0];
    private PhysicsMaterial[] bodyMaterials = new PhysicsMaterial[0];
    private PhysicsMaterial flippedMaterial;
    private bool rough;

    private void Reset()
    {
        kart = GetComponent<KartBehaviour>();
        body = GetComponent<Rigidbody>();
    }

    private void Awake()
    {
        if (kart == null)
            kart = GetComponent<KartBehaviour>();

        if (body == null)
            body = GetComponent<Rigidbody>();

        FindBodyColliders();
    }

    private void OnDisable()
    {
        SetRough(false);
    }

    private void OnDestroy()
    {
        if (flippedMaterial != null)
            Destroy(flippedMaterial);
    }

    private void FixedUpdate()
    {
        if (kart == null || body == null || !kart.IsSimulated || body.isKinematic)
        {
            flippedTime = 0f;
            SetRough(false);
            return;
        }

        float tilt = Vector3.Angle(body.rotation * Vector3.up, Vector3.up);
        bool flipped = tilt > tiltAngle;

        // Volcado roza contra el piso; derecho otra vez, vuelve a deslizar.
        if (flipped)
            SetRough(true);
        else if (tilt < uprightAngle)
            SetRough(false);

        bool still = body.linearVelocity.sqrMagnitude < maxSpeed * maxSpeed &&
                     body.angularVelocity.sqrMagnitude < maxAngularSpeed * maxAngularSpeed;

        if (!flipped || !still)
        {
            flippedTime = 0f;
            return;
        }

        flippedTime += Time.fixedDeltaTime;

        if (flippedTime >= delay)
        {
            flippedTime = 0f;
            Recover();
        }
    }

    /// <summary>Pone el kart derecho sobre la pista, donde está, mirando hacia donde iba.</summary>
    public void Recover()
    {
        Vector3 position = body.position;
        Vector3 groundNormal = Vector3.up;

        if (FindGround(position, out RaycastHit ground))
        {
            groundNormal = ground.normal;
            position = ground.point + groundNormal * heightAboveGround;
        }
        else
        {
            position += Vector3.up * heightAboveGround;
        }

        Quaternion rotation = Quaternion.LookRotation(GetHeading(groundNormal), groundNormal);

        NetworkTransform networkTransform = GetComponent<NetworkTransform>();

        if (networkTransform != null && networkTransform.IsSpawned && networkTransform.CanCommitToTransform)
        {
            // Las copias saltan directo, sin deslizarse desde donde estaba.
            networkTransform.Teleport(position, rotation, transform.localScale);
        }
        else
        {
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
        }

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        SetRough(false);

        // La física del kart guarda estado propio (giro de las ruedas, motor,
        // goma deformada): se pone a cero junto con el Rigidbody.
        kart.ResetMotion();
    }

    /// <summary>
    /// Colliders de la carrocería: los que tienen un material propio (el de
    /// la carrocería). Los demás (disparadores, ruedas) quedan como están.
    /// </summary>
    private void FindBodyColliders()
    {
        var colliders = new System.Collections.Generic.List<Collider>();
        var materials = new System.Collections.Generic.List<PhysicsMaterial>();

        foreach (Collider candidate in GetComponentsInChildren<Collider>(true))
        {
            if (candidate.isTrigger || candidate.sharedMaterial == null)
                continue;

            colliders.Add(candidate);
            materials.Add(candidate.sharedMaterial);
        }

        bodyColliders = colliders.ToArray();
        bodyMaterials = materials.ToArray();
    }

    /// <summary>Volcado (true): la carrocería roza contra el piso. Derecho (false): su material de siempre.</summary>
    private void SetRough(bool value)
    {
        if (rough == value)
            return;

        rough = value;

        if (value && flippedMaterial == null)
        {
            flippedMaterial = new PhysicsMaterial("KartVolcado")
            {
                dynamicFriction = flippedFriction,
                staticFriction = flippedFriction,
                bounciness = 0f,
                // Maximum manda sobre el material del piso: roza siempre así.
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }

        for (int i = 0; i < bodyColliders.Length; i++)
        {
            if (bodyColliders[i] != null)
                bodyColliders[i].sharedMaterial = value ? flippedMaterial : bodyMaterials[i];
        }
    }

    /// <summary>Hacia dónde mira el kart, sobre el plano de la pista.</summary>
    private Vector3 GetHeading(Vector3 groundNormal)
    {
        // Volcado, la trompa sigue apuntando para el mismo lado; de punta, se
        // usa hacia dónde apunta el techo.
        Vector3 heading = Vector3.ProjectOnPlane(body.rotation * Vector3.forward, groundNormal);

        if (heading.sqrMagnitude < 0.01f)
            heading = Vector3.ProjectOnPlane(body.rotation * Vector3.up, groundNormal);

        if (heading.sqrMagnitude < 0.01f)
            heading = Vector3.ProjectOnPlane(Vector3.forward, groundNormal);

        return heading.normalized;
    }

    /// <summary>Busca el piso debajo del kart (lo que no es un kart ni un trigger).</summary>
    private bool FindGround(Vector3 position, out RaycastHit ground)
    {
        ground = default;
        float best = float.MaxValue;
        Vector3 origin = position + Vector3.up * 1.5f;

        foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, groundSearchDistance + 1.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            // El propio kart y los demás karts no son piso.
            if (hit.rigidbody != null)
                continue;

            if (hit.distance < best)
            {
                best = hit.distance;
                ground = hit;
            }
        }

        return best < float.MaxValue;
    }
}
