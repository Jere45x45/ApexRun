using UnityEngine;
using System.Collections;
using UnityEngine.Networking;
using TMPro;

public class MechanicAI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_InputField Epregunta;
    [SerializeField] private TMP_Text RTATxt;

    private string ollamaURL = "http://localhost:11434/api/generate";

    public void Preguntar()
    {
        string Pregunta = Epregunta.text;

        if (string.IsNullOrEmpty(Pregunta))
        {
            RTATxt.text = "Escribí tu pregunta";
            return;
        }

        RTATxt.text = "Pensando...";

        StartCoroutine(SendQuestion(Pregunta));
    }

    private IEnumerator SendQuestion(string Pregunta)
    // IEnumerator permite hacer una corrutina para que no se trabe todo el juego
    {
        string json = JsonUtility.ToJson(new OllamaRequest
        {
            model = "llama3.2",

            prompt = "Sos el mecánico del juego Apex Run. " +
                     "Respondé en español. " +
                     "Respondé de forma muy breve, clara y fácil de entender. " +
                     "Usá como máximo 2 o 3 frases. " +
                     "No des explicaciones largas ni listas. " +
                     "Ayudá al jugador con preguntas sobre karts, motores, ruedas, " +
                     "aerodinámica y carreras.\n\n" +
                     "Pregunta del jugador: " + Pregunta,

            stream = false
        });

        using (UnityWebRequest request = new UnityWebRequest(ollamaURL, "POST"))
        {
            byte[] cuerpito = System.Text.Encoding.UTF8.GetBytes(json);

            request.uploadHandler = new UploadHandlerRaw(cuerpito);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                RTATxt.text = "No pude conectarme con el mecánico.";
                Debug.LogError(request.error);
            }
            else
            {
                OllamaResponse response =
                    JsonUtility.FromJson<OllamaResponse>(
                        request.downloadHandler.text
                    );

                RTATxt.text = response.response;
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
}