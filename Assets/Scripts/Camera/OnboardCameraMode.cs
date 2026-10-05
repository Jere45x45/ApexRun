using System;
using UnityEngine;

/// <summary>
/// Cámara fija al kart: se mueve exactamente con el chasis, como una cámara
/// atornillada. Sirve para la de la trompa (la 3 de BeamNG, "Onboard.Hood") y
/// para la relativa (la 5 de BeamNG, las "onboard" de Assetto Corsa): con
/// varias posiciones, apretar otra vez su número pasa a la siguiente.
/// </summary>
public class OnboardCameraMode : KartCameraMode
{
    [Serializable]
    public class OnboardView
    {
        public string name = "Trompa";

        [Tooltip("Posición en coordenadas del kart (m).")]
        public Vector3 localPosition = new Vector3(0f, 0.55f, 0.55f);

        [Tooltip("Rotación respecto del kart (grados): x = hacia abajo, y = hacia la derecha.")]
        public Vector3 localEulerAngles = new Vector3(8f, 0f, 0f);

        [Range(10f, 120f)]
        public float fieldOfView = 65f;
    }

    [Header("Posiciones")]
    [SerializeField] private OnboardView[] views = { new OnboardView() };

    private int currentView;

    private OnboardView CurrentView =>
        views != null && views.Length > 0 ? views[Mathf.Clamp(currentView, 0, views.Length - 1)] : null;

    public override string DisplayName
    {
        get
        {
            OnboardView view = CurrentView;

            if (view == null || views.Length < 2)
                return base.DisplayName;

            return base.DisplayName + ": " + view.name;
        }
    }

    public override float FieldOfView => CurrentView != null ? CurrentView.fieldOfView : fieldOfView;

    private void Reset()
    {
        nearClipPlane = 0.03f;
    }

    public override void NextVariant(KartCameraContext context)
    {
        if (views != null && views.Length > 0)
        {
            currentView = (currentView + 1) % views.Length;
        }
    }

    public override void UpdateCamera(
        KartCameraContext context,
        float deltaTime,
        out Vector3 position,
        out Quaternion rotation)
    {
        Transform kart = context.Target;
        OnboardView view = CurrentView;

        if (view == null)
        {
            position = kart.position;
            rotation = kart.rotation;
            return;
        }

        position = kart.TransformPoint(view.localPosition);
        rotation = kart.rotation * Quaternion.Euler(view.localEulerAngles);
    }
}
