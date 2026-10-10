using System;
using UnityEngine;

/// <summary>
/// Una rueda del kart: contacto con el piso y fuerzas del neumático.
///
/// 1. Contacto (UpdateContact): el neumático es una esfera; Unity calcula la
///    dirección y profundidad exactas de cada contacto y se aplica la fuerza
///    vertical (resorte + amortiguador). Empuja hacia arriba en el piso,
///    inclinada en una rampa y hacia atrás contra la cara de un escalón.
///
/// 2. Agarre (BeginTireStep → subpasos → ApplyTireForces): la goma se modela
///    como un resorte que se deforma en dos direcciones (de costado y hacia
///    adelante/atrás). La deformación crece cuando la rueda patina y se
///    "relaja" a medida que rueda. La fuerza sale de la Magic Formula de
///    Pacejka aplicada a esa deformación. Con el kart quieto, la goma
///    deformada lo sostiene como un resorte: no se arrastra en pendiente.
///    Las dos direcciones comparten un único agarre (círculo de fricción):
///    una rueda que patina acelerando o bloqueada frenando pierde agarre lateral.
///
/// El giro de la rueda no vive acá: lo maneja el KartAxle al que pertenece
/// (las traseras de un kart comparten un eje rígido).
/// </summary>
public class KartWheel
{
    private const int MaxContacts = 16;

    private readonly Collider[] overlapBuffer = new Collider[MaxContacts];
    private readonly SphereCollider tireCollider;

    // Ejes de la rueda sobre el piso y fuerzas acumuladas en este paso.
    private Vector3 forwardAxis;
    private Vector3 rightAxis;
    private float lateralImpulse;
    private float longitudinalImpulse;
    private float substepDampingCoefficient;
    private float substepLateralForce;

    // Último collider pisado: la superficie solo se busca de nuevo si cambia.
    private Collider surfaceCollider;

    public string Name { get; }

    /// <summary>Centro de la rueda, en coordenadas locales del kart.</summary>
    public Vector3 LocalPosition { get; }

    /// <summary>
    /// Desplazamiento del centro de la rueda respecto de LocalPosition, en
    /// coordenadas del kart. Lo calcula la geometría de la dirección: al girar,
    /// cada mangueta sube o baja su rueda.
    /// </summary>
    public Vector3 GeometryOffset { get; set; }

    /// <summary>Ángulo de dirección en grados (positivo = hacia la derecha).</summary>
    public float SteerAngle { get; set; }

    // ---------- Contacto ----------

    public bool IsGrounded { get; private set; }

    /// <summary>Punto de contacto principal (el más aplastado).</summary>
    public Vector3 ContactPoint { get; private set; }

    /// <summary>Dirección en la que empuja el piso.</summary>
    public Vector3 ContactNormal { get; private set; } = Vector3.up;

    /// <summary>Cuánto está aplastado el neumático (m).</summary>
    public float Deflection { get; private set; }

    /// <summary>Fuerza total que el piso hace sobre la rueda (N).</summary>
    public float NormalForce { get; private set; }

    /// <summary>
    /// Parte de la fuerza normal que sostiene al kart desde abajo (N).
    /// Es la que genera agarre: un golpe contra una pared no da agarre.
    /// </summary>
    public float VerticalLoad { get; private set; }

    public Collider GroundCollider { get; private set; }

    /// <summary>Velocidad del piso en el punto de contacto (otro kart, plataforma).</summary>
    public Vector3 GroundVelocity { get; private set; }

    /// <summary>True si la rueda está apoyada y puede generar agarre.</summary>
    public bool HasGrip => IsGrounded && VerticalLoad > 0f;

    // ---------- Superficie ----------

    /// <summary>Superficie que pisa la rueda (pista, pasto...). Null si el piso no tiene TrackSurface.</summary>
    public TrackSurfaceData CurrentSurface { get; private set; }

    /// <summary>Multiplica el agarre del neumático según la superficie (1 = asfalto normal).</summary>
    public float SurfaceGripMultiplier =>
        CurrentSurface != null ? Mathf.Max(0f, CurrentSurface.GripMultiplier) : 1f;

    /// <summary>True si la rueda está apoyada fuera de los límites de pista.</summary>
    public bool IsOnInvalidSurface =>
        IsGrounded && CurrentSurface != null && !CurrentSurface.IsValidForTrack;

    /// <summary>Carga que genera agarre: la vertical corregida por la superficie (N).</summary>
    private float GripLoad => VerticalLoad * SurfaceGripMultiplier;

    // ---------- Neumático ----------

    /// <summary>Velocidad del punto de contacto en la dirección en que apunta la rueda (m/s).</summary>
    public float LongitudinalVelocity { get; private set; }

    /// <summary>Velocidad del punto de contacto de costado (m/s).</summary>
    public float LateralVelocity { get; private set; }

    /// <summary>Deformación lateral de la goma (m).</summary>
    public float LateralDeflection { get; private set; }

    /// <summary>Deformación longitudinal de la goma (m).</summary>
    public float LongitudinalDeflection { get; private set; }

    /// <summary>Velocidad de giro de la rueda (rad/s), copiada de su eje.</summary>
    public float AngularVelocity { get; private set; }

    /// <summary>Ángulo de deslizamiento en grados.</summary>
    public float SlipAngle { get; private set; }

    /// <summary>Patinamiento: positivo = traccionando (gira más rápido que el piso), negativo = frenando.</summary>
    public float SlipRatio { get; private set; }

    /// <summary>Fuerza lateral del neumático (N). Positiva = hacia la derecha de la rueda.</summary>
    public float LateralForce { get; private set; }

    /// <summary>Fuerza longitudinal del neumático (N). Positiva = empuja hacia adelante.</summary>
    public float LongitudinalForce { get; private set; }

    /// <summary>Cuánto del agarre disponible está usando, de 0 a 1 (1 = al límite).</summary>
    public float GripUsage { get; private set; }

    /// <summary>Fuerza del resorte longitudinal en el subpaso actual (la usa el eje).</summary>
    public float SubstepSpringForce { get; private set; }

    /// <summary>Amortiguación longitudinal de la goma en el subpaso actual (la usa el eje).</summary>
    public float SubstepDampingCoefficient => substepDampingCoefficient;

    public KartWheel(string name, SphereCollider tireCollider)
    {
        if (tireCollider == null)
            throw new ArgumentNullException(nameof(tireCollider));

        if (!tireCollider.isTrigger)
            throw new ArgumentException(
                "El collider del neumático tiene que ser trigger: " +
                "solo se usa para medir el contacto, no para chocar.",
                nameof(tireCollider));

        Name = name;
        this.tireCollider = tireCollider;
        LocalPosition = tireCollider.transform.localPosition;
    }

    public void ApplySettings(TireSettings tire)
    {
        if (tire == null)
            throw new ArgumentNullException(nameof(tire));

        tireCollider.radius = tire.radius;
        tireCollider.center = Vector3.zero;
    }

    /// <summary>Borra la deformación de la goma (por ejemplo, al reubicar el kart).</summary>
    public void ResetTireState()
    {
        LateralDeflection = 0f;
        LongitudinalDeflection = 0f;
    }

    // =====================================================================
    // 1. Contacto
    // =====================================================================

    /// <summary>
    /// Detecta el contacto y aplica la fuerza vertical del neumático.
    /// Se llama una vez por paso de física, antes del agarre.
    /// </summary>
    public void UpdateContact(Rigidbody body, TireSettings tire)
    {
        // Usamos la pose física del Rigidbody, no la del Transform:
        // con interpolación activada el Transform muestra una pose
        // intermedia, pensada para el render.
        Vector3 center = body.position + body.rotation * (LocalPosition + GeometryOffset);

        IsGrounded = false;
        Deflection = 0f;
        NormalForce = 0f;
        VerticalLoad = 0f;
        ContactNormal = Vector3.up;
        ContactPoint = center - Vector3.up * tire.radius;
        GroundCollider = null;
        GroundVelocity = Vector3.zero;

        int count = Physics.OverlapSphereNonAlloc(
            center,
            tire.radius,
            overlapBuffer,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < count; i++)
        {
            Collider ground = overlapBuffer[i];

            if (ground.attachedRigidbody == body)
                continue;

            // Capas que no chocan entre sí (Physics → Layer Collision Matrix), por
            // ejemplo los bots de entrenamiento: tampoco se apoyan uno sobre otro.
            if (Physics.GetIgnoreLayerCollision(body.gameObject.layer, ground.gameObject.layer))
                continue;

            bool touching = Physics.ComputePenetration(
                tireCollider,
                center,
                body.rotation,
                ground,
                ground.transform.position,
                ground.transform.rotation,
                out Vector3 normal,
                out float depth
            );

            if (!touching || depth <= 0f)
                continue;

            ApplyContactForce(body, ground, tire, center, normal, depth);
        }

        if (IsGrounded)
        {
            Vector3 kartUp = body.rotation * Vector3.up;

            VerticalLoad =
                NormalForce * Mathf.Max(0f, Vector3.Dot(ContactNormal, kartUp));
        }

        UpdateSurface();
    }

    /// <summary>Busca la TrackSurface del piso que pisa la rueda (solo si cambió de collider).</summary>
    private void UpdateSurface()
    {
        if (GroundCollider == surfaceCollider)
            return;

        surfaceCollider = GroundCollider;

        TrackSurface surface = GroundCollider != null
            ? GroundCollider.GetComponentInParent<TrackSurface>()
            : null;

        CurrentSurface = surface != null ? surface.SurfaceData : null;
    }

    private void ApplyContactForce(
        Rigidbody body,
        Collider ground,
        TireSettings tire,
        Vector3 center,
        Vector3 normal,
        float depth)
    {
        Vector3 point = center - normal * (tire.radius - depth);

        // Velocidad con la que la rueda se acerca al piso en este punto.
        // Si el piso se mueve (otro kart), se usa la velocidad relativa.
        Vector3 groundVelocity = ground.attachedRigidbody != null
            ? ground.attachedRigidbody.GetPointVelocity(point)
            : Vector3.zero;

        Vector3 relativeVelocity = body.GetPointVelocity(point) - groundVelocity;

        float approachSpeed = -Vector3.Dot(relativeVelocity, normal);

        float springForce = tire.verticalStiffness * depth;

        if (depth > tire.maxDeflection)
        {
            springForce +=
                tire.verticalStiffness *
                (tire.bottomOutStiffnessMultiplier - 1f) *
                (depth - tire.maxDeflection);
        }

        float dampingForce = tire.verticalDamping * approachSpeed;

        // Un neumático empuja, nunca tira del piso.
        float force = Mathf.Max(0f, springForce + dampingForce);

        body.AddForceAtPosition(normal * force, point, ForceMode.Force);

        IsGrounded = true;
        NormalForce += force;

        if (depth > Deflection)
        {
            Deflection = depth;
            ContactPoint = point;
            ContactNormal = normal;
            GroundCollider = ground;
            GroundVelocity = groundVelocity;
        }
    }

    // =====================================================================
    // 2. Agarre
    // =====================================================================

    /// <summary>
    /// Prepara el cálculo del agarre para este paso de física: ejes de la
    /// rueda sobre el piso y velocidad del punto de contacto.
    /// </summary>
    public void BeginTireStep(Rigidbody body)
    {
        lateralImpulse = 0f;
        longitudinalImpulse = 0f;
        LongitudinalVelocity = 0f;
        LateralVelocity = 0f;

        if (!HasGrip)
        {
            // En el aire la goma vuelve a su forma.
            ResetTireState();
            SlipAngle = 0f;
            SlipRatio = 0f;
            return;
        }

        Quaternion wheelRotation = body.rotation * Quaternion.Euler(0f, SteerAngle, 0f);
        Vector3 forward = Vector3.ProjectOnPlane(wheelRotation * Vector3.forward, ContactNormal);

        if (forward.sqrMagnitude < 0.0001f)
        {
            forwardAxis = Vector3.zero;
            rightAxis = Vector3.zero;
            return;
        }

        forwardAxis = forward.normalized;
        rightAxis = Vector3.Cross(ContactNormal, forwardAxis);

        Vector3 velocity = body.GetPointVelocity(ContactPoint) - GroundVelocity;

        LongitudinalVelocity = Vector3.Dot(velocity, forwardAxis);
        LateralVelocity = Vector3.Dot(velocity, rightAxis);
    }

    /// <summary>
    /// Subpaso, parte 1 (antes de que el eje actualice su giro):
    /// deforma la goma en las dos direcciones y calcula las fuerzas con el
    /// círculo de fricción. Deja preparada la fuerza longitudinal para el eje.
    /// </summary>
    public void UpdateDeflections(float axleAngularVelocity, float substepTime, TireSettings tire)
    {
        SubstepSpringForce = 0f;
        substepDampingCoefficient = 0f;
        substepLateralForce = 0f;

        if (!HasGrip || forwardAxis == Vector3.zero)
            return;

        // El agarre depende de la carga y de la superficie (pasto, tierra...).
        float load = GripLoad;
        float wheelSurfaceSpeed = axleAngularVelocity * tire.radius;
        float patchSpeed = Mathf.Sqrt(
            LongitudinalVelocity * LongitudinalVelocity +
            LateralVelocity * LateralVelocity
        );

        // La amortiguación de la goma solo actúa casi quieto.
        float lowSpeedFactor = 1f - Mathf.Clamp01(
            Mathf.Max(patchSpeed, Mathf.Abs(wheelSurfaceSpeed)) / tire.lowSpeedDampingSpeed
        );

        // ---- Deformación lateral ----
        // La goma se deforma con el deslizamiento lateral y se relaja al rodar.
        // Integración implícita: estable aunque la relajación sea muy rápida.
        float sigmaY = tire.lateralRelaxationLength;

        LateralDeflection =
            (LateralDeflection + LateralVelocity * substepTime) /
            (1f + patchSpeed * substepTime / sigmaY);

        // Cuánto se puede deformar la goma: patinando de costado, hasta σ (90°).
        // Casi quieto, solo hasta el punto de máximo agarre: más allá la goma
        // ya no se estira, desliza. Si no, al detenerse quedaría "cargada"
        // como un resorte estirado y el kart pegaría un tirón hacia atrás.
        float maxLateralDeflection = sigmaY * Mathf.Lerp(1f, Mathf.Sin(tire.LateralPeakSlipAngle), lowSpeedFactor);
        LateralDeflection = Mathf.Clamp(LateralDeflection, -maxLateralDeflection, maxLateralDeflection);

        // En régimen, deformación/σ = sen(ángulo de deslizamiento).
        float slipAngle = Mathf.Asin(LateralDeflection / sigmaY);
        SlipAngle = slipAngle * Mathf.Rad2Deg;

        // ---- Deformación longitudinal ----
        // Velocidad de deslizamiento: piso respecto de la superficie de la goma.
        float sigmaX = tire.longitudinalRelaxationLength;
        float slipVelocity = LongitudinalVelocity - wheelSurfaceSpeed;
        float relaxationSpeed = Mathf.Max(Mathf.Abs(LongitudinalVelocity), Mathf.Abs(wheelSurfaceSpeed));

        LongitudinalDeflection =
            (LongitudinalDeflection + slipVelocity * substepTime) /
            (1f + relaxationSpeed * substepTime / sigmaX);

        float maxLongitudinalDeflection = sigmaX * Mathf.Lerp(1f, tire.LongitudinalPeakSlipRatio, lowSpeedFactor);
        LongitudinalDeflection = Mathf.Clamp(LongitudinalDeflection, -maxLongitudinalDeflection, maxLongitudinalDeflection);

        float slipRatio = -LongitudinalDeflection / sigmaX;
        SlipRatio = slipRatio;

        // ---- Círculo de fricción ----
        // Un solo presupuesto de agarre para las dos direcciones.
        tire.EvaluateCombined(
            slipRatio,
            slipAngle,
            out float longitudinalCoefficient,
            out float lateralCoefficient
        );

        SubstepSpringForce = longitudinalCoefficient * load;

        substepDampingCoefficient =
            tire.CarcassDamping(load, tire.LongitudinalStiffnessPerLoad, sigmaX) *
            lowSpeedFactor;

        float lateralDamping =
            -tire.CarcassDamping(load, tire.LateralStiffnessPerLoad, sigmaY) *
            lowSpeedFactor *
            LateralVelocity;

        substepLateralForce = -lateralCoefficient * load + lateralDamping;
    }

    /// <summary>
    /// Subpaso, parte 2 (con el giro del eje ya actualizado):
    /// fuerzas finales de este subpaso, dentro de la elipse de fricción.
    /// </summary>
    public void FinishSubstep(float axleAngularVelocity, float substepTime, TireSettings tire)
    {
        AngularVelocity = axleAngularVelocity;

        if (!HasGrip || forwardAxis == Vector3.zero)
        {
            GripUsage = 0f;
            return;
        }

        float slipVelocity = LongitudinalVelocity - axleAngularVelocity * tire.radius;
        float longitudinalForce = SubstepSpringForce - substepDampingCoefficient * slipVelocity;
        float lateralForce = substepLateralForce;

        // La fuerza total no puede salir de la elipse de fricción (cada dirección
        // con su μ). Esto también limita la amortiguación de baja velocidad.
        float maxLongitudinal = tire.longitudinalFriction * GripLoad;
        float maxLateral = tire.lateralFriction * GripLoad;

        float usageX = maxLongitudinal > 0f ? longitudinalForce / maxLongitudinal : 0f;
        float usageY = maxLateral > 0f ? lateralForce / maxLateral : 0f;
        float usage = Mathf.Sqrt(usageX * usageX + usageY * usageY);

        if (usage > 1f)
        {
            longitudinalForce /= usage;
            lateralForce /= usage;
            usage = 1f;
        }

        GripUsage = usage;

        longitudinalImpulse += longitudinalForce * substepTime;
        lateralImpulse += lateralForce * substepTime;
    }

    /// <summary>
    /// Aplica al chasis la fuerza promedio de todos los subpasos.
    /// Se llama una vez por paso de física, al final.
    /// </summary>
    public void ApplyTireForces(Rigidbody body, float deltaTime)
    {
        LateralForce = lateralImpulse / deltaTime;
        LongitudinalForce = longitudinalImpulse / deltaTime;

        if (!HasGrip || forwardAxis == Vector3.zero)
            return;

        Vector3 force = forwardAxis * LongitudinalForce + rightAxis * LateralForce;

        body.AddForceAtPosition(force, ContactPoint, ForceMode.Force);
    }
}
