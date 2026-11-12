using UnityEngine;

/// <summary>
/// Arma todo lo que una pista necesita para correr, a partir del modelo de la
/// pista y de la línea de llegada. Se usa desde el Inspector, con el botón
/// "Preparar pista" (ver TrackSetupEditor). Genera:
/// - La línea central (TrackCenterline), que también dibuja el minimapa.
/// - Los checkpoints, repartidos a lo largo de la línea, con el ancho del
///   asfalto en cada lugar. El último queda en la llegada (cuenta la vuelta).
/// - Un punto de reaparición por checkpoint, sobre el asfalto y mirando en el
///   sentido de carrera.
/// - La grilla de largada y el punto de reaparición de la largada.
/// - La zona de reaparición de emergencia, debajo de toda la pista.
/// - Las superficies (asfalto y pasto) con su TrackSurface y su MeshCollider.
/// Y conecta todo con los managers de la carrera, el minimapa y los bots.
///
/// Lo único que se hace a mano: poner el modelo de la pista, la línea de
/// llegada (LineaDeLlegada, con el eje Z en el sentido de carrera) y elegir
/// acá qué objetos son asfalto y cuáles pasto.
///
/// Los datos de este componente se usan solo en el editor.
/// </summary>
public class TrackSetup : MonoBehaviour
{
    [Header("Pista")]
    [Tooltip("Línea central. Se genera desde su Pista (el MeshCollider del asfalto) y empieza en su LineaDeLlegada.")]
    public TrackCenterline centerline;

    [Tooltip("Objetos del asfalto (la superficie válida). Normalmente uno solo: el mismo de la línea central.")]
    public GameObject[] roadObjects = new GameObject[0];

    [Tooltip("Superficie del asfalto (Road).")]
    public TrackSurfaceData roadSurface;

    [Tooltip("Pasto, tierra, piano y todo lo que está fuera de pista.")]
    public GameObject[] offTrackObjects = new GameObject[0];

    [Tooltip("Superficie de afuera de la pista (Grass).")]
    public TrackSurfaceData offTrackSurface;

    [Header("Checkpoints")]
    public RaceCheckpointManager checkpointManager;

    [Tooltip("Dónde se crean los checkpoints. Lo que haya adentro se reemplaza.")]
    public Transform checkpointParent;

    [Tooltip("Distancia aproximada entre checkpoints (m). También es lo máximo que retrocede un kart al reaparecer.")]
    [Min(10f)] public float checkpointSpacing = 50f;

    [Tooltip("Cuánto se extiende cada checkpoint afuera del asfalto, de cada lado (m), para que cuente aunque el kart se abra.")]
    [Min(0f)] public float checkpointSideMargin = 3f;

    [Tooltip("Alto de cada checkpoint (m).")]
    [Min(1f)] public float checkpointHeight = 5f;

    [Tooltip("A cuántos metros después de su checkpoint queda cada punto de reaparición.")]
    [Min(0f)] public float respawnDistanceAfterCheckpoint = 3f;

    [Header("Grilla de largada")]
    public NetworkKartSpawner kartSpawner;

    [Tooltip("Dónde se crean los lugares de la grilla. Lo que haya adentro se reemplaza.")]
    public Transform gridParent;

    [Tooltip("Cantidad de lugares (jugadores como máximo).")]
    [Min(1)] public int gridSlots = 6;

    [Tooltip("Metros entre la pole y la línea de llegada, sobre la línea central.")]
    [Min(0f)] public float gridDistanceBeforeFinish = 15f;

    [Tooltip("Metros entre un lugar y el siguiente.")]
    [Min(1f)] public float gridSlotSpacing = 3f;

    [Tooltip("Cuánto se corre cada lugar hacia el costado desde el centro de la pista (m).")]
    [Min(0f)] public float gridLateralOffset = 1.3f;

    [Tooltip("La pole a la izquierda (mirando en el sentido de carrera). Los demás lugares se alternan.")]
    public bool poleOnLeft = true;

    [Tooltip("Altura de cada lugar sobre el asfalto (m).")]
    [Min(0f)] public float gridHeight = 0.18f;

    [Header("Reaparición")]
    public RaceRespawnManager respawnManager;

    [Tooltip("Dónde reaparece un kart que todavía no pasó ningún checkpoint. Se pone en la pole.")]
    public Transform startRespawnPoint;

    [Tooltip("Zona debajo de la pista: el kart que cae ahí reaparece.")]
    public BoxCollider emergencyRespawnZone;

    [Tooltip("Cuántos metros por debajo del punto más bajo de la pista empieza la zona de emergencia.")]
    [Min(1f)] public float emergencyDepthBelowTrack = 10f;
}
