using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Inspector de TrackSetup: los botones "Preparar pista" y "Revisar".
/// </summary>
[CustomEditor(typeof(TrackSetup))]
public class TrackSetupEditor : Editor
{
    private string report;

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "Para una pista nueva: poné el modelo, asigná el asfalto a la línea central (Pista), " +
            "poné LineaDeLlegada en la meta con el eje Z (azul) en el sentido de carrera, " +
            "elegí abajo qué es asfalto y qué es pasto, y apretá Preparar pista. Se puede deshacer con Ctrl+Z.",
            MessageType.Info);

        DrawDefaultInspector();

        EditorGUILayout.Space();

        if (GUILayout.Button("Preparar pista", GUILayout.Height(32)))
        {
            report = TrackSetupBuilder.Build((TrackSetup)target);
        }

        if (GUILayout.Button("Revisar"))
        {
            report = TrackSetupBuilder.Check((TrackSetup)target);
        }

        if (!string.IsNullOrEmpty(report))
        {
            EditorGUILayout.HelpBox(report, MessageType.None);
        }
    }
}

/// <summary>
/// Lo que hacen los botones de TrackSetup. Está separado del Inspector para
/// poder llamarlo desde otras herramientas.
/// </summary>
public static class TrackSetupBuilder
{
    // Hasta dónde se busca el borde del asfalto, de cada lado del centro (m).
    private const float MaxHalfWidth = 40f;

    // Cada cuánto se prueba si sigue habiendo asfalto al medir el ancho (m).
    private const float WidthStep = 0.25f;

    // Lo que baja el centro del checkpoint respecto de la mitad de su alto, para
    // que empiece un poco debajo del asfalto y no quede un hueco en las lomas (m).
    private const float CheckpointSink = 0.2f;

    /// <summary>Genera todo y devuelve el informe de "Revisar".</summary>
    public static string Build(TrackSetup setup)
    {
        FillMissingReferences(setup);

        TrackCenterline centerline = setup.centerline;

        if (centerline == null)
            return "Falta la línea central (TrackCenterline).";

        MeshCollider road = centerline.TrackSurface;

        if (road == null)
            return "A la línea central le falta Pista: el MeshCollider del asfalto.";

        if (centerline.StartFinishLine == null)
            return "A la línea central le falta la línea de llegada (LineaDeLlegada).";

        if (setup.checkpointManager == null)
            return "No hay un RaceCheckpointManager en la escena.";

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Preparar pista");
        int undoGroup = Undo.GetCurrentGroup();

        Undo.RecordObject(setup, "Preparar pista");

        SetupSurfaces(setup);
        Physics.SyncTransforms();

        if (!centerline.Generate())
        {
            Undo.CollapseUndoOperations(undoGroup);
            return "No se pudo generar la línea central. El motivo está en la consola.";
        }

        BuildCheckpoints(setup, road);
        BuildGrid(setup);
        BuildEmergencyZone(setup);
        LinkCenterline(setup);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(setup.gameObject.scene);

        string result = "Pista preparada.\n\n" + Check(setup);
        Debug.Log("PREPARAR PISTA\n" + result, setup);

        return result;
    }

    /// <summary>Revisa que la pista esté lista para correr y lo cuenta en texto.</summary>
    public static string Check(TrackSetup setup)
    {
        StringBuilder text = new StringBuilder();
        List<string> warnings = new List<string>();

        TrackCenterline centerline = setup.centerline;

        if (centerline == null || !centerline.IsValid)
        {
            return "La línea central no está generada.";
        }

        float length = centerline.Length;

        text.AppendLine($"Línea central: {centerline.Points.Count} puntos, vuelta de {length:F0} m, ancho medio {centerline.AverageWidth:F1} m.");

        if (centerline.LastMissedPoints > 0)
            warnings.Add($"{centerline.LastMissedPoints} puntos de la línea central no cayeron sobre el asfalto.");

        // Checkpoints.
        RaceCheckpoint[] checkpoints = GetCheckpoints(setup.checkpointManager);

        if (checkpoints.Length == 0)
        {
            warnings.Add("El RaceCheckpointManager no tiene checkpoints.");
        }
        else
        {
            int offRoad = 0;
            float previous = -1f;
            bool ordered = true;
            float minWidth = float.MaxValue;
            float maxWidth = 0f;

            for (int i = 0; i < checkpoints.Length; i++)
            {
                RaceCheckpoint checkpoint = checkpoints[i];

                if (checkpoint == null)
                {
                    warnings.Add($"El checkpoint {i} está vacío en la lista.");
                    continue;
                }

                if (checkpoint.CheckpointIndex != i)
                    warnings.Add($"{checkpoint.name} tiene el índice {checkpoint.CheckpointIndex} en el lugar {i}.");

                BoxCollider box = checkpoint.GetComponent<BoxCollider>();

                if (box == null || !box.isTrigger)
                    warnings.Add($"{checkpoint.name} no tiene un BoxCollider trigger.");
                else
                {
                    float width = box.size.x * checkpoint.transform.lossyScale.x;
                    minWidth = Mathf.Min(minWidth, width);
                    maxWidth = Mathf.Max(maxWidth, width);
                }

                Transform respawn = FindChild(checkpoint.transform, "RespawnPoint");

                if (respawn == null)
                    warnings.Add($"{checkpoint.name} no tiene RespawnPoint.");
                else if (!IsOnValidSurface(respawn.position))
                    offRoad++;

                // Todos en orden sobre la línea; el último, en la llegada.
                float distance = centerline.GetDistance(checkpoint.transform.position);

                if (i == checkpoints.Length - 1)
                {
                    float finish = centerline.GetDistance(centerline.StartFinishLine.position);
                    float gap = Mathf.Abs(Mathf.DeltaAngle(distance / length * 360f, finish / length * 360f)) / 360f * length;

                    if (gap > 3f)
                        warnings.Add($"El último checkpoint (el que cuenta la vuelta) está a {gap:F0} m de la línea de llegada.");
                }
                else
                {
                    if (distance <= previous)
                        ordered = false;

                    previous = distance;
                }
            }

            text.AppendLine($"Checkpoints: {checkpoints.Length}, uno cada {length / checkpoints.Length:F0} m, de {minWidth:F1} a {maxWidth:F1} m de ancho. El último está en la llegada.");

            if (!ordered)
                warnings.Add("Los checkpoints no siguen el sentido de carrera.");

            if (offRoad > 0)
                warnings.Add($"{offRoad} puntos de reaparición no están sobre asfalto.");
        }

        // Grilla.
        Transform[] slots = GetGridSlots(setup.kartSpawner);

        if (slots.Length == 0)
        {
            warnings.Add("La grilla (NetworkKartSpawner) no tiene lugares.");
        }
        else
        {
            int offRoad = 0;

            foreach (Transform slot in slots)
            {
                if (slot == null || !IsOnValidSurface(slot.position))
                    offRoad++;
            }

            float pole = centerline.GetDistance(slots[0].position);
            float finish = centerline.GetDistance(centerline.StartFinishLine.position);
            float behind = Mathf.Repeat(finish - pole, length);

            text.AppendLine($"Grilla: {slots.Length} lugares; la pole está {behind:F0} m antes de la llegada.");

            if (offRoad > 0)
                warnings.Add($"{offRoad} lugares de la grilla no están sobre asfalto.");
        }

        // Referencias.
        if (setup.respawnManager == null)
            warnings.Add("No hay RaceRespawnManager.");
        else if (GetReference(setup.respawnManager, "startRespawnPoint") == null)
            warnings.Add("Falta el punto de reaparición de la largada.");

        if (setup.emergencyRespawnZone == null)
            warnings.Add("No hay zona de reaparición de emergencia.");

        RacePositionManager positions = FindInScene<RacePositionManager>(setup);

        if (positions != null && GetReference(positions, "centerline") != centerline)
            warnings.Add("RacePositionManager no usa esta línea central.");

        List<TrackMinimap> minimaps = FindAllInScene<TrackMinimap>(setup);

        foreach (TrackMinimap minimap in minimaps)
        {
            if (GetReference(minimap, "centerline") != centerline)
                warnings.Add($"El minimapa {minimap.name} no usa esta línea central.");
        }

        text.AppendLine($"Minimapa: {(minimaps.Count > 0 ? "conectado" : "no hay en la escena")}.");

        List<CustomIABots> bots = FindAllInScene<CustomIABots>(setup);

        if (bots.Count > 0)
        {
            text.AppendLine($"Bots: {bots.Count}, conectados a la línea central. Lo que aprendieron es de la pista donde entrenaron: en una pista nueva hay que volver a entrenarlos.");
        }

        if (warnings.Count == 0)
        {
            text.AppendLine();
            text.Append("Todo en orden.");
        }
        else
        {
            text.AppendLine();
            text.AppendLine("Para revisar:");

            foreach (string warning in warnings)
                text.AppendLine("- " + warning);
        }

        return text.ToString().TrimEnd();
    }

    private static void FillMissingReferences(TrackSetup setup)
    {
        Undo.RecordObject(setup, "Preparar pista");

        if (setup.centerline == null)
            setup.centerline = FindInScene<TrackCenterline>(setup);

        if (setup.checkpointManager == null)
            setup.checkpointManager = FindInScene<RaceCheckpointManager>(setup);

        if (setup.respawnManager == null)
            setup.respawnManager = FindInScene<RaceRespawnManager>(setup);

        if (setup.kartSpawner == null)
            setup.kartSpawner = FindInScene<NetworkKartSpawner>(setup);

        if (setup.startRespawnPoint == null && setup.respawnManager != null)
            setup.startRespawnPoint = GetReference(setup.respawnManager, "startRespawnPoint") as Transform;

        if (setup.emergencyRespawnZone == null)
        {
            RaceRespawnZone zone = FindInScene<RaceRespawnZone>(setup);

            if (zone != null)
                setup.emergencyRespawnZone = zone.GetComponent<BoxCollider>();
        }
    }

    // ---------------------------------------------------------------- Superficies

    private static void SetupSurfaces(TrackSetup setup)
    {
        SetupSurface(setup.roadObjects, setup.roadSurface);
        SetupSurface(setup.offTrackObjects, setup.offTrackSurface);
    }

    private static void SetupSurface(GameObject[] objects, TrackSurfaceData data)
    {
        if (objects == null)
            return;

        foreach (GameObject surfaceObject in objects)
        {
            if (surfaceObject == null)
                continue;

            if (surfaceObject.GetComponent<Collider>() == null)
                Undo.AddComponent<MeshCollider>(surfaceObject);

            TrackSurface surface = surfaceObject.GetComponent<TrackSurface>();

            if (surface == null)
                surface = Undo.AddComponent<TrackSurface>(surfaceObject);

            if (data != null)
                SetReference(surface, "surfaceData", data);
        }
    }

    // ---------------------------------------------------------------- Checkpoints

    private static void BuildCheckpoints(TrackSetup setup, MeshCollider road)
    {
        TrackCenterline centerline = setup.centerline;
        Transform parent = setup.checkpointParent;

        if (parent == null)
        {
            parent = CreateChild(setup.checkpointManager.transform.parent, "CheckPoints");
            setup.checkpointParent = parent;
        }

        int layer = parent.gameObject.layer;
        ClearChildren(parent);

        float length = centerline.Length;
        int count = Mathf.Max(3, Mathf.RoundToInt(length / setup.checkpointSpacing));
        float step = length / count;

        Transform finishLine = centerline.StartFinishLine;
        float finishDistance = centerline.GetDistance(finishLine.position);

        RaceCheckpoint[] checkpoints = new RaceCheckpoint[count];

        for (int i = 0; i < count; i++)
        {
            // El 0 está un tramo después de la llegada y el último, en la llegada.
            bool isFinish = i == count - 1;
            float distance = Mathf.Repeat(finishDistance + (i + 1) * step, length);

            if (isFinish)
                distance = finishDistance;

            Vector3 point = centerline.GetPointAtDistance(distance);
            Vector3 forward = Flat(isFinish ? finishLine.forward : centerline.GetDirectionAtDistance(distance));
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            float leftWidth = MeasureSide(road, point, -right);
            float rightWidth = MeasureSide(road, point, right);
            Vector3 middle = point + right * ((rightWidth - leftWidth) * 0.5f);

            GameObject checkpointObject = new GameObject($"Checkpoint_{i:00}");
            checkpointObject.layer = layer;
            checkpointObject.transform.SetParent(parent, false);
            checkpointObject.transform.SetPositionAndRotation(
                middle + Vector3.up * (setup.checkpointHeight * 0.5f - CheckpointSink),
                Quaternion.LookRotation(forward, Vector3.up));
            Undo.RegisterCreatedObjectUndo(checkpointObject, "Preparar pista");

            BoxCollider box = checkpointObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(leftWidth + rightWidth + 2f * setup.checkpointSideMargin, setup.checkpointHeight, 1f);

            Transform respawn = CreatePointOnRoad(
                checkpointObject.transform, "RespawnPoint", centerline, road,
                distance + setup.respawnDistanceAfterCheckpoint, 0f);
            respawn.gameObject.layer = layer;

            RaceCheckpoint checkpoint = checkpointObject.AddComponent<RaceCheckpoint>();
            SerializedObject serialized = new SerializedObject(checkpoint);
            serialized.FindProperty("checkpointIndex").intValue = i;
            serialized.FindProperty("respawnPoint").objectReferenceValue = respawn;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            checkpoints[i] = checkpoint;
        }

        SetArray(setup.checkpointManager, "checkpoints", checkpoints);
    }

    /// <summary>Cuánto asfalto hay desde el punto hacia ese lado (m).</summary>
    private static float MeasureSide(MeshCollider road, Vector3 point, Vector3 side)
    {
        float last = 0f;
        int misses = 0;

        for (float offset = WidthStep; offset <= MaxHalfWidth; offset += WidthStep)
        {
            Ray ray = new Ray(point + side * offset + Vector3.up * 5f, Vector3.down);

            if (road.Raycast(ray, out _, 25f))
            {
                last = offset;
                misses = 0;
            }
            else if (++misses >= 4)
            {
                break;
            }
        }

        return last;
    }

    // ---------------------------------------------------------------- Grilla

    private static void BuildGrid(TrackSetup setup)
    {
        TrackCenterline centerline = setup.centerline;
        Transform parent = setup.gridParent;

        if (parent == null)
        {
            GameObject gridObject = new GameObject("GrillaLargada");
            SceneManager.MoveGameObjectToScene(gridObject, setup.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(gridObject, "Preparar pista");
            parent = gridObject.transform;
            setup.gridParent = parent;
        }

        ClearChildren(parent);

        float length = centerline.Length;
        float finishDistance = centerline.GetDistance(centerline.StartFinishLine.position);
        float poleSide = setup.poleOnLeft ? -1f : 1f;

        Transform[] slots = new Transform[setup.gridSlots];

        for (int i = 0; i < setup.gridSlots; i++)
        {
            float distance = Mathf.Repeat(
                finishDistance - setup.gridDistanceBeforeFinish - i * setup.gridSlotSpacing, length);

            Vector3 forward = Flat(centerline.GetDirectionAtDistance(distance));
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            float side = i % 2 == 0 ? poleSide : -poleSide;
            Vector3 point = centerline.GetPointAtDistance(distance) + right * (side * setup.gridLateralOffset);

            if (SurfaceBelow(point, out RaycastHit hit))
                point = hit.point;

            GameObject slot = new GameObject($"Lugar{i + 1}");
            slot.transform.SetParent(parent, false);
            slot.transform.SetPositionAndRotation(
                point + Vector3.up * setup.gridHeight,
                Quaternion.LookRotation(forward, Vector3.up));
            Undo.RegisterCreatedObjectUndo(slot, "Preparar pista");

            slots[i] = slot.transform;
        }

        if (setup.kartSpawner != null)
            SetArray(setup.kartSpawner, "gridSlots", slots);

        // Reaparición antes de pasar el primer checkpoint: en la pole.
        if (setup.respawnManager != null)
        {
            if (setup.startRespawnPoint == null)
            {
                setup.startRespawnPoint = CreateChild(setup.respawnManager.transform.parent, "StartRespawnPoint");
            }

            float poleDistance = Mathf.Repeat(finishDistance - setup.gridDistanceBeforeFinish, length);
            PlaceOnRoad(setup.startRespawnPoint, slots[0].position, centerline.GetDirectionAtDistance(poleDistance));

            SetReference(setup.respawnManager, "startRespawnPoint", setup.startRespawnPoint);

            if (GetReference(setup.respawnManager, "checkpointManager") == null)
                SetReference(setup.respawnManager, "checkpointManager", setup.checkpointManager);
        }
    }

    // ---------------------------------------------------------------- Emergencia

    private static void BuildEmergencyZone(TrackSetup setup)
    {
        if (setup.respawnManager == null)
            return;

        if (setup.emergencyRespawnZone == null)
        {
            Transform zoneTransform = CreateChild(setup.respawnManager.transform.parent, "EmergencyRespawnZone");
            BoxCollider newBox = Undo.AddComponent<BoxCollider>(zoneTransform.gameObject);
            RaceRespawnZone newZone = Undo.AddComponent<RaceRespawnZone>(zoneTransform.gameObject);
            SetReference(newZone, "respawnManager", setup.respawnManager);
            setup.emergencyRespawnZone = newBox;
        }

        Bounds bounds = GetTrackBounds(setup);
        BoxCollider box = setup.emergencyRespawnZone;
        Transform zone = box.transform;

        const float height = 50f;
        const float extra = 100f;

        Undo.RecordObject(zone, "Preparar pista");
        Undo.RecordObject(box, "Preparar pista");

        zone.SetPositionAndRotation(
            new Vector3(bounds.center.x, bounds.min.y - setup.emergencyDepthBelowTrack - height * 0.5f, bounds.center.z),
            Quaternion.identity);

        Vector3 parentScale = zone.parent != null ? zone.parent.lossyScale : Vector3.one;
        zone.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);

        box.isTrigger = true;
        box.center = Vector3.zero;
        box.size = new Vector3(bounds.size.x + extra, height, bounds.size.z + extra);
    }

    private static Bounds GetTrackBounds(TrackSetup setup)
    {
        IReadOnlyList<Vector3> points = setup.centerline.Points;
        Bounds bounds = new Bounds(points[0], Vector3.zero);

        foreach (Vector3 point in points)
            bounds.Encapsulate(point);

        EncapsulateRenderers(ref bounds, setup.roadObjects);
        EncapsulateRenderers(ref bounds, setup.offTrackObjects);
        EncapsulateRenderers(ref bounds, new[] { setup.centerline.TrackSurface.gameObject });

        return bounds;
    }

    private static void EncapsulateRenderers(ref Bounds bounds, GameObject[] objects)
    {
        if (objects == null)
            return;

        foreach (GameObject item in objects)
        {
            if (item == null)
                continue;

            foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(renderer.bounds);
        }
    }

    // ---------------------------------------------------------------- Conexiones

    private static void LinkCenterline(TrackSetup setup)
    {
        TrackCenterline centerline = setup.centerline;

        RacePositionManager positions = FindInScene<RacePositionManager>(setup);

        if (positions != null)
            SetReference(positions, "centerline", centerline);

        foreach (TrackMinimap minimap in FindAllInScene<TrackMinimap>(setup))
        {
            SetReference(minimap, "centerline", centerline);
            minimap.SetVerticesDirty();
        }

        foreach (CustomIABots bot in FindAllInScene<CustomIABots>(setup))
            SetReference(bot, "centerline", centerline);
    }

    // ---------------------------------------------------------------- Ayudas

    private static Transform CreatePointOnRoad(
        Transform parent, string name, TrackCenterline centerline, MeshCollider road, float distance, float height)
    {
        GameObject pointObject = new GameObject(name);
        pointObject.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(pointObject, "Preparar pista");

        Vector3 point = centerline.GetPointAtDistance(distance);
        Vector3 direction = centerline.GetDirectionAtDistance(distance);
        Vector3 normal = Vector3.up;

        if (road.Raycast(new Ray(point + Vector3.up * 5f, Vector3.down), out RaycastHit hit, 25f))
        {
            point = hit.point;
            normal = hit.normal;
        }

        pointObject.transform.SetPositionAndRotation(
            point + normal * height,
            Quaternion.LookRotation(Vector3.ProjectOnPlane(direction, normal).normalized, normal));

        return pointObject.transform;
    }

    /// <summary>Apoya el punto sobre el piso, mirando hacia adelante y con el "arriba" según la pendiente.</summary>
    private static void PlaceOnRoad(Transform target, Vector3 point, Vector3 direction)
    {
        Vector3 normal = Vector3.up;

        if (SurfaceBelow(point, out RaycastHit hit))
        {
            point = hit.point;
            normal = hit.normal;
        }

        Undo.RecordObject(target, "Preparar pista");
        target.SetPositionAndRotation(
            point,
            Quaternion.LookRotation(Vector3.ProjectOnPlane(direction, normal).normalized, normal));
    }

    private static bool SurfaceBelow(Vector3 point, out RaycastHit hit)
    {
        return Physics.Raycast(point + Vector3.up * 5f, Vector3.down, out hit, 25f, ~0, QueryTriggerInteraction.Ignore);
    }

    private static bool IsOnValidSurface(Vector3 point)
    {
        if (!Physics.Raycast(point + Vector3.up, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore))
            return false;

        TrackSurface surface = hit.collider.GetComponentInParent<TrackSurface>();

        return surface != null && surface.SurfaceData != null && surface.SurfaceData.IsValidForTrack;
    }

    private static Vector3 Flat(Vector3 direction)
    {
        direction.y = 0f;

        return direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
    }

    private static Transform CreateChild(Transform parent, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(child, "Preparar pista");

        return child.transform;
    }

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(parent.GetChild(i).gameObject);
    }

    private static Transform FindChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
                return child;
        }

        return null;
    }

    private static RaceCheckpoint[] GetCheckpoints(RaceCheckpointManager manager)
    {
        if (manager == null)
            return new RaceCheckpoint[0];

        SerializedProperty list = new SerializedObject(manager).FindProperty("checkpoints");
        RaceCheckpoint[] result = new RaceCheckpoint[list.arraySize];

        for (int i = 0; i < result.Length; i++)
            result[i] = list.GetArrayElementAtIndex(i).objectReferenceValue as RaceCheckpoint;

        return result;
    }

    private static Transform[] GetGridSlots(NetworkKartSpawner spawner)
    {
        if (spawner == null)
            return new Transform[0];

        SerializedProperty list = new SerializedObject(spawner).FindProperty("gridSlots");
        List<Transform> result = new List<Transform>();

        for (int i = 0; i < list.arraySize; i++)
        {
            Transform slot = list.GetArrayElementAtIndex(i).objectReferenceValue as Transform;

            if (slot != null)
                result.Add(slot);
        }

        return result.ToArray();
    }

    private static Object GetReference(Object target, string field)
    {
        SerializedProperty property = new SerializedObject(target).FindProperty(field);

        return property != null ? property.objectReferenceValue : null;
    }

    private static void SetReference(Object target, string field, Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);

        if (property == null)
        {
            Debug.LogWarning($"Preparar pista: {target.GetType().Name} no tiene el campo {field}.", target);
            return;
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedProperties();
    }

    private static void SetArray(Object target, string field, Object[] values)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);

        if (property == null)
        {
            Debug.LogWarning($"Preparar pista: {target.GetType().Name} no tiene el campo {field}.", target);
            return;
        }

        property.arraySize = values.Length;

        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];

        serialized.ApplyModifiedProperties();
    }

    private static T FindInScene<T>(TrackSetup setup) where T : Component
    {
        foreach (GameObject root in setup.gameObject.scene.GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);

            if (found != null)
                return found;
        }

        return null;
    }

    private static List<T> FindAllInScene<T>(TrackSetup setup) where T : Component
    {
        List<T> result = new List<T>();

        foreach (GameObject root in setup.gameObject.scene.GetRootGameObjects())
            result.AddRange(root.GetComponentsInChildren<T>(true));

        return result;
    }
}
