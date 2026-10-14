using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Lleva a la carrera la configuración que eligió el anfitrión en el menú
/// (RaceSetupStore) y se la pasa a todos: hoy, la cantidad de vueltas.
/// - En el anfitrión se aplica en Awake, antes de que aparezcan los objetos de
///   red (así RaceWeather ya sortea el cambio de clima con las vueltas reales).
/// - Los demás la reciben por red y la aplican en su RaceLapManager, para que
///   el HUD muestre "VUELTA 1/5" y no la cantidad que trae la escena.
/// El clima viaja aparte, en RaceWeather.
/// </summary>
public class RaceSetupSync : NetworkBehaviour
{
    [SerializeField] private RaceLapManager lapManager;

    private readonly NetworkVariable<int> laps = new NetworkVariable<int>(RaceSetupStore.DefaultLaps);

    private void Awake()
    {
        if (NetworkRole.IsAuthority)
            Apply(RaceSetupStore.Laps);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            laps.Value = RaceSetupStore.Laps;
            Apply(laps.Value);

            Debug.Log($"CARRERA: {laps.Value} vueltas.", this);
            return;
        }

        Apply(laps.Value);
        laps.OnValueChanged += HandleLapsChanged;
    }

    public override void OnNetworkDespawn()
    {
        laps.OnValueChanged -= HandleLapsChanged;
    }

    private void HandleLapsChanged(int previous, int current)
    {
        Apply(current);
    }

    private void Apply(int value)
    {
        if (lapManager != null)
            lapManager.SetTotalLaps(value);
    }
}
