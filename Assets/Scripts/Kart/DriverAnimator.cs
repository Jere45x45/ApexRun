using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Animación del piloto: mueve los brazos con el volante.
/// Cada animación de Blender (DoblaDer / DoblaIzq) va del centro al giro
/// máximo y vuelve; acá no se reproducen: se usa el punto que corresponde al
/// giro actual (centro = principio, giro máximo = peakFraction de la animación).
/// Cada volante tiene su juego de animaciones (Kart, F1, Avión, Passat, Barco):
/// se elige por el nombre del modelo del volante montado y cambia solo si se
/// cambia el volante.
/// Va en el objeto del piloto, que tiene que tener un Animator (sin controller).
/// Funciona en todos los karts: el propio, los de red y los bots, porque usa
/// KartBehaviour.SteerAngle.
/// </summary>
[RequireComponent(typeof(Animator))]
public class DriverAnimator : MonoBehaviour
{
    /// <summary>Animaciones del piloto para un volante.</summary>
    [Serializable]
    private struct WheelClips
    {
        [Tooltip("Parte del nombre del modelo del volante (por ejemplo \"Passat\" para VolantePassat).")]
        public string wheelName;

        public AnimationClip turnRight;
        public AnimationClip turnLeft;
    }

    [SerializeField] private KartBehaviour kart;
    [SerializeField] private KartModelController modelController;

    [Tooltip("Un juego de animaciones por volante. Si el volante montado no coincide con ninguno, se usa el primero.")]
    [SerializeField] private WheelClips[] wheels = new WheelClips[0];

    [Tooltip("Ángulo de las ruedas con el que el piloto llega al giro máximo de la animación (grados). Es el máximo del chasis.")]
    [SerializeField, Min(1f)] private float fullLockAngle = 25f;

    [Tooltip("En qué parte de la animación está el giro máximo (0,5 = la mitad).")]
    [SerializeField, Range(0.1f, 1f)] private float peakFraction = 0.5f;

    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable right;
    private AnimationClipPlayable left;
    private int usingIndex = -1;
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
        lastWheelModel = CurrentWheelModel();
        Build(FindClipsIndex());
    }

    private void OnDisable()
    {
        if (graph.IsValid())
            graph.Destroy();

        built = false;
        usingIndex = -1;
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
            int index = FindClipsIndex();

            if (!built || index != usingIndex)
                Build(index);
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

    private void Build(int index)
    {
        if (graph.IsValid())
            graph.Destroy();

        built = false;
        usingIndex = index;

        if (index < 0 || index >= wheels.Length)
            return;

        AnimationClip r = wheels[index].turnRight;
        AnimationClip l = wheels[index].turnLeft;

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

    /// <summary>
    /// El juego de animaciones del volante montado: el primero cuyo nombre
    /// aparece en el nombre del modelo. Si no hay volante o no coincide
    /// ninguno, el primero de la lista.
    /// </summary>
    private int FindClipsIndex()
    {
        if (wheels.Length == 0)
            return -1;

        GameObject model = CurrentWheelModel();

        if (model != null)
        {
            for (int i = 0; i < wheels.Length; i++)
            {
                string word = wheels[i].wheelName;

                if (!string.IsNullOrEmpty(word) && model.name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }
        }

        return 0;
    }
}
