using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Pista al revés: el mismo circuito en sentido contrario. Se aplica una vez
/// por escena, al cargar la pista y antes de que aparezcan los karts:
/// - La grilla pasa al otro lado de la línea de llegada (la misma distancia,
///   la pole del mismo lado para el que maneja) y mira para el otro lado.
/// - Los puntos de reaparición de cada checkpoint quedan del otro lado de su
///   checkpoint, mirando en el sentido nuevo; el de la largada, en la pole.
/// - La línea central se da vuelta (TrackCenterline.SetReversed): con ella,
///   los bots, las posiciones, el minimapa y las cámaras de TV.
/// - Los checkpoints se cuentan en el orden contrario
///   (RaceCheckpointManager.SetReversed); la llegada sigue siendo la llegada.
/// - El arco de largada y el semáforo (StartLights) giran 180° sobre el
///   centro del arco, así miran a los que llegan en el sentido nuevo.
/// En el anfitrión lo aplica RaceSetupSync al cargar la escena (con
/// RaceSession.Reversed); en los demás, cuando llega por red.
/// </summary>
public static class TrackDirection
{
    private static int appliedScene = -1;
    private static bool appliedReversed;

    /// <summary>True si la pista de esta escena está al revés.</summary>
    public static bool IsReversed => appliedScene == SceneManager.GetActiveScene().handle && appliedReversed;

    public static void Apply(bool reversed)
    {
        int scene = SceneManager.GetActiveScene().handle;

        // Ya está así en esta escena.
        if (appliedScene == scene && appliedReversed == reversed)
            return;

        // Al derecho no hay nada que hacer (la escena viene así). Volver de al
        // revés a al derecho en la misma escena no pasa: cada sesión carga la
        // escena de nuevo.
        if (appliedScene == scene && appliedReversed && !reversed)
            return;

        appliedScene = scene;
        appliedReversed = reversed;

        if (!reversed)
            return;

        TrackCenterline centerline = Object.FindFirstObjectByType<TrackCenterline>(FindObjectsInactive.Include);
        RaceCheckpointManager checkpoints = Object.FindFirstObjectByType<RaceCheckpointManager>(FindObjectsInactive.Include);

        if (centerline == null || !centerline.IsValid || checkpoints == null)
        {
            Debug.LogWarning("TrackDirection: falta la línea central o los checkpoints; la pista queda al derecho.");
            appliedReversed = false;
            return;
        }

        // Todo se calcula con la línea al derecho y después se da vuelta.
        Transform pole = MoveGrid(centerline);
        MoveRespawnPoints(centerline, checkpoints);
        MoveStartRespawn(pole);
        TurnStartArch(centerline);

        centerline.SetReversed(true);
        checkpoints.SetReversed(true);

        Debug.Log("PISTA: al revés.");
    }

    /// <summary>La grilla, del otro lado de la llegada. Devuelve la pole.</summary>
    private static Transform MoveGrid(TrackCenterline centerline)
    {
        NetworkKartSpawner spawner = Object.FindFirstObjectByType<NetworkKartSpawner>(FindObjectsInactive.Include);
        FieldInfo field = typeof(NetworkKartSpawner).GetField("gridSlots", BindingFlags.Instance | BindingFlags.NonPublic);
        Transform[] slots = spawner != null && field != null ? field.GetValue(spawner) as Transform[] : null;

        if (slots == null)
            return null;

        float length = centerline.Length;

        foreach (Transform slot in slots)
        {
            if (slot == null)
                continue;

            // Dónde está: metros desde la llegada y cuánto al costado.
            float distance = centerline.GetDistance(slot.position);
            float lateral = Lateral(centerline, distance, slot.position);
            float clearance = Clearance(slot.position);

            // Lo mismo del otro lado: si estaba 15 m antes de la llegada,
            // queda 15 m después (en el sentido de siempre). Al costado, del
            // mismo lado para el que maneja (la derecha nueva es la izquierda
            // vieja).
            float newDistance = Mathf.Repeat(length - distance, length);
            Vector3 center = centerline.GetPointAtDistance(newDistance);
            Vector3 direction = Flat(centerline.GetDirectionAtDistance(newDistance));
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;

            Vector3 position = center - right * lateral;
            slot.SetPositionAndRotation(OnGround(position, clearance), Quaternion.LookRotation(-direction, Vector3.up));
        }

        return slots.Length > 0 ? slots[0] : null;
    }

    /// <summary>Cada punto de reaparición, del otro lado de su checkpoint y mirando en el sentido nuevo.</summary>
    private static void MoveRespawnPoints(TrackCenterline centerline, RaceCheckpointManager checkpoints)
    {
        float length = centerline.Length;

        for (int i = 0; i < checkpoints.CheckpointCount; i++)
        {
            RaceCheckpoint checkpoint = checkpoints.GetCheckpoint(i);
            Transform point = checkpoint != null ? RespawnPointOf(checkpoint) : null;

            if (point == null)
                continue;

            float checkpointDistance = centerline.GetDistance(checkpoint.transform.position);
            float pointDistance = centerline.GetDistance(point.position);

            // Cuánto después del checkpoint estaba (normalmente unos 3 m).
            float after = Mathf.Repeat(pointDistance - checkpointDistance + length * 0.5f, length) - length * 0.5f;
            float lateral = Lateral(centerline, pointDistance, point.position);
            float clearance = Clearance(point.position);

            float newDistance = Mathf.Repeat(checkpointDistance - after, length);
            Vector3 center = centerline.GetPointAtDistance(newDistance);
            Vector3 direction = Flat(centerline.GetDirectionAtDistance(newDistance));
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;

            point.SetPositionAndRotation(OnGround(center + right * lateral, clearance), Quaternion.LookRotation(-direction, Vector3.up));
        }
    }

    private static Transform RespawnPointOf(RaceCheckpoint checkpoint)
    {
        if (checkpoint.RespawnPoint != null)
            return checkpoint.RespawnPoint;

        // Todavía no corrió su Awake: se busca igual que él.
        foreach (Transform child in checkpoint.GetComponentsInChildren<Transform>(true))
        {
            if (child != checkpoint.transform && child.name == "RespawnPoint")
                return child;
        }

        return null;
    }

    /// <summary>El de la largada (antes del primer checkpoint) va en la pole.</summary>
    private static void MoveStartRespawn(Transform pole)
    {
        RaceRespawnManager respawn = Object.FindFirstObjectByType<RaceRespawnManager>(FindObjectsInactive.Include);
        FieldInfo field = typeof(RaceRespawnManager).GetField("startRespawnPoint", BindingFlags.Instance | BindingFlags.NonPublic);
        Transform start = respawn != null && field != null ? field.GetValue(respawn) as Transform : null;

        if (start != null && pole != null)
            start.SetPositionAndRotation(pole.position, pole.rotation);
    }

    /// <summary>
    /// El arco de la meta (los objetos de la pista con "largada" en el nombre,
    /// cerca de la línea) y el semáforo, dados vuelta sobre el centro del arco.
    /// </summary>
    private static void TurnStartArch(TrackCenterline centerline)
    {
        Vector3 line = centerline.StartFinishLine != null
            ? centerline.StartFinishLine.position
            : centerline.GetPointAtDistance(0f);

        var arch = new System.Collections.Generic.List<Transform>();
        Bounds bounds = new Bounds(line, Vector3.zero);
        bool hasArch = false;

        foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!renderer.name.ToLowerInvariant().Contains("largada") || renderer.GetComponentInParent<StartLights>(true) != null)
                continue;

            Vector3 center = renderer.bounds.center;
            center.y = line.y;

            if (Vector3.Distance(center, line) > 15f)
                continue;

            arch.Add(renderer.transform);

            if (hasArch)
                bounds.Encapsulate(renderer.bounds);
            else
                bounds = renderer.bounds;

            hasArch = true;
        }

        // El centro del arco (si no hay arco, la línea de llegada).
        Vector3 pivot = hasArch ? bounds.center : line;

        foreach (Transform part in arch)
            part.RotateAround(pivot, Vector3.up, 180f);

        foreach (StartLights lights in Object.FindObjectsByType<StartLights>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            lights.transform.RotateAround(pivot, Vector3.up, 180f);
    }

    /// <summary>Cuánto al costado de la línea central está una posición (m, + = derecha).</summary>
    private static float Lateral(TrackCenterline centerline, float distance, Vector3 position)
    {
        Vector3 center = centerline.GetPointAtDistance(distance);
        Vector3 right = Vector3.Cross(Vector3.up, Flat(centerline.GetDirectionAtDistance(distance))).normalized;
        return Vector3.Dot(position - center, right);
    }

    /// <summary>Altura de una posición sobre el piso (m).</summary>
    private static float Clearance(Vector3 position)
    {
        return Ground(position, out float y) ? position.y - y : 0.2f;
    }

    private static Vector3 OnGround(Vector3 position, float clearance)
    {
        if (Ground(position, out float y))
            position.y = y + clearance;

        return position;
    }

    private static bool Ground(Vector3 position, out float y)
    {
        y = position.y;
        RaycastHit[] hits = Physics.RaycastAll(position + Vector3.up * 3f, Vector3.down, 10f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;

        foreach (RaycastHit hit in hits)
        {
            // Solo el piso (no karts).
            if (hit.collider.attachedRigidbody != null || hit.distance >= best)
                continue;

            best = hit.distance;
            y = hit.point.y;
        }

        return best < float.MaxValue;
    }

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector.sqrMagnitude > 1e-6f ? vector.normalized : Vector3.forward;
    }
}
