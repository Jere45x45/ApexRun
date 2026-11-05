using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Efectos de partículas de un kart:
/// - Humo: una rueda que desliza fuerte sobre el asfalto (derrape, patinar al
///   acelerar, bloquear al frenar). Con la pista mojada casi no sale.
/// - Tierra: una rueda que pisa fuera de pista (pasto, grava) a más de
///   dirtMinSpeedKmh. Terrones del color de la superficie y algo de polvo.
/// - Agua: con la pista mojada (TrackConditions.Wetness), las ruedas levantan
///   una cortina de agua que crece con la velocidad (más las traseras).
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
/// Todos los karts emiten en las mismas cuatro partículas de la escena (se
/// crean solas). Lejos de la cámara se emite menos, y nada más allá de
/// maxDistance. KartBehaviour lo agrega solo.
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

    private KartBehaviour kart;
    private Rigidbody body;

    // Partículas que quedaron "a medias" por rueda (se emiten enteras).
    private float[] smokeCarry = new float[0];
    private float[] dirtCarry = new float[0];
    private float[] sprayCarry = new float[0];
    private float scrapeCarry;

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

        float detail = Detail(transform.position);

        if (detail <= 0f || !Pool.Ensure())
            return;

        int count = vehicle.Wheels.Length;
        Resize(count);

        float wetness = Mathf.Clamp01(TrackConditions.Wetness);

        for (int i = 0; i < count; i++)
        {
            if (!Sample(vehicle, i, out WheelSample wheel))
            {
                smokeCarry[i] = dirtCarry[i] = sprayCarry[i] = 0f;
                continue;
            }

            float speedKmh = wheel.Velocity.magnitude * 3.6f;
            TrackSurfaceData.SurfaceEffect effect = TrackSurfaceData.EffectOf(wheel.Surface);

            if (effect == TrackSurfaceData.SurfaceEffect.None)
            {
                smokeCarry[i] = dirtCarry[i] = sprayCarry[i] = 0f;
                continue;
            }

            if (effect == TrackSurfaceData.SurfaceEffect.Dirt)
            {
                // Tierra: con la velocidad, y más si además desliza.
                float amount = Mathf.InverseLerp(dirtMinSpeedKmh, 50f, speedKmh);

                if (speedKmh >= dirtMinSpeedKmh)
                    amount = Mathf.Max(amount, 0.15f) * (0.6f + 0.4f * Mathf.InverseLerp(1f, 6f, wheel.Slide));

                int n = Accumulate(ref dirtCarry[i], dirtPerSecond * amount * detail * deltaTime);

                for (int k = 0; k < n; k++)
                    Pool.EmitDirt(wheel.Point, wheel.Normal, wheel.Velocity, TrackSurfaceData.DirtColorOf(wheel.Surface), k % 2 == 0);
            }
            else
            {
                dirtCarry[i] = 0f;

                // Humo: deslizando fuerte. El agua no deja que salga.
                float smoke = Mathf.InverseLerp(smokeSlideMin, Mathf.Max(smokeSlideMin + 0.1f, smokeSlideFull), wheel.Slide);
                smoke *= 1f - 0.9f * wetness;

                int n = wheel.Slide >= smokeSlideMin
                    ? Accumulate(ref smokeCarry[i], smokePerSecond * Mathf.Max(smoke, 0.1f) * detail * deltaTime)
                    : Accumulate(ref smokeCarry[i], 0f);

                for (int k = 0; k < n; k++)
                    Pool.EmitSmoke(wheel.Point, wheel.Normal, wheel.Velocity, smoke);

                // Agua: según qué tan mojada esté y la velocidad.
                float spray = wetness >= sprayMinWetness && speedKmh >= sprayMinSpeedKmh
                    ? Mathf.InverseLerp(sprayMinWetness, 1f, wetness) * Mathf.InverseLerp(sprayMinSpeedKmh, sprayMinSpeedKmh + 50f, speedKmh)
                    : 0f;

                spray = Mathf.Max(spray, spray > 0f ? 0.1f : 0f) * (wheel.Rear ? 1f : 0.45f);

                int m = Accumulate(ref sprayCarry[i], sprayPerSecond * spray * detail * deltaTime);

                for (int k = 0; k < m; k++)
                    Pool.EmitSpray(wheel.Point, wheel.Velocity, wetness);
            }
        }
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

        float detail = Detail(contact.point);
        int n = Accumulate(ref scrapeCarry, 90f * Mathf.InverseLerp(scrapeMinSpeed, 15f, speed) * detail * Time.fixedDeltaTime);

        if (n > 0 && Pool.Ensure())
            Pool.EmitSparks(contact.point, contact.normal, body.linearVelocity, n, 0.6f);
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

        float detail = effects != null ? effects.Detail(point) : 1f;

        if (detail <= 0f || !Pool.Ensure())
            return;

        int count = Mathf.RoundToInt(Mathf.Clamp(speedChange * 6f, 8f, 50f) * Mathf.Max(0.3f, detail));
        Vector3 carry = kartBody != null ? kartBody.linearVelocity : Vector3.zero;
        Pool.EmitSparks(point, normal, carry, count, Mathf.Clamp01(speedChange / 8f));
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

        float distance = Vector3.Distance(camera.transform.position, position);
        return 1f - Mathf.InverseLerp(fullDetailDistance, Mathf.Max(fullDetailDistance + 1f, maxDistance), distance);
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
        if (smokeCarry.Length == count)
            return;

        smokeCarry = new float[count];
        dirtCarry = new float[count];
        sprayCarry = new float[count];
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

        public static void EmitDirt(Vector3 point, Vector3 normal, Vector3 wheelVelocity, Color surfaceColor, bool dust)
        {
            float shade = Random.Range(0.85f, 1.25f);
            Color color = surfaceColor * shade * LightLevel();
            color.a = 1f;

            var chunk = new ParticleSystem.EmitParams
            {
                position = point + normal * 0.05f + Random.insideUnitSphere * 0.08f,
                velocity = wheelVelocity * 0.2f + normal * Random.Range(1.5f, 3.5f) + Random.insideUnitSphere * 1f,
                startSize = Random.Range(0.08f, 0.16f),
                startLifetime = Random.Range(0.5f, 0.9f),
                startColor = color
            };

            dirt.Emit(chunk, 1);

            if (!dust)
                return;

            // Polvo: una nube clara del mismo color, en las partículas del humo.
            Color dustColor = Color.Lerp(surfaceColor, new Color(0.8f, 0.75f, 0.65f), 0.5f) * LightLevel();
            dustColor.a = 0.4f;

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

        public static void EmitSpray(Vector3 point, Vector3 wheelVelocity, float wetness)
        {
            Color color = new Color(0.88f, 0.9f, 0.93f) * LightLevel();
            color.a = Mathf.Lerp(0.15f, 0.35f, wetness);

            var emit = new ParticleSystem.EmitParams
            {
                position = point + Vector3.up * 0.1f + Random.insideUnitSphere * 0.08f,
                velocity = wheelVelocity * 0.4f + Vector3.up * Random.Range(0.8f, 2f) + Random.insideUnitSphere * 0.7f,
                startSize = Random.Range(0.4f, 0.8f),
                startLifetime = Random.Range(0.5f, 0.85f),
                startColor = color,
                rotation = Random.Range(0f, 360f)
            };

            spray.Emit(emit, 1);
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
