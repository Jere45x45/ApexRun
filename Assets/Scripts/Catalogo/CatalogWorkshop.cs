using System;
using UnityEngine;

/// <summary>
/// Taller del Catálogo: cada pestaña (motor, ruedas, aero, volante) tiene su
/// estación en el taller. Al cambiar de pestaña, el kart se desliza hasta esa
/// estación y la cámara va con él hasta su punto, para que se vea bien lo que
/// se está cambiando.
/// - Cada estación tiene un punto para el kart (en el piso, con la trompa
///   hacia su eje Z) y uno para la cámara. Si el punto de la cámara tiene un
///   componente Camera, se usa su campo de visión; esas cámaras solo marcan
///   el lugar y quedan apagadas.
/// - El kart se apoya en el piso de la estación: su parte más baja (las
///   ruedas) queda a la altura del punto. Se vuelve a medir al cambiar de
///   pieza, porque cada juego de ruedas tiene otro tamaño.
/// - El encuadre se corre hacia la derecha (lensShift) para que el kart no
///   quede debajo de la lista de piezas, que ocupa la mitad izquierda.
/// - Entre estaciones el kart hace una curva por el centro libre del taller
///   (transitPoint), y la cámara nunca atraviesa el taller: si algo la tapa,
///   se acerca al kart (workshopModel).
/// - Con la R apretada el kart gira sobre su centro; al cambiar de pestaña
///   vuelve derecho.
/// </summary>
public class CatalogWorkshop : MonoBehaviour
{
    [Serializable]
    public class Station
    {
        public string name;

        [Tooltip("Pestaña del catálogo que lleva a esta estación.")]
        public PartType part;

        [Tooltip("Dónde va el kart: en el piso, con la trompa hacia el eje Z (flecha azul).")]
        public Transform kartPoint;

        [Tooltip("Dónde va la cámara. Si tiene un componente Camera, se usa su campo de visión.")]
        public Transform cameraPoint;
    }

    [Header("Referencias")]
    [SerializeField] private CatalogUIController catalogUI;

    [SerializeField] private CatalogController catalogController;

    [Tooltip("Kart del Catálogo (el que muestra las piezas).")]
    [SerializeField] private Transform kart;

    [Tooltip("Cámara que se mueve entre las estaciones.")]
    [SerializeField] private Camera viewCamera;

    [Header("Estaciones")]
    [SerializeField] private Station[] stations = new Station[0];

    [Header("Movimiento")]
    [Tooltip("Lo que tarda en ir de una estación a otra (s).")]
    [SerializeField, Min(0f)] private float transitionTime = 1.1f;

    [Tooltip("Apoyar el kart en el piso de cada estación.")]
    [SerializeField] private bool placeOnFloor = true;

    [Tooltip("Lugar libre del taller por donde pasa el kart entre estaciones (hace una curva hacia ahí). " +
             "Así no atraviesa las mesadas ni lo que hay contra las paredes. Vacío = en línea recta.")]
    [SerializeField] private Transform transitPoint;

    [Tooltip("Una toma a menos de esta distancia del kart (m) es una toma de cerca (por ejemplo, la del volante).")]
    [SerializeField, Min(0f)] private float closeShotDistance = 1.2f;

    [Tooltip("Cuánto sube la cámara a mitad de camino al ir a una toma de cerca o salir de ella (m).")]
    [SerializeField, Min(0f)] private float closeShotArc = 0.9f;

    [Header("Taller")]
    [Tooltip("Modelo del taller. Al arrancar se le arma un collider (si no tiene) para que la cámara " +
             "no atraviese el techo, las paredes ni las mesadas, sobre todo al ir de una estación a otra.")]
    [SerializeField] private Transform workshopModel;

    [Tooltip("Distancia mínima entre la cámara y el taller (m).")]
    [SerializeField, Min(0f)] private float cameraClearance = 0.15f;

    [Header("Encuadre")]
    [Tooltip("Corrimiento del encuadre (fracción de la pantalla). x = -0,25 deja el kart en la mitad derecha, " +
             "libre de la lista de piezas.")]
    [SerializeField] private Vector2 lensShift = new Vector2(-0.25f, 0f);

    [Header("Girar el kart (tecla R)")]
    [Tooltip("Mientras se mantiene apretada la R, el kart gira sobre su centro para verlo de todos lados. " +
             "Al cambiar de pestaña vuelve a su posición. En la toma del volante (de cerca) no gira.")]
    [SerializeField] private bool allowSpin = true;

    [Tooltip("Velocidad del giro (grados por segundo).")]
    [SerializeField, Min(0f)] private float spinSpeed = 90f;

    // Cuánto giró el kart con la R (grados) y alrededor de qué punto (su centro, en coordenadas del kart).
    private float spinAngle;
    private Vector3 spinPivotLocal;

    private Station current;
    private PartType currentPart;
    private bool hasPart;

    // Altura del origen del kart sobre su parte más baja (m).
    private float floorOffset = 0.2f;

    private bool moving;
    private float progress;
    private Vector3 kartFromPosition;
    private Quaternion kartFromRotation;
    private Vector3 cameraFromOffset;
    private Vector3 lookFromLocal;
    private float fovFrom;

    private float defaultFieldOfView = 60f;

    // Superficies del taller que la cámara no atraviesa.
    private readonly System.Collections.Generic.List<Collider> obstacles =
        new System.Collections.Generic.List<Collider>();

    // Punto del kart desde el que se mira si la cámara quedó tapada (coordenadas del kart).
    private static readonly Vector3 CameraPivot = new Vector3(0f, 0.35f, 0f);

    /// <summary>True mientras el kart va de una estación a otra.</summary>
    public bool IsMoving => moving;

    /// <summary>
    /// Otro script maneja la cámara por un rato (el Easter Egg de la radio):
    /// mientras es true, el taller sigue ubicando el kart pero no toca la cámara.
    /// </summary>
    public bool CameraHeld { get; set; }

    /// <summary>
    /// True mientras no se puede cambiar de estación: el kart está viajando o
    /// la cámara la está usando otro script.
    /// </summary>
    public bool IsBusy => moving || CameraHeld;

    private void Awake()
    {
        BuildObstacles();

        if (viewCamera != null)
        {
            defaultFieldOfView = viewCamera.fieldOfView;

            // Si la cámara estaba adentro del kart, se movería con él.
            if (kart != null && viewCamera.transform.IsChildOf(kart))
                viewCamera.transform.SetParent(null, true);

            if (lensShift != Vector2.zero)
            {
                viewCamera.usePhysicalProperties = true;
                viewCamera.gateFit = Camera.GateFitMode.Vertical;
                viewCamera.fieldOfView = defaultFieldOfView;
                viewCamera.lensShift = lensShift;
            }
        }

        // Las cámaras de las estaciones solo marcan el lugar.
        foreach (Station station in stations)
        {
            if (station == null || station.cameraPoint == null)
                continue;

            Camera marker = station.cameraPoint.GetComponent<Camera>();

            if (marker != null && marker != viewCamera)
                marker.enabled = false;

            AudioListener listener = station.cameraPoint.GetComponent<AudioListener>();

            if (listener != null)
                listener.enabled = false;
        }

        // El kart del Catálogo no anda: lo mueve este script.
        Rigidbody body = kart != null ? kart.GetComponent<Rigidbody>() : null;

        if (body != null)
        {
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
        }
    }

    private void OnEnable()
    {
        if (catalogController != null)
            catalogController.LoadoutChanged += HandleLoadoutChanged;
    }

    private void OnDisable()
    {
        if (catalogController != null)
            catalogController.LoadoutChanged -= HandleLoadoutChanged;
    }

    private void Start()
    {
        MeasureFloorOffset();
        GoTo(catalogUI != null ? catalogUI.CurrentCategory : PartType.Engine, true);
    }

    private void LateUpdate()
    {
        if (catalogUI != null && (!hasPart || catalogUI.CurrentCategory != currentPart))
            GoTo(catalogUI.CurrentCategory, false);

        if (current == null)
            return;

        if (moving)
        {
            progress += transitionTime > 0f ? Time.deltaTime / transitionTime : 1f;

            if (progress >= 1f)
            {
                progress = 1f;
                moving = false;
            }
        }

        float t = moving ? Ease(progress) : 1f;

        if (kart == null || current.kartPoint == null)
            return;

        Vector3 kartTargetPosition = GetKartPosition(current);
        Quaternion kartTargetRotation = current.kartPoint.rotation;

        // Tecla R: el kart gira sobre su centro (no sobre su origen, que está
        // atrás), así no se va del encuadre. Solo quieto en una estación.
        if (!moving && !CameraHeld && allowSpin && !IsCloseShot(current) && SpinPressed())
            spinAngle = Mathf.Repeat(spinAngle + spinSpeed * Time.deltaTime, 360f);

        if (spinAngle != 0f)
        {
            Quaternion spun = Quaternion.AngleAxis(spinAngle, current.kartPoint.up) * kartTargetRotation;
            kartTargetPosition += kartTargetRotation * spinPivotLocal - spun * spinPivotLocal;
            kartTargetRotation = spun;
        }

        Vector3 kartPosition = GetPathPosition(kartFromPosition, kartTargetPosition, t);
        Quaternion kartRotation = Quaternion.Slerp(kartFromRotation, kartTargetRotation, t);

        kart.SetPositionAndRotation(kartPosition, kartRotation);

        // La cámara la está usando otro script (el Easter Egg de la radio).
        if (CameraHeld)
            return;

        if (viewCamera == null || current.cameraPoint == null)
            return;

        if (!moving)
        {
            Vector3 restPosition = KeepClear(kartPosition + kartRotation * CameraPivot, current.cameraPoint.position);
            viewCamera.transform.SetPositionAndRotation(restPosition, current.cameraPoint.rotation);
            viewCamera.fieldOfView = GetFieldOfView(current);
            return;
        }

        // La cámara viaja con el kart: va de la toma de una estación a la de
        // la otra girando alrededor de él, y siempre mirándolo. Si fuera en
        // línea recta por el taller, a mitad de camino el kart le pasaría por
        // debajo. La vuelta alrededor del kart es en el sentido del taller y
        // no en el del kart: las tomas miran desde adentro del taller, y así
        // la cámara no pasa del lado de la pared cuando el kart da media vuelta.
        Vector3 cameraToOffset = current.cameraPoint.position - kartTargetPosition;
        Vector3 offset = Vector3.Slerp(cameraFromOffset, cameraToOffset, t);

        // Si una de las tomas está pegada al kart (la del volante, desde el
        // asiento), la cámara hace un arco por arriba: si no, a mitad de
        // camino pasaría rozando la carrocería.
        // Sube rápido, se queda arriba mientras se acerca o se aleja, y baja
        // (o arranca a subir) recién junto a la toma de cerca, como una grúa.
        bool toClose = cameraToOffset.magnitude < closeShotDistance;
        bool fromClose = cameraFromOffset.magnitude < closeShotDistance;

        if (toClose || fromClose)
        {
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, toClose && !fromClose ? 0.3f : 0.15f, t));
            float fall = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fromClose && !toClose ? 0.7f : 0.8f, 1f, t));
            offset += Vector3.up * closeShotArc * rise * fall;
        }

        Vector3 cameraPosition = kartPosition + offset;

        Vector3 lookToLocal = Quaternion.Inverse(kartTargetRotation) *
            (GetLookPoint(current.cameraPoint, kartTargetPosition + kartTargetRotation * CameraPivot) - kartTargetPosition);
        Vector3 lookPoint = kartPosition + kartRotation * Vector3.Lerp(lookFromLocal, lookToLocal, t);

        cameraPosition = KeepClear(kartPosition + kartRotation * CameraPivot, cameraPosition);

        Vector3 forward = lookPoint - cameraPosition;
        Quaternion rotation = forward.sqrMagnitude > 1e-6f
            ? Quaternion.LookRotation(forward, Vector3.up)
            : current.cameraPoint.rotation;

        viewCamera.transform.SetPositionAndRotation(cameraPosition, rotation);
        viewCamera.fieldOfView = Mathf.Lerp(fovFrom, GetFieldOfView(current), t);
    }

    /// <summary>
    /// Punto al que mira una cámara: sobre su eje, a la altura del kart (el
    /// punto del eje más cercano al kart).
    /// </summary>
    private static Vector3 GetLookPoint(Transform camera, Vector3 kartPivot)
    {
        float distance = Mathf.Max(0.3f, Vector3.Dot(kartPivot - camera.position, camera.forward));
        return camera.position + camera.forward * distance;
    }

    /// <summary>
    /// Si entre el kart y la cámara hay una parte del taller (el techo bajo,
    /// una pared, una mesada), acerca la cámara al kart para que quede de este
    /// lado, como la cámara de persecución de un juego de autos.
    /// </summary>
    private Vector3 KeepClear(Vector3 pivot, Vector3 desired)
    {
        Vector3 toCamera = desired - pivot;
        float distance = toCamera.magnitude;

        if (obstacles.Count == 0 || distance < 1e-3f)
            return desired;

        Vector3 direction = toCamera / distance;
        Ray ray = new Ray(pivot, direction);
        float allowed = distance;

        foreach (Collider obstacle in obstacles)
        {
            if (obstacle != null && obstacle.Raycast(ray, out RaycastHit hit, distance + cameraClearance))
                allowed = Mathf.Min(allowed, hit.distance - cameraClearance);
        }

        return pivot + direction * Mathf.Max(allowed, 0.1f);
    }

    /// <summary>Colliders del taller: los que ya tenga y uno de malla en cada pieza que no tenga.</summary>
    private void BuildObstacles()
    {
        obstacles.Clear();

        if (workshopModel == null)
            return;

        foreach (MeshFilter filter in workshopModel.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null)
                continue;

            Collider existing = filter.GetComponent<Collider>();

            if (existing == null)
            {
                MeshCollider meshCollider = filter.gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = filter.sharedMesh;
                existing = meshCollider;
            }

            obstacles.Add(existing);
        }
    }

    /// <summary>Va a la estación de una pestaña. Sin estación para esa pestaña, se queda donde está.</summary>
    private void GoTo(PartType part, bool instant)
    {
        currentPart = part;
        hasPart = true;

        Station station = FindStation(part);

        if (station == null || station == current)
            return;

        current = station;

        // El giro de la R se deshace en el viaje: llega derecho a la estación nueva.
        spinAngle = 0f;

        if (kart != null)
        {
            kartFromPosition = kart.position;
            kartFromRotation = kart.rotation;
        }

        if (viewCamera != null && kart != null)
        {
            // Dónde está la cámara respecto del kart al salir, y a qué parte del kart mira.
            cameraFromOffset = viewCamera.transform.position - kart.position;
            lookFromLocal = Quaternion.Inverse(kart.rotation) *
                (GetLookPoint(viewCamera.transform, kart.position + kart.rotation * CameraPivot) - kart.position);
            fovFrom = viewCamera.fieldOfView;
        }

        progress = 0f;
        moving = !instant && transitionTime > 0f;
    }

    private Station FindStation(PartType part)
    {
        foreach (Station station in stations)
        {
            if (station != null && station.part == part)
                return station;
        }

        return null;
    }

    /// <summary>
    /// Camino del kart entre dos estaciones: una curva que pasa cerca del
    /// punto libre del taller (curva de Bézier con ese punto de control), o
    /// línea recta si no hay punto.
    /// </summary>
    private Vector3 GetPathPosition(Vector3 from, Vector3 to, float t)
    {
        if (transitPoint == null || !moving)
            return Vector3.Lerp(from, to, t);

        Vector3 control = transitPoint.position;
        control.y = Mathf.Lerp(from.y, to.y, 0.5f);

        float u = 1f - t;
        return u * u * from + 2f * u * t * control + t * t * to;
    }

    private Vector3 GetKartPosition(Station station)
    {
        Vector3 position = station.kartPoint.position;

        if (placeOnFloor)
            position += station.kartPoint.up * floorOffset;

        return position;
    }

    private float GetFieldOfView(Station station)
    {
        Camera marker = station.cameraPoint.GetComponent<Camera>();
        return marker != null ? marker.fieldOfView : defaultFieldOfView;
    }

    /// <summary>Cambió una pieza: las ruedas nuevas pueden ser de otro tamaño.</summary>
    private void HandleLoadoutChanged()
    {
        MeasureFloorOffset();
    }

    /// <summary>Cuánto está el origen del kart por encima de su parte más baja.</summary>
    private void MeasureFloorOffset()
    {
        if (kart == null)
            return;

        Renderer[] renderers = kart.GetComponentsInChildren<Renderer>();
        bool found = false;
        float lowest = float.MaxValue;
        Bounds all = new Bounds();

        foreach (Renderer renderer in renderers)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            lowest = Mathf.Min(lowest, renderer.bounds.min.y);

            if (found)
                all.Encapsulate(renderer.bounds);
            else
                all = renderer.bounds;

            found = true;
        }

        if (!found)
            return;

        floorOffset = Mathf.Max(0f, kart.position.y - lowest);

        // Centro del kart para girarlo con la R (solo en el plano del piso).
        spinPivotLocal = kart.InverseTransformPoint(all.center);
        spinPivotLocal.y = 0f;
    }

    /// <summary>La toma de la estación está pegada al kart (la del volante).</summary>
    private bool IsCloseShot(Station station)
    {
        return station.cameraPoint != null &&
            (station.cameraPoint.position - GetKartPosition(station)).magnitude < closeShotDistance;
    }

    private static bool SpinPressed()
    {
#if ENABLE_INPUT_SYSTEM
        UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
        return keyboard != null && keyboard.rKey.isPressed;
#else
        return Input.GetKey(KeyCode.R);
#endif
    }

    // Arranca y frena suave.
    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t * (t * (6f * t - 15f) + 10f);
    }
}
