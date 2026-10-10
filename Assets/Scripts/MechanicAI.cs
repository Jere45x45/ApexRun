using UnityEngine;
using System.Collections;
using UnityEngine.Networking;
using TMPro;

public class MechanicAI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_InputField Epregunta;
    [SerializeField] private TMP_Text RTATxt;

    [Header("Panel")]
    [SerializeField] private GameObject mechanicPanel;
    [SerializeField] private GameObject mechanicButton;

    private string ollamaURL = "http://localhost:11434/api/generate";

    public void Preguntar()
    {
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
                "\n\nPregunta del jugador: " + pregunta,
            stream = false
        };

        string json = JsonUtility.ToJson(datos);


        using (UnityWebRequest request = new UnityWebRequest(ollamaURL, "POST"))
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json);

            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            Debug.Log("RESULTADO HTTP: " + request.responseCode);
            Debug.Log("RESPUESTA OLLAMA: " + request.downloadHandler.text);

            if (request.result != UnityWebRequest.Result.Success)
            {
                RTATxt.text = "Error conectando con Ollama.";
                Debug.LogError("ERROR OLLAMA: " + request.error);
                yield break;
            }

            OllamaResponse respuesta =
                JsonUtility.FromJson<OllamaResponse>(
                    request.downloadHandler.text
                );

            if (respuesta != null && !string.IsNullOrEmpty(respuesta.response))
            {
                RTATxt.text = respuesta.response;
            }
            else
            {
                RTATxt.text = "Ollama respondió, pero no pude leer la respuesta.";
            }
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
        mechanicPanel.SetActive(true);
        mechanicButton.SetActive(false);
    }

    public void CerrarPanel()
    {
        mechanicPanel.SetActive(false);
        mechanicButton.SetActive(true);
    }
}