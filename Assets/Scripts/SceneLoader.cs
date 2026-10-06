using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Carga una escena por nombre. Pensado para el OnClick de los botones de
/// menú (por ejemplo, Un jugador → Race). La escena tiene que estar en
/// Build Settings.
/// </summary>
public class SceneLoader : MonoBehaviour
{
    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("SceneLoader: falta el nombre de la escena.", this);
            return;
        }

        SceneManager.LoadScene(sceneName);
    }
}
