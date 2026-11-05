using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rebufo: detrás de un kart el aire viaja "arrastrado" por él, así que el que
/// va pegado atrás choca contra aire que ya se mueve en su misma dirección y
/// tiene menos resistencia. Con eso gana velocidad en la recta y puede pasar.
///
/// Detrás de cada kart hay un cono (la estela): angosto pegado a la cola y
/// más ancho a medida que se aleja, hasta <see cref="Settings.range"/> metros.
/// Cuanto más cerca y más centrado se va dentro del cono, más baja la
/// resistencia del aire (KartAerodynamics.DragMultiplier).
///
/// Los karts se anotan solos (KartBehaviour); también cuentan los de los
/// otros jugadores en red, porque solo se usa su posición y hacia dónde miran.
/// </summary>
public static class KartSlipstream
{
    [System.Serializable]
    public class Settings
    {
        [Tooltip("Largo de la estela detrás de cada kart (m).")]
        [Min(1f)]
        public float range = 12f;

        [Tooltip("Distancia mínima (m): más pegado ya es un choque, no una estela.")]
        [Min(0f)]
        public float minDistance = 0.6f;

        [Tooltip("Medio ancho de la estela pegado a la cola del kart (m).")]
        [Min(0.1f)]
        public float halfWidthAtTail = 0.7f;

        [Tooltip("Cuánto se ensancha la estela por cada metro hacia atrás (m/m).")]
        [Min(0f)]
        public float widening = 0.06f;

        [Tooltip("Cuánto baja la resistencia del aire en el mejor lugar de la estela (0,3 = 30 % menos).")]
        [Range(0f, 0.8f)]
        public float maxDragReduction = 0.3f;

        [Tooltip("Por debajo de esta velocidad (km/h) no hay estela que aprovechar.")]
        [Min(0f)]
        public float minSpeedKmh = 30f;

        [Tooltip("Desde esta velocidad (km/h) la estela actúa completa.")]
        [Min(0f)]
        public float fullSpeedKmh = 55f;

        [Tooltip("Tiempo (s) para entrar o salir del rebufo: el aire no cambia de golpe.")]
        [Min(0f)]
        public float response = 0.25f;
    }

    private static readonly List<Rigidbody> karts = new List<Rigidbody>();

    /// <summary>Anota un kart para que deje estela.</summary>
    public static void Register(Rigidbody kart)
    {
        if (kart != null && !karts.Contains(kart))
            karts.Add(kart);
    }

    public static void Unregister(Rigidbody kart)
    {
        karts.Remove(kart);
    }

    /// <summary>
    /// Cuánto rebufo recibe este kart ahora, de 0 (aire libre) a 1 (pegado
    /// y centrado detrás de otro). Si hay varios adelante, cuenta el mejor.
    /// </summary>
    public static float Evaluate(Rigidbody self, Settings settings)
    {
        if (self == null || settings == null)
            return 0f;

        Vector3 velocity = self.linearVelocity;
        velocity.y = 0f;

        float speedKmh = velocity.magnitude * 3.6f;
        float speedFactor = Mathf.InverseLerp(settings.minSpeedKmh, Mathf.Max(settings.minSpeedKmh + 0.1f, settings.fullSpeedKmh), speedKmh);

        if (speedFactor <= 0f)
            return 0f;

        Vector3 selfForward = Flat(self.rotation * Vector3.forward);
        Vector3 selfPosition = self.position;

        float best = 0f;

        for (int i = karts.Count - 1; i >= 0; i--)
        {
            Rigidbody other = karts[i];

            if (other == null)
            {
                karts.RemoveAt(i);
                continue;
            }

            if (other == self || !other.gameObject.activeInHierarchy)
                continue;

            Vector3 otherForward = Flat(other.rotation * Vector3.forward);

            // Tiene que ir más o menos para el mismo lado.
            if (Vector3.Dot(otherForward, selfForward) < 0.8f)
                continue;

            Vector3 offset = selfPosition - other.position;

            // Muy arriba o muy abajo (otro nivel de la pista, un salto): no cuenta.
            if (Mathf.Abs(offset.y) > 1.5f)
                continue;

            offset.y = 0f;

            float behind = -Vector3.Dot(offset, otherForward);

            if (behind < settings.minDistance || behind > settings.range)
                continue;

            float lateral = (offset + otherForward * behind).magnitude;
            float halfWidth = settings.halfWidthAtTail + settings.widening * behind;

            if (lateral >= halfWidth)
                continue;

            float distanceFactor = 1f - Mathf.InverseLerp(settings.minDistance, settings.range, behind);
            float centered = lateral / halfWidth;
            float strength = distanceFactor * (1f - centered * centered);

            if (strength > best)
                best = strength;
        }

        return best * speedFactor;
    }

    private static Vector3 Flat(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
    }

#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        karts.Clear();
    }
#endif
}
