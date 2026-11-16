using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Lleva a la carrera la configuración que eligió el anfitrión en el menú
/// (RaceSetupStore) y el estado del fin de semana (RaceSession), y se los pasa
/// a todos:
/// - Vueltas. En la qualy no hay cantidad (la tanda es con tiempo): se ponen
///   QualifyingLaps y la vuelta no se muestra.
/// - Fase (qualy o carrera), sentido, duración de la qualy, fecha y cantidad
///   de fechas del campeonato, y la tabla del campeonato.
/// - En la qualy, los mejores tiempos de cada kart (QualifyingSession).
/// - Boxes: si la parada es obligatoria y cuántas hizo cada kart (PitStops);
///   los clientes avisan sus paradas con ReportPitStop.
/// - En el anfitrión se aplica en Awake, antes de que aparezcan los objetos de
///   red (así la grilla ya está del lado correcto y RaceWeather sortea el
///   cambio de clima con las vueltas reales).
/// - Los demás lo reciben por red, lo copian en RaceSession y dan vuelta la
///   pista si hace falta (TrackDirection).
/// El clima viaja aparte, en RaceWeather.
/// </summary>
public class RaceSetupSync : NetworkBehaviour
{
    /// <summary>Vueltas en la qualy: no se llega nunca (la termina el tiempo).</summary>
    public const int QualifyingLaps = 999;

    [SerializeField] private RaceLapManager lapManager;

    private readonly NetworkVariable<int> laps = new NetworkVariable<int>(RaceSetupStore.DefaultLaps);
    private readonly NetworkVariable<byte> phase = new NetworkVariable<byte>(0);
    private readonly NetworkVariable<bool> reversed = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<float> qualifyingSeconds = new NetworkVariable<float>(0f);
    private readonly NetworkVariable<int> round = new NetworkVariable<int>(1);
    private readonly NetworkVariable<int> rounds = new NetworkVariable<int>(0);
    private readonly NetworkVariable<FixedString4096Bytes> standings = new NetworkVariable<FixedString4096Bytes>();
    private readonly NetworkVariable<FixedString4096Bytes> qualifyingTimes = new NetworkVariable<FixedString4096Bytes>();
    private readonly NetworkVariable<bool> mandatoryPitStop = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<FixedString512Bytes> pitStops = new NetworkVariable<FixedString512Bytes>();

    /// <summary>El de la escena (hay uno solo).</summary>
    public static RaceSetupSync Instance { get; private set; }

    /// <summary>Mejores tiempos de la qualy, como los publica el servidor (ver QualifyingSession).</summary>
    public string QualifyingTimes => qualifyingTimes.Value.ToString();

    /// <summary>Paradas en boxes de cada kart, como las publica el servidor ("id:paradas;", ver PitStops).</summary>
    public string PitStopsText => pitStops.Value.ToString();

    private void Awake()
    {
        Instance = this;

        if (NetworkRole.IsAuthority)
        {
            Apply(LapsForSession());

            // La pista al revés va antes de que aparezcan los karts (la
            // grilla cambia de lado).
            TrackDirection.Apply(RaceSession.Reversed);
        }
    }

    public override void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        RaceSession.Changed -= HandleSessionChanged;
        base.OnDestroy();
    }

    private static int LapsForSession()
    {
        if (RaceSession.IsQualifying)
            return QualifyingLaps;

        // La contrarreloj de la tabla es de una sola vuelta.
        return RaceSession.TimeTrial ? 1 : RaceSetupStore.Laps;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            laps.Value = LapsForSession();
            Apply(laps.Value);

            PublishSession();
            RaceSession.Changed += HandleSessionChanged;

            Debug.Log($"CARRERA: {RaceSession.Title()}, " +
                      (RaceSession.IsQualifying ? $"{RaceSession.QualifyingSeconds:0} s de qualy" : $"{laps.Value} vueltas") +
                      (RaceSession.Reversed ? ", al revés." : ", al derecho."), this);
            return;
        }

        Apply(laps.Value);
        laps.OnValueChanged += HandleLapsChanged;
        standings.OnValueChanged += HandleStandingsChanged;

        CopyToSession();
        TrackDirection.Apply(reversed.Value);
    }

    public override void OnNetworkDespawn()
    {
        laps.OnValueChanged -= HandleLapsChanged;
        standings.OnValueChanged -= HandleStandingsChanged;
        RaceSession.Changed -= HandleSessionChanged;
    }

    /// <summary>Servidor: publica los mejores tiempos de la qualy.</summary>
    public void PublishQualifyingTimes(string value)
    {
        if (!IsSpawned || !IsServer)
            return;

        var text = new FixedString4096Bytes();
        text.CopyFromTruncated(value ?? "");

        if (!qualifyingTimes.Value.Equals(text))
            qualifyingTimes.Value = text;
    }

    /// <summary>Servidor: publica las paradas en boxes.</summary>
    public void PublishPitStops(string value)
    {
        if (!IsSpawned || !IsServer)
            return;

        var text = new FixedString512Bytes();
        text.CopyFromTruncated(value ?? "");

        if (!pitStops.Value.Equals(text))
            pitStops.Value = text;
    }

    /// <summary>El dueño de un kart terminó una parada en boxes: se la cuenta el servidor.</summary>
    public void ReportPitStop(NetworkObject kart)
    {
        if (kart == null || !IsSpawned)
            return;

        if (IsServer)
        {
            if (PitStops.Instance != null)
                PitStops.Instance.ServerAddStop(kart.GetComponent<Rigidbody>());

            return;
        }

        ReportPitStopRpc(kart);
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void ReportPitStopRpc(NetworkObjectReference kartReference, RpcParams rpcParams = default)
    {
        // Solo el dueño del kart avisa sus paradas.
        if (!kartReference.TryGet(out NetworkObject kart) || kart.OwnerClientId != rpcParams.Receive.SenderClientId)
            return;

        if (PitStops.Instance != null)
            PitStops.Instance.ServerAddStop(kart.GetComponent<Rigidbody>());
    }

    /// <summary>Servidor: lo de RaceSession, para los demás.</summary>
    private void PublishSession()
    {
        phase.Value = (byte)RaceSession.Phase;
        reversed.Value = RaceSession.Reversed;
        qualifyingSeconds.Value = RaceSession.QualifyingSeconds;
        round.Value = RaceSession.Round;
        rounds.Value = RaceSession.Rounds;
        mandatoryPitStop.Value = RaceSession.MandatoryPitStop;

        var text = new FixedString4096Bytes();
        text.CopyFromTruncated(RaceSession.StandingsText ?? "");
        standings.Value = text;
    }

    private void HandleSessionChanged()
    {
        // En esta escena solo cambia la tabla (al terminar la carrera). La fase
        // y la fecha nuevas viajan con la escena siguiente.
        if (!IsSpawned || !IsServer)
            return;

        var text = new FixedString4096Bytes();
        text.CopyFromTruncated(RaceSession.StandingsText ?? "");

        if (!standings.Value.Equals(text))
            standings.Value = text;
    }

    /// <summary>Clientes: lo que llegó, en RaceSession (así todos lo leen de ahí).</summary>
    private void CopyToSession()
    {
        RaceSession.Phase = (RaceSession.SessionPhase)phase.Value;
        RaceSession.Reversed = reversed.Value;
        RaceSession.QualifyingSeconds = qualifyingSeconds.Value;
        RaceSession.Round = round.Value;
        RaceSession.Rounds = rounds.Value;
        RaceSession.MandatoryPitStop = mandatoryPitStop.Value;
        RaceSession.StandingsText = standings.Value.ToString();
        RaceSession.NotifyChanged();
    }

    private void HandleLapsChanged(int previous, int current)
    {
        Apply(current);
    }

    private void HandleStandingsChanged(FixedString4096Bytes previous, FixedString4096Bytes current)
    {
        RaceSession.StandingsText = current.ToString();
        RaceSession.NotifyChanged();
    }

    private void Apply(int value)
    {
        if (lapManager != null)
            lapManager.SetTotalLaps(value);
    }
}
