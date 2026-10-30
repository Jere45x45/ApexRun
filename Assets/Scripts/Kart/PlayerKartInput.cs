using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerKartInput : MonoBehaviour
{
    [Header("Kart")]
    [SerializeField] private KartBehaviour kart;

    private InputAction throttleAction;
    private InputAction steeringAction;
    private InputAction brakeAction;

    private void Awake()
    {
        if (kart == null)
        {
            kart = GetComponent<KartBehaviour>();
        }

        if (kart == null)
        {
            Debug.LogError(
                "PlayerKartInput no encontró un KartBehaviour.",
                this
            );

            return;
        }

        CreateInputActions();
    }

    private void OnEnable()
    {
        throttleAction?.Enable();
        steeringAction?.Enable();
        brakeAction?.Enable();
    }

    private void OnDisable()
    {
        throttleAction?.Disable();
        steeringAction?.Disable();
        brakeAction?.Disable();
    }

    private void OnDestroy()
    {
        throttleAction?.Dispose();
        steeringAction?.Dispose();
        brakeAction?.Dispose();
    }

    private void Update()
    {
        if (kart == null)
            return;

        float throttle =
            throttleAction.ReadValue<float>();

        float steering =
            steeringAction.ReadValue<float>();

        bool brake =
            brakeAction.IsPressed();

        // canReverse: con el kart casi quieto, la S lo lleva para atrás
        // (por ejemplo, para salir de un muro). Los bots no lo usan.
        kart.SetInputs(
            throttle,
            steering,
            brake,
            canReverse: true
        );
    }

    private void CreateInputActions()
    {
        throttleAction = new InputAction(
            "Throttle",
            InputActionType.Value,
            expectedControlType: "Axis"
        );

        throttleAction
            .AddCompositeBinding("1DAxis")
            .With("negative", "<Keyboard>/s")
            .With("positive", "<Keyboard>/w");

        // Las flechas hacen lo mismo que W y S.
        throttleAction
            .AddCompositeBinding("1DAxis")
            .With("negative", "<Keyboard>/downArrow")
            .With("positive", "<Keyboard>/upArrow");

        throttleAction.AddBinding(
            "<Gamepad>/rightTrigger"
        );

        steeringAction = new InputAction(
            "Steering",
            InputActionType.Value,
            expectedControlType: "Axis"
        );

        steeringAction
            .AddCompositeBinding("1DAxis")
            .With("negative", "<Keyboard>/a")
            .With("positive", "<Keyboard>/d");

        // Las flechas hacen lo mismo que A y D.
        steeringAction
            .AddCompositeBinding("1DAxis")
            .With("negative", "<Keyboard>/leftArrow")
            .With("positive", "<Keyboard>/rightArrow");

        steeringAction.AddBinding(
            "<Gamepad>/leftStick/x"
        );

        brakeAction = new InputAction(
            "Brake",
            InputActionType.Button
        );

        brakeAction.AddBinding(
            "<Keyboard>/space"
        );

        brakeAction.AddBinding(
            "<Gamepad>/leftTrigger"
        );
    }
}