using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Efectos de partículas de un kart:
/// - Humo: una rueda que desliza fuerte sobre el asfalto (derrape, patinar al
///   acelerar, bloquear al frenar). Con la pista mojada casi no sale.
/// - Tierra: una rueda que pisa fuera de pista (pasto, grava) a más de
///   dirtMinSpeedKmh. Terrones del color de la superficie y algo de polvo.
/// - Agua: con la pista mojada (TrackConditions.Wetness), las ruedas levantan
///   una llovizna tenue que crece con la velocidad (más las traseras). Al cruzar
///   un charco (WetPuddles, los mismos que dibuja el shader) salpican de verdad:
///   gotas que vuelan a los costados y caen, una nube de agua más grande y
///   anillos en el charco.
/// - Chispas: en los choques contra paredes u otros karts, y al raspar.
///
/// Qué superficie es asfalto y cuál tira tierra, y de qué color, lo dice cada
/// TrackSurfaceData (Efecto, Color de la tierra).
///
/// Karts propios y bots: se usa lo que calcula la física de cada rueda
/// (KartWheel). Karts de otros jugadores (su física corre en otra
/// computadora): el derrape sale del movimiento que se ve (velocidad de costado
/// de cada rueda) y la superficie de un rayo hacia abajo. No se manda nada
/// nuevo por red.
///
/// Todos los karts emiten en las mismas cinco partículas de la escena (se
/// crean solas). Lejos de la cámara se emite menos, y nada más allá de
/// maxDistance. KartBehaviour lo agrega solo.
///
/// Repetición (RaceReplay): cada rueda deja lo que está largando en
/// GetWheelFx (sin contar la distancia a la cámara), y las chispas avisan con
/// SparksEmitted. La repetición lo graba y lo vuelve a largar con
/// EmitWheelFx / EmitSparks.
/// </summary>
[DisallowMultipleComponent]
public class KartEffects : MonoBehaviour
{
    [Header("Humo (asfalto)")]
    [Tooltip("Velocidad con la que desliza la goma contra el piso (m/s) desde la que sale humo.")]
    [SerializeField, Min(0f)] private float smokeSlideMin = 3.5f;

    [Tooltip("Desde este deslizamiento (m/s) el humo sale completo.")]
    [SerializeField, Min(0f)] private float smokeSlideFull = 9f;

    [Tooltip("Partículas por segundo y por rueda con el humo completo.")]
    [SerializeField, Min(0f)] private float smokePerSecond = 36f;

    [Header("Tierra (fuera de pista)")]
    [SerializeField, Min(0f)] private float dirtMinSpeedKmh = 15f;

    [Tooltip("Partículas por segundo y por rueda a 50 km/h o más.")]
    [SerializeField, Min(0f)] private float dirtPerSecond = 90f;

    [Header("Agua (pista mojada)")]
    [Tooltip("Desde esta humedad de la pista (0 a 1) las ruedas levantan agua.")]
    [SerializeField, Range(0f, 1f)] private float sprayMinWetness = 0.15f;

    [SerializeField, Min(0f)] private float sprayMinSpeedKmh = 25f;

    [Tooltip("Partículas por segundo y por rueda trasera con la pista empapada y a fondo.")]
    [SerializeField, Min(0f)] private float sprayPerSecond = 45f;

    [Tooltip("Fuera de los charcos, la llovizna es esta parte de sprayPerSecond (el agua de verdad sale en los charcos).")]
    [SerializeField, Range(0f, 1f)] private float filmSpray = 0.4f;

    [Header("Charcos")]
    [Tooltip("Gotas por segundo y por rueda trasera al cruzar un charco lleno a 50 km/h o más.")]
    [SerializeField, Min(0f)] private float splashPerSecond = 220f;

    [Tooltip("Desde esta velocidad (km/h) un charco salpica.")]
    [SerializeField, Min(0f)] private float splashMinSpeedKmh = 4f;

    [Header("Chispas (choques)")]
    [Tooltip("Cambio de velocidad del golpe (m/s) desde el que saltan chispas.")]
    [SerializeField, Min(0f)] private float sparkMinSpeedChange = 1.5f;

    [Tooltip("Rozando una pared u otro kart a más de esto (m/s) salen chispas seguidas.")]
    [SerializeField, Min(0f)] private float scrapeMinSpeed = 4f;

    [Header("Distancia a la cámara")]
    [Tooltip("Hasta acá (m) se emite todo.")]
    [SerializeField, Min(1f)] private float fullDetailDistance = 25f;

    [Tooltip("Más lejos que esto (m) no se emite nada.")]
    [SerializeField, Min(1f)] private float maxDistance = 70f;

    // Con menos de esta aceleración (m/s²) un kart remoto no chocó: frenar o
    // doblar al límite da ~25.
    private const float RemoteImpactAcceleration = 45f;

    private const float FallbackTireRadius = 0.14f;

    /// <summary>
    /// Lo que larga una rueda ahora, en partículas por segundo (sin contar la
    /// distancia a la cámara). Lo graba la repetición.
    /// </summary>
    public struct WheelFx
    {
        public float Smoke;
        public float SmokeStrength;
        public float Dirt;
        public float Spray;
        public float Wetness;

        /// <summary>Gotas por segundo (cruzando un charco).</summary>
        public float Splash;

        /// <summary>Qué tan dentro de un charco está la rueda (0 a 1).</summary>
        public float Puddle;
        public Color32 DirtColor;

        /// <summary>Lo que levanta son piedritas (grava), no tierra.</summary>
        public bool Gravel;

        public bool IsEmpty => Smoke <= 0f && Dirt <= 0f && Spray <= 0f && Splash <= 0f;
    }

    /// <summary>Saltaron chispas: punto, normal, velocidad que arrastran, cantidad (sin la distancia) y fuerza (0 a 1).</summary>
    public static event System.Action<Vector3, Vector3, Vector3, int, float> SparksEmitted;

    private KartBehaviour kart;
    private Rigidbody body;

    // Lo que larga cada rueda ahora, y las partículas que quedaron "a medias"
    // (x humo, y tierra, z agua; se emiten enteras).
    private WheelFx[] wheelFx = new WheelFx[0];
    private Vector3[] carries = new Vector3[0];
    private float scrapeCarry;

    /// <summary>Lo que larga la rueda (FL, FR, RL, RR) ahora.</summary>
    public WheelFx GetWheelFx(int wheel) => wheel >= 0 && wheel < wheelFx.Length ? wheelFx[wheel] : default;

    // Karts remotos: superficie de cada rueda (por collider, para no buscarla siempre).
    private Collider[] remoteGround = new Collider[0];
    private TrackSurfaceData[] remoteSurface = new TrackSurfaceData[0];
    private readonly RaycastHit[] rayHits = new RaycastHit[8];

    // Karts remotos: movimiento que se ve.
    private bool hasLastPose;
    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private Vector3 remoteVelocity;
    private Vector3 remoteAngularVelocity;
    private float lastImpactTime = -10f;

    private struct WheelSample
    {
        public Vector3 Point;
        public Vector3 Normal;
        public Vector3 Velocity;
        public float Slide;
        public TrackSurfaceData Surface;
        public bool Rear;
    }

    private void Awake()
    {
        kart = GetComponent<KartBehaviour>();
        body = GetComponent<Rigidbody>();
    }

    private void OnDisable()
    {
        hasLastPose = false;
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        KartVehicle vehicle = kart != null ? kart.Vehicle : null;

        // En pausa (deltaTime 0) no se emite ni se mide nada.
        if (vehicle == null || body == null || deltaTime <= 0f)
            return;

        if (!kart.IsSimulated)
            TrackRemoteMotion(deltaTime);
        else
            hasLastPose = false;

        int count = vehicle.Wheels.Length;
        Resize(count);

        // Se calcula siempre (también lejos de la cámara) para la repetición;
        // se emite según la distancia.
        float detail = Detail(transform.position);
        bool emit = detail > 0f && Pool.Ensure();
        float wetness = Mathf.Clamp01(TrackConditions.Wetness);

        for (int i = 0; i < count; i++)
        {
            if (!Sample(vehicle, i, out WheelSample wheel))
            {
                wheelFx[i] = default;
                carries[i] = Vector3.zero;
                continue;
            }

            wheelFx[i] = Compute(wheel, wetness);

            if (emit)
                EmitWheelFx(wheelFx[i], wheel.Point, wheel.Normal, wheel.Velocity, ref carries[i], detail * deltaTime);
        }
    }

    /// <summary>
    /// Para la repetición de la tabla (que no tiene física): lo que largaría
    /// una rueda con esa superficie, velocidad del contacto y deslizamiento de costado.
    /// </summary>
    public WheelFx ComputeFor(TrackSurfaceData surface, Vector3 velocity, float slide, bool rear, float wetness)
    {
        var wheel = new WheelSample { Surface = surface, Velocity = velocity, Slide = slide, Rear = rear };
        return Compute(wheel, wetness);
    }

    /// <summary>Qué larga una rueda según su superficie, deslizamiento y velocidad (partículas por segundo).</summary>
    private WheelFx Compute(WheelSample wheel, float wetness)
    {
        var fx = new WheelFx { Wetness = wetness };
        float speedKmh = wheel.Velocity.magnitude * 3.6f;
        TrackSurfaceData.SurfaceEffect effect = TrackSurfaceData.EffectOf(wheel.Surface);

        if (effect == TrackSurfaceData.SurfaceEffect.None)
            return fx;

        bool offTrack = effect == TrackSurfaceData.SurfaceEffect.Dirt || effect == TrackSurfaceData.SurfaceEffect.Gravel;

        // Agua en un charco: gotas y una nube grande, desde poca velocidad.
        float puddle = WetPuddles.At(wheel.Point, wheel.Surface);
        fx.Puddle = puddle;

        if (puddle > 0.02f && speedKmh >= splashMinSpeedKmh)
        {
            float speed = Mathf.Max(Mathf.InverseLerp(splashMinSpeedKmh, 50f, speedKmh), 0.12f);
            float axle = wheel.Rear ? 1f : 0.6f;

            fx.Splash = splashPerSecond * puddle * speed * axle;
            fx.Spray = sprayPerSecond * 0.7f * puddle * speed * axle;
        }

        // Agua en el resto de la pista mojada: una llovizna tenue, a más velocidad.
        if (!offTrack && wetness >= sprayMinWetness && speedKmh >= sprayMinSpeedKmh)
        {
            float spray = Mathf.InverseLerp(sprayMinWetness, 1f, wetness) * Mathf.InverseLerp(sprayMinSpeedKmh, sprayMinSpeedKmh + 50f, speedKmh);
            fx.Spray += sprayPerSecond * filmSpray * Mathf.Max(spray, 0.1f) * (wheel.Rear ? 1f : 0.45f);
        }

        if (offTrack)
        {
            // Tierra o piedritas: con la velocidad, y más si además desliza.
            fx.Gravel = effect == TrackSurfaceData.SurfaceEffect.Gravel;

            if (speedKmh >= dirtMinSpeedKmh)
            {
                float amount = Mathf.Max(Mathf.InverseLerp(dirtMinSpeedKmh, 50f, speedKmh), 0.15f);
                amount *= 0.6f + 0.4f * Mathf.InverseLerp(1f, 6f, wheel.Slide);

                fx.Dirt = dirtPerSecond * amount;
                fx.DirtColor = TrackSurfaceData.DirtColorOf(wheel.Surface);
            }

            return fx;
        }

        // Humo: deslizando fuerte. El agua no deja que salga.
        if (wheel.Slide >= smokeSlideMin)
        {
            float smoke = Mathf.InverseLerp(smokeSlideMin, Mathf.Max(smokeSlideMin + 0.1f, smokeSlideFull), wheel.Slide);
            smoke *= (1f - 0.9f * wetness) * (1f - puddle);

            fx.SmokeStrength = smoke;
            fx.Smoke = smokePerSecond * Mathf.Max(smoke, 0.1f);
        }

        return fx;
    }

    /// <summary>
    /// Larga lo de una rueda en un punto. amount = segundos × detalle (lo que
    /// se larga es fx × amount). La usan los karts y la repetición.
    /// </summary>
    public static void EmitWheelFx(WheelFx fx, Vector3 point, Vector3 normal, Vector3 velocity, ref Vector3 carry, float amount)
    {
        if (fx.IsEmpty || amount <= 0f)
        {
            carry = Vector3.zero;
            return;
        }

        if (!Pool.Ensure())
            return;

        int smoke = Accumulate(ref carry.x, fx.Smoke * amount);

        for (int k = 0; k < smoke; k++)
            Pool.EmitSmoke(point, normal, velocity, fx.SmokeStrength);

        int dirt = Accumulate(ref carry.y, fx.Dirt * amount);

        for (int k = 0; k < dirt; k++)
            Pool.EmitDirt(point, normal, velocity, fx.DirtColor, fx.Gravel ? k % 3 == 0 : k % 2 == 0, fx.Gravel);

        // Agua: nube y gotas comparten la cuenta (se reparte según cuánto es cada una).
        int water = Accumulate(ref carry.z, (fx.Spray + fx.Splash) * amount);

        if (water <= 0)
            return;

        float dropShare = fx.Splash / Mathf.Max(0.0001f, fx.Spray + fx.Splash);

        for (int k = 0; k < water; k++)
        {
            if (Random.value < dropShare)
                Pool.EmitDrop(point, velocity, fx.Puddle);
            else
                Pool.EmitSpray(point, velocity, fx.Wetness, fx.Puddle);
        }

        // El charco se mueve donde pasó la rueda.
        if (fx.Puddle > 0.05f)
            WetPuddles.Disturb(point, fx.Puddle * Mathf.Clamp01(0.3f + velocity.magnitude / 8f));
    }

    /// <summary>Chispas (la usa la repetición): cantidad ya con la distancia.</summary>
    public static void EmitSparks(Vector3 point, Vector3 normal, Vector3 carry, int count, float strength)
    {
        if (count > 0 && Pool.Ensure())
            Pool.EmitSparks(point, normal, carry, count, strength);
    }

    // ------------------------------------------------------------------
    // Ruedas
    // ------------------------------------------------------------------

    private bool Sample(KartVehicle vehicle, int index, out WheelSample sample)
    {
        sample = default;
        KartWheel wheel = vehicle.Wheels[index];
        sample.Rear = wheel.LocalPosition.z < 0f;

        if (kart.IsSimulated)
        {
            if (!wheel.HasGrip || !vehicle.IsConfigured)
                return false;

            // Deslizamiento de la goma contra el piso: de costado y lo que la
            // rueda gira de más (acelerando) o de menos (frenando).
            float spin = wheel.AngularVelocity * vehicle.Tire.radius;
            float longitudinalSlide = wheel.LongitudinalVelocity - spin;

            sample.Slide = Mathf.Sqrt(wheel.LateralVelocity * wheel.LateralVelocity + longitudinalSlide * longitudinalSlide);
            sample.Normal = wheel.ContactNormal;
            sample.Surface = wheel.CurrentSurface;
            sample.Velocity = body.GetPointVelocity(wheel.ContactPoint) - wheel.GroundVelocity;

            // El contacto sale de la pose física; se pasa a la pose que se
            // dibuja (interpolada) para que no quede corrido a alta velocidad.
            Vector3 local = Quaternion.Inverse(body.rotation) * (wheel.ContactPoint - body.position);
            sample.Point = transform.TransformPoint(local);
            return true;
        }

        // Kart remoto: rayo hacia abajo desde el centro de la rueda.
        float radius = vehicle.IsConfigured ? vehicle.Tire.radius : FallbackTireRadius;
        Vector3 up = transform.up;
        Vector3 center = transform.TransformPoint(wheel.LocalPosition);

        if (!CastDown(center + up * 0.3f, -up, radius + 0.45f, out RaycastHit hit))
            return false;

        if (hit.collider != remoteGround[index])
        {
            remoteGround[index] = hit.collider;
            TrackSurface surface = hit.collider.GetComponentInParent<TrackSurface>();
            remoteSurface[index] = surface != null ? surface.SurfaceData : null;
        }

        sample.Point = hit.point;
        sample.Normal = hit.normal;
        sample.Surface = remoteSurface[index];
        sample.Velocity = remoteVelocity + Vector3.Cross(remoteAngularVelocity, hit.point - transform.position);

        float steer = sample.Rear ? 0f : kart.SteerAngle;
        Vector3 wheelForward = transform.rotation * Quaternion.Euler(0f, steer, 0f) * Vector3.forward;
        Vector3 wheelRight = Vector3.Cross(hit.normal, wheelForward).normalized;

        // Sin la física no se sabe si la rueda patina: solo cuenta el costado.
        sample.Slide = Mathf.Abs(Vector3.Dot(sample.Velocity, wheelRight));
        return true;
    }

    private bool CastDown(Vector3 origin, Vector3 direction, float length, out RaycastHit best)
    {
        best = default;
        int hits = Physics.RaycastNonAlloc(origin, direction, rayHits, length, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        float bestDistance = float.MaxValue;

        for (int i = 0; i < hits; i++)
        {
            RaycastHit hit = rayHits[i];

            if (hit.collider.attachedRigidbody == body || hit.distance >= bestDistance)
                continue;

            best = hit;
            bestDistance = hit.distance;
        }

        return bestDistance < float.MaxValue;
    }

    /// <summary>
    /// Kart remoto: velocidad y giro a partir de cómo se mueve en pantalla.
    /// Un frenazo imposible para las gomas es un choque: chispas.
    /// </summary>
    private void TrackRemoteMotion(float deltaTime)
    {
        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;

        if (!hasLastPose)
        {
            hasLastPose = true;
            lastPosition = position;
            lastRotation = rotation;
            remoteVelocity = Vector3.zero;
            remoteAngularVelocity = Vector3.zero;
            return;
        }

        Vector3 velocity = (position - lastPosition) / deltaTime;
        float blend = 1f - Mathf.Exp(-20f * deltaTime);

        if (velocity.sqrMagnitude > 60f * 60f)
        {
            // Lo reubicaron (reaparición): no es un movimiento.
            remoteVelocity = Vector3.zero;
            remoteAngularVelocity = Vector3.zero;
        }
        else
        {
            Vector3 previous = remoteVelocity;
            remoteVelocity = Vector3.Lerp(remoteVelocity, velocity, blend);

            (rotation * Quaternion.Inverse(lastRotation)).ToAngleAxis(out float angle, out Vector3 axis);

            if (angle > 180f)
                angle -= 360f;

            Vector3 angular = float.IsInfinity(axis.x) ? Vector3.zero : axis * (angle * Mathf.Deg2Rad / deltaTime);
            remoteAngularVelocity = Vector3.Lerp(remoteAngularVelocity, angular, blend);

            Vector3 change = remoteVelocity - previous;
            change.y = 0f;

            float acceleration = change.magnitude / deltaTime;

            if (acceleration > RemoteImpactAcceleration && previous.magnitude > 3f &&
                Time.time - lastImpactTime > 0.4f && !RecentImpactNear(position))
            {
                Vector3 push = change.normalized;
                Vector3 point = position - push * 0.9f + transform.up * 0.15f;
                Impact(body, point, push, acceleration * 0.08f);
            }
        }

        lastPosition = position;
        lastRotation = rotation;
    }

    // ------------------------------------------------------------------
    // Choques
    // ------------------------------------------------------------------

    private void OnCollisionEnter(Collision collision)
    {
        if (kart == null || !kart.IsSimulated || body == null || collision.contactCount == 0)
            return;

        ContactPoint contact = collision.GetContact(0);

        // Fuerza del golpe: la velocidad con la que se acercaban (el impulso
        // del primer contacto suele venir chico, repartido en varios pasos).
        float approach = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
        float speedChange = Mathf.Max(approach, collision.impulse.magnitude / Mathf.Max(1f, body.mass));

        if (speedChange < sparkMinSpeedChange)
            return;

        Impact(body, contact.point, contact.normal, speedChange);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (kart == null || !kart.IsSimulated || collision.contactCount == 0 || Time.deltaTime <= 0f)
            return;

        ContactPoint contact = collision.GetContact(0);
        Vector3 sliding = Vector3.ProjectOnPlane(collision.relativeVelocity, contact.normal);
        float speed = sliding.magnitude;

        if (speed < scrapeMinSpeed)
            return;

        // Se cuentan sin la distancia (para la repetición) y se larga cada
        // una con probabilidad según la distancia.
        int n = Accumulate(ref scrapeCarry, 90f * Mathf.InverseLerp(scrapeMinSpeed, 15f, speed) * Time.fixedDeltaTime);

        if (n <= 0)
            return;

        SparksEmitted?.Invoke(contact.point, contact.normal, body.linearVelocity, n, 0.6f);

        float detail = Detail(contact.point);
        int shown = 0;

        for (int k = 0; k < n; k++)
        {
            if (Random.value < detail)
                shown++;
        }

        if (shown > 0 && Pool.Ensure())
            Pool.EmitSparks(contact.point, contact.normal, body.linearVelocity, shown, 0.6f);
    }

    // Últimos choques (para no repetir las chispas de un mismo golpe).
    private static readonly List<Vector4> recentImpacts = new List<Vector4>();

    /// <summary>
    /// Chispas de un golpe. speedChange: cuánto cambió la velocidad del kart
    /// (m/s); más fuerte, más chispas. Lo usan las colisiones de la física y
    /// los choques entre karts de la red (KartRemoteContacts).
    /// </summary>
    public static void Impact(Rigidbody kartBody, Vector3 point, Vector3 normal, float speedChange)
    {
        KartEffects effects = kartBody != null ? kartBody.GetComponent<KartEffects>() : null;

        if (effects != null)
        {
            effects.lastImpactTime = Time.time;

            if (speedChange < effects.sparkMinSpeedChange)
                return;
        }

        recentImpacts.Add(new Vector4(point.x, point.y, point.z, Time.time));

        if (recentImpacts.Count > 8)
            recentImpacts.RemoveAt(0);

        int full = Mathf.RoundToInt(Mathf.Clamp(speedChange * 6f, 8f, 50f));
        Vector3 carry = kartBody != null ? kartBody.linearVelocity : Vector3.zero;
        float strength = Mathf.Clamp01(speedChange / 8f);

        // La repetición lo graba aunque acá no se vea (lejos de la cámara).
        SparksEmitted?.Invoke(point, normal, carry, full, strength);

        float detail = effects != null ? effects.Detail(point) : 1f;

        if (detail <= 0f || !Pool.Ensure())
            return;

        Pool.EmitSparks(point, normal, carry, Mathf.RoundToInt(full * Mathf.Max(0.3f, detail)), strength);
    }

    private static bool RecentImpactNear(Vector3 position)
    {
        foreach (Vector4 impact in recentImpacts)
        {
            if (Time.time - impact.w < 0.5f && (new Vector3(impact.x, impact.y, impact.z) - position).sqrMagnitude < 2.5f * 2.5f)
                return true;
        }

        return false;
    }

    // ------------------------------------------------------------------
    // Ayudas
    // ------------------------------------------------------------------

    /// <summary>1 cerca de la cámara, baja con la distancia y 0 más allá de maxDistance.</summary>
    private float Detail(Vector3 position)
    {
        Camera camera = Camera.main;

        if (camera == null)
            return 0f;

        return DetailFrom(camera.transform.position, position, fullDetailDistance, maxDistance);
    }

    /// <summary>Lo mismo desde cualquier cámara (la de la repetición), con las distancias por defecto.</summary>
    public static float DetailFrom(Vector3 cameraPosition, Vector3 position, float full = 25f, float max = 70f)
    {
        float distance = Vector3.Distance(cameraPosition, position);
        return 1f - Mathf.InverseLerp(full, Mathf.Max(full + 1f, max), distance);
    }

    private static int Accumulate(ref float carry, float amount)
    {
        if (amount <= 0f)
        {
            carry = 0f;
            return 0;
        }

        carry += amount;
        int whole = Mathf.FloorToInt(carry);
        carry -= whole;
        return whole;
    }

    private void Resize(int count)
    {
        if (wheelFx.Length == count)
            return;

        wheelFx = new WheelFx[count];
        carries = new Vector3[count];
        remoteGround = new Collider[count];
        remoteSurface = new TrackSurfaceData[count];
    }

    // ------------------------------------------------------------------
    // Partículas compartidas por todos los karts
    // ------------------------------------------------------------------

    private static class Pool
    {
        private const string MaterialPath = "Efectos/ParticulaKart";

        private static ParticleSystem smoke;
        private static ParticleSystem dirt;
        private static ParticleSystem spray;
        private static ParticleSystem drops;
        private static ParticleSystem sparks;
        private static Material material;
        private static int lightFrame = -1;
        private static float light = 1f;

        /// <summary>Crea las partículas en la escena actual si no están (se borran al cambiar de escena).</summary>
        public static bool Ensure()
        {
            if (smoke != null)
                return true;

            if (material == null)
                material = LoadMaterial();

            if (material == null)
                return false;

            var root = new GameObject("EfectosKarts");

            smoke = Create(root.transform, "Humo", 1500, gravity: -0.04f, drag: 2.5f, grow: 3.2f, stretch: false);
            dirt = Create(root.transform, "Tierra", 1500, gravity: 1f, drag: 0.4f, grow: 1f, stretch: false);
            spray = Create(root.transform, "Agua", 1500, gravity: 0.35f, drag: 1.8f, grow: 2.6f, stretch: false);
            drops = Create(root.transform, "Gotas", 2500, gravity: 1f, drag: 0.3f, grow: 0.7f, stretch: true);
            sparks = Create(root.transform, "Chispas", 600, gravity: 1.4f, drag: 0.6f, grow: 0.6f, stretch: true);
            return true;
        }

        public static void EmitSmoke(Vector3 point, Vector3 normal, Vector3 wheelVelocity, float strength)
        {
            float alpha = Mathf.Lerp(0.18f, 0.42f, strength);
            Color color = new Color(0.86f, 0.86f, 0.86f, alpha) * LightLevel();
            color.a = alpha;

            var emit = new ParticleSystem.EmitParams
            {
                position = point + normal * 0.12f + Random.insideUnitSphere * 0.1f,
                velocity = wheelVelocity * 0.2f + Vector3.up * Random.Range(0.3f, 0.9f) + Random.insideUnitSphere * 0.35f,
                startSize = Random.Range(0.45f, 0.8f),
                startLifetime = Random.Range(1.3f, 2.2f),
                startColor = color,
                rotation = Random.Range(0f, 360f),
                angularVelocity = Random.Range(-40f, 40f)
            };

            smoke.Emit(emit, 1);
        }

        public static void EmitDirt(Vector3 point, Vector3 normal, Vector3 wheelVelocity, Color surfaceColor, bool dust, bool gravel = false)
        {
            // Piedritas: cada una de un gris distinto, más chicas y tiradas más
            // lejos y para los costados que la tierra.
            float shade = gravel ? (Random.value < 0.5f ? Random.Range(0.35f, 0.6f) : Random.Range(1.2f, 1.6f)) : Random.Range(0.85f, 1.25f);
            Color color = surfaceColor * shade * LightLevel();
            color.a = 1f;

            var chunk = new ParticleSystem.EmitParams
            {
                position = point + normal * 0.05f + Random.insideUnitSphere * 0.08f,
                velocity = gravel
                    ? wheelVelocity * 0.35f + normal * Random.Range(2f, 5f) + Random.insideUnitSphere * 2f
                    : wheelVelocity * 0.2f + normal * Random.Range(1.5f, 3.5f) + Random.insideUnitSphere * 1f,
                startSize = gravel ? Random.Range(0.06f, 0.13f) : Random.Range(0.08f, 0.16f),
                startLifetime = gravel ? Random.Range(0.35f, 0.7f) : Random.Range(0.5f, 0.9f),
                startColor = color
            };

            dirt.Emit(chunk, 1);

            if (!dust)
                return;

            // Polvo: una nube clara del mismo color, en las partículas del humo
            // (la grava levanta menos y más gris).
            Color dustColor = Color.Lerp(surfaceColor, gravel ? new Color(0.78f, 0.77f, 0.74f) : new Color(0.8f, 0.75f, 0.65f), 0.5f) * LightLevel();
            dustColor.a = gravel ? 0.32f : 0.4f;

            var puff = new ParticleSystem.EmitParams
            {
                position = point + normal * 0.15f,
                velocity = wheelVelocity * 0.25f + Vector3.up * Random.Range(0.2f, 0.6f) + Random.insideUnitSphere * 0.3f,
                startSize = Random.Range(0.5f, 0.9f),
                startLifetime = Random.Range(0.9f, 1.4f),
                startColor = dustColor,
                rotation = Random.Range(0f, 360f)
            };

            smoke.Emit(puff, 1);
        }

        // Nube de agua: tenue sobre la pista mojada, más grande, alta y espesa en un charco.
        public static void EmitSpray(Vector3 point, Vector3 wheelVelocity, float wetness, float puddle)
        {
            Color color = new Color(0.88f, 0.9f, 0.93f) * LightLevel();
            color.a = Mathf.Lerp(0.08f, 0.2f, wetness) + 0.1f * puddle;

            float lift = puddle * Random.Range(0.3f, 1.2f) * Mathf.Clamp01(wheelVelocity.magnitude / 15f);

            var emit = new ParticleSystem.EmitParams
            {
                position = point + Vector3.up * 0.1f + Random.insideUnitSphere * 0.08f,
                velocity = wheelVelocity * 0.4f + Vector3.up * (Random.Range(0.8f, 2f) + lift) + Random.insideUnitSphere * (0.7f + puddle),
                startSize = Random.Range(0.4f, 0.8f) * (1f + 0.35f * puddle),
                startLifetime = Random.Range(0.5f, 0.85f),
                startColor = color,
                rotation = Random.Range(0f, 360f)
            };

            spray.Emit(emit, 1);
        }

        // Gota de un charco: sale para un costado y para arriba (más rápido cuanto
        // más rápido va la rueda) y cae. Se dibuja estirada según su velocidad.
        public static void EmitDrop(Vector3 point, Vector3 wheelVelocity, float puddle)
        {
            float speed = wheelVelocity.magnitude;
            Vector3 forward = speed > 0.1f ? wheelVelocity / speed : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, forward);

            if (side.sqrMagnitude < 0.01f)
                side = Vector3.right;

            side.Normalize();

            if (Random.value < 0.5f)
                side = -side;

            float push = Mathf.Clamp(speed, 2f, 22f);
            Color color = new Color(0.82f, 0.87f, 0.93f) * LightLevel();
            color.a = Random.Range(0.45f, 0.8f);

            var drop = new ParticleSystem.EmitParams
            {
                position = point + Vector3.up * 0.03f + side * 0.05f + Random.insideUnitSphere * 0.06f,
                velocity = wheelVelocity * Random.Range(0.2f, 0.55f)
                    + side * push * Random.Range(0.15f, 0.35f)
                    + Vector3.up * Random.Range(1.4f, 3f) * (0.6f + push / 22f) * (0.7f + 0.3f * puddle),
                startSize = Random.Range(0.035f, 0.08f),
                startLifetime = Random.Range(0.35f, 0.75f),
                startColor = color
            };

            drops.Emit(drop, 1);
        }

        public static void EmitSparks(Vector3 point, Vector3 normal, Vector3 carry, int count, float strength)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 direction = (normal + Random.insideUnitSphere * 0.9f + Vector3.up * 0.3f).normalized;

                var emit = new ParticleSystem.EmitParams
                {
                    position = point + Random.insideUnitSphere * 0.05f,
                    velocity = carry * 0.5f + direction * Random.Range(2.5f, 5f + 5f * strength),
                    startSize = Random.Range(0.045f, 0.08f),
                    startLifetime = Random.Range(0.2f, 0.5f),
                    startColor = Color.Lerp(new Color(1f, 0.95f, 0.55f), new Color(1f, 0.55f, 0.12f), Random.value)
                };

                sparks.Emit(emit, 1);
            }
        }

        /// <summary>
        /// Las partículas no reciben luz: de noche o con el cielo cubierto se
        /// oscurecen según el sol, para que el humo no brille.
        /// </summary>
        private static float LightLevel()
        {
            if (lightFrame == Time.frameCount)
                return light;

            lightFrame = Time.frameCount;
            Light sun = RenderSettings.sun;
            light = sun != null && sun.isActiveAndEnabled ? Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(sun.intensity)) : 1f;
            return light;
        }

        private static ParticleSystem Create(Transform parent, string name, int maxParticles, float gravity, float drag, float grow, bool stretch)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            ParticleSystem system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            ParticleSystem.LimitVelocityOverLifetimeModule limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f;
            limit.drag = drag;

            // Crece de a poco (humo, agua) y se desvanece al final.
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, grow));

            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(stretch ? 1f : 0f, 0f), new GradientAlphaKey(1f, stretch ? 0.6f : 0.12f), new GradientAlphaKey(0f, 1f) });

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = fade;

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;

            if (stretch)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.05f;
                renderer.lengthScale = 2f;
            }

            system.Play();
            return system;
        }

        private static Material LoadMaterial()
        {
            Material loaded = Resources.Load<Material>(MaterialPath);

            if (loaded != null)
                return loaded;

            Debug.LogWarning("KartEffects: falta el material Resources/" + MaterialPath + ". No hay partículas.");
            return null;
        }
    }
}
