using Unity;
using System.Collections;
using UnityEngine.Networking;
using TMPro;

public class MechanicAI : MonoBehaviour
{
   [Header("UI")]
   [SerializeField] private TMP_InpuField Epregunta;
   [SerializeField] private TMP_Text RTATxt;

   private string ollamaURL = "http://localhost:11434/api/generate";

   public void Preguntar()
   {
    string Pregunta = Epregunta.text; 

    if (string.IsNullOrEmpty(Pregunta))
    {
        RTATxt.text = "Escribi tu pregunta";
        return;
    }

    RTATxt.text = "Pensando";

    StartCoroutine(SendQuestion(Pregunta));
   }

   private IEnumerator SendQuestion(string Pregunta)
   //IEnumartor permite hacer una corutina asi no se traba todo el juego (lo puso juli)
   {
     string json = JsonUtility.ToJson(new OllamaRequest
     {
        model = "llama3.2",
        prompt = "Sos el mecánico del juego Apex Run. " +
                     "Respondé en español, de forma clara y breve. " +
                     "Ayudá al jugador con preguntas sobre karts, motores, ruedas, " +
                     "aerodinámica y carreras.\n\n" +
                     "Pregunta del jugador: " + question,
        stream = false
     });
     using (UnityWebRequest request = new UnityWebRequest(ollamaURL, "POST"))
     {
        byte[] cuerpito = System.Text.Encoding.UTF8.GetBytes(json);

        request.uploadHandler = new UploadHandlerRaw(cuerpito);
     } //no esta terminado pero bn
   }
}
