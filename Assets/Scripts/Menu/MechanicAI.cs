using UnityEngine;
using System.Collections;
using System.Text;
using UnityEngine.Networking;
using TMPro;

/// <summary>
/// Pepe, el mecánico del juego: un chat con una IA que corre en la
/// computadora (Ollama, modelo llama3.2).
/// - En el menú de Inicio está en el panel AI Mechanic (lo abre y lo cierra
///   el MenuManager). En la escena de Juli (JuliVersionMINI) lo abren y
///   cierran AbrirPanel y CerrarPanel.
/// - Si tiene el catálogo de piezas, Pepe sabe qué piezas tiene puestas el
///   kart del jugador (las que eligió en el Catálogo) y cuáles hay en el
///   taller, para poder aconsejarlo.
/// </summary>
public class MechanicAI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_InputField Epregunta;
    [SerializeField] private TMP_Text RTATxt;

    [Header("Panel")]
    [Tooltip("Panel del mecánico. Lo usan AbrirPanel y CerrarPanel; en el menú de Inicio puede quedar vacío.")]
    [SerializeField] private GameObject mechanicPanel;

    [Tooltip("Botón que abre el panel. Lo usan AbrirPanel y CerrarPanel; en el menú de Inicio puede quedar vacío.")]
    [SerializeField] private GameObject mechanicButton;

    [Header("Kart del jugador")]
    [Tooltip("Catálogo de piezas. Con él, Pepe sabe qué piezas tiene el kart del jugador y cuáles hay en el taller. Vacío = no sabe nada del kart.")]
    [SerializeField] private PartCatalog catalog;

    [Header("Ollama")]
    [Tooltip("Cuánto espera la respuesta antes de rendirse (s).")]
    [SerializeField, Min(1)] private int timeoutSeconds = 60;

    private string ollamaURL = "http://localhost:11434/api/generate";

    // Hay una pregunta esperando respuesta.
    private bool waiting;

    private static readonly PartType[] KartParts =
    {
        PartType.Engine,
        PartType.Wheels,
        PartType.AeroKit,
        PartType.SteeringWheel
    };

    private void OnDisable()
    {
        // Si se cierra el panel mientras Pepe piensa, la pregunta se pierde.
        waiting = false;
    }

    public void Preguntar()
    {
        if (waiting)
            return;

        string pregunta = Epregunta.text;

        if (string.IsNullOrWhiteSpace(pregunta))
        {
            RTATxt.text = "Escribí tu pregunta.";
            return;
        }

        RTATxt.text = "Pensando...";

        StartCoroutine(SendQuestion(pregunta));
    }

    private IEnumerator SendQuestion(string pregunta)
    {
        waiting = true;

        OllamaRequest datos = new OllamaRequest
        {
            model = "llama3.2",
            prompt =
                "Sos Pepe, el mecánico del juego Apex Run. " +
                "Respondé siempre en español. " +
                "Respondé de forma breve, clara y fácil de entender. " +
                "Usá como máximo 2 o 3 frases. " +
                "No hagas listas ni explicaciones largas. " +
                "Ayudá al jugador con preguntas sobre karts, motores, ruedas, " +
                "aerodinámica y carreras. " +
                "Si la pregunta no tiene relación con el juego o los autos, " +
                "decí brevemente que solo podés ayudar con temas de mecánica. " +
                DescribeKart() +
                "\n\nPregunta del jugador: " + pregunta,
            stream = false
        };

        string json = JsonUtility.ToJson(datos);


        using (UnityWebRequest request = new UnityWebRequest(ollamaURL, "POST"))
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json);

            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = timeoutSeconds;

            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            waiting = false;

            Debug.Log("RESULTADO HTTP: " + request.responseCode);
            Debug.Log("RESPUESTA OLLAMA: " + request.downloadHandler.text);

            if (request.result != UnityWebRequest.Result.Success)
            {
                // Sin respuesta: Ollama no está abierto (o no tiene el modelo).
                RTATxt.text = request.responseCode == 404
                    ? "Pepe no tiene el modelo llama3.2. Instalalo con: ollama pull llama3.2"
                    : "Pepe no está en el taller: abrí Ollama y volvé a preguntar.";
                Debug.LogError("ERROR OLLAMA: " + request.error);
                yield break;
            }

            OllamaResponse respuesta =
                JsonUtility.FromJson<OllamaResponse>(
                    request.downloadHandler.text
                );

            if (respuesta != null && !string.IsNullOrEmpty(respuesta.response))
            {
                RTATxt.text = respuesta.response.Trim();
            }
            else
            {
                RTATxt.text = "Ollama respondió, pero no pude leer la respuesta.";
            }
        }
    }

    /// <summary>
    /// Lo que Pepe sabe del kart del jugador: las piezas que tiene puestas
    /// (las elegidas en el Catálogo) y las que hay en el taller.
    /// </summary>
    private string DescribeKart()
    {
        if (catalog == null)
            return "";

        StringBuilder text = new StringBuilder();
        text.Append("\n\nDatos del juego (usalos solo si sirven para la pregunta). ");
        text.Append("En el Catálogo (el taller) el jugador elige el motor, las ruedas, el kit aerodinámico y el volante de su kart. ");

        foreach (PartType type in KartParts)
        {
            KartPart installed = catalog.GetPartByID(KartLoadoutStore.GetSelectedID(type));

            text.Append("\n").Append(PartTypeName(type)).Append(" puesto: ");
            text.Append(installed != null ? Describe(installed) : "el que viene de fábrica");
            text.Append(". Opciones en el taller: ");

            bool first = true;

            foreach (KartPart part in catalog.GetParts(type))
            {
                if (!first)
                    text.Append("; ");

                text.Append(Describe(part));
                first = false;
            }

            text.Append(".");
        }

        return text.ToString();
    }

    /// <summary>Nombre de la pieza y lo que cambia en el manejo (por ejemplo "+ Aceleración").</summary>
    private static string Describe(KartPart part)
    {
        string name = string.IsNullOrWhiteSpace(part.partName) ? part.name : part.partName;

        if (part.effects == null || part.effects.Length == 0)
            return name;

        return name + " (" + string.Join(", ", part.effects) + ")";
    }

    private static string PartTypeName(PartType type)
    {
        switch (type)
        {
            case PartType.Engine: return "Motor";
            case PartType.Wheels: return "Ruedas";
            case PartType.AeroKit: return "Kit aerodinámico";
            case PartType.SteeringWheel: return "Volante";
            default: return type.ToString();
        }
    }

    [System.Serializable]
    private class OllamaRequest
    {
        public string model;
        public string prompt;
        public bool stream;
    }

    [System.Serializable]
    private class OllamaResponse
    {
        public string response;
    }

    public void AbrirPanel()
    {
        if (mechanicPanel != null)
            mechanicPanel.SetActive(true);

        if (mechanicButton != null)
            mechanicButton.SetActive(false);
    }

    public void CerrarPanel()
    {
        if (mechanicPanel != null)
            mechanicPanel.SetActive(false);

        if (mechanicButton != null)
            mechanicButton.SetActive(true);
    }
}
