using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Animación del piloto: mueve los brazos con el volante.
/// Cada animación de Blender (DoblaDer / DoblaIzq) va del centro al giro
/// máximo y vuelve; acá no se reproducen: se usa el punto que corresponde al
/// giro actual (centro = principio, giro máximo = mitad de la animación).
/// Hay dos juegos: uno para volantes chicos (karting, F1, avión) y otro para
/// volantes redondos grandes (Passat, timón de barco).
/// Va en el objeto del piloto, que tiene que tener un Animator (sin controller).
/// Funciona en todos los karts: el propio, los de red y los bots, porque usa
/// KartBehaviour.SteerAngle.
/// </summary>
[RequireComponent(typeof(Animator))]
public class DriverAnimator : MonoBehaviour
{
    [SerializeField] private KartBehaviour kart;
    [SerializeField] private KartModelController modelController;

    [Header("Volantes chicos (karting, F1, avión)")]
    [SerializeField] private AnimationClip turnRight;
    [SerializeField] private AnimationClip turnLeft;

    [Header("Volantes redondos grandes")]
    [SerializeField] private AnimationClip turnRightRound;
    [SerializeField] private AnimationClip turnLeftRound;

    [Tooltip("Si el modelo del volante tiene alguna de estas palabras en el nombre, usa las animaciones de volante redondo.")]
    [SerializeField] private string[] roundWheelNames = { "Passat", "Barco" };

    [Tooltip("Ángulo de las ruedas con el que el piloto llega al giro máximo de la animación (grados). Es el máximo del chasis.")]
    [SerializeField, Min(1f)] private float fullLockAngle = 25f;

    [Tooltip("En qué parte de la animación está el giro máximo (0,5 = la mitad).")]
    [SerializeField, Range(0.1f, 1f)] private float peakFraction = 0.5f;

    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable right;
    private AnimationClipPlayable left;
    private bool usingRound;
    private bool built;
    private GameObject lastWheelModel;

    private void Awake()
    {
        if (kart == null)
            kart = GetComponentInParent<KartBehaviour>();

        if (modelController == null && kart != null)
            modelController = kart.GetComponentInChildren<KartModelController>(true);
    }

    private void OnEnable()
    {
        Build(IsRoundWheel());
    }

    private void OnDisable()
    {
        if (graph.IsValid())
            graph.Destroy();

        built = false;
    }

    private void LateUpdate()
    {
        if (kart == null)
            return;

        // Si cambió el volante (taller), cambia el juego de animaciones.
        GameObject wheelModel = CurrentWheelModel();

        if (wheelModel != lastWheelModel)
        {
            lastWheelModel = wheelModel;
            bool round = IsRoundWheel();

            if (!built || round != usingRound)
                Build(round);
        }

        if (!built)
            return;

        float steer = Mathf.Clamp(kart.SteerAngle / fullLockAngle, -1f, 1f);
        bool toRight = steer >= 0f;

        mixer.SetInputWeight(0, toRight ? 1f : 0f);
        mixer.SetInputWeight(1, toRight ? 0f : 1f);

        AnimationClipPlayable active = toRight ? right : left;
        AnimationClip clip = active.GetAnimationClip();

        if (clip != null)
            active.SetTime(Mathf.Abs(steer) * peakFraction * clip.length);

        graph.Evaluate();
    }

    private void Build(bool round)
    {
        if (graph.IsValid())
            graph.Destroy();

        built = false;
        usingRound = round;

        AnimationClip r = round && turnRightRound != null ? turnRightRound : turnRight;
        AnimationClip l = round && turnLeftRound != null ? turnLeftRound : turnLeft;

        if (r == null || l == null)
            return;

        graph = PlayableGraph.Create(name + " (piloto)");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Piloto", GetComponent<Animator>());
        mixer = AnimationMixerPlayable.Create(graph, 2);
        output.SetSourcePlayable(mixer);

        right = AnimationClipPlayable.Create(graph, r);
        left = AnimationClipPlayable.Create(graph, l);
        right.Pause();
        left.Pause();

        graph.Connect(right, 0, mixer, 0);
        graph.Connect(left, 0, mixer, 1);
        mixer.SetInputWeight(0, 1f);

        graph.Play();
        built = true;
    }

    private GameObject CurrentWheelModel()
    {
        if (modelController == null || modelController.SteeringWheelSlot == null)
            return null;

        return modelController.SteeringWheelSlot.CurrentInstance;
    }

    private bool IsRoundWheel()
    {
        GameObject model = CurrentWheelModel();

        if (model == null)
            return false;

        foreach (string word in roundWheelNames)
        {
            if (!string.IsNullOrEmpty(word) && model.name.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }
}
