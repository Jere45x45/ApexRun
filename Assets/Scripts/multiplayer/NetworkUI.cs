using Unity.Netcode;
using UnityEngine;

public class NetworkUI : MonoBehaviour
{
    private void OnGUI()
    {
        // Verifica que el NetworkManager exista en la escena
        if (NetworkManager.Singleton == null)
        {
            GUI.Label(new Rect(10, 10, 300, 20), "Buscando NetworkManager...");
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 200, 200));

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (GUILayout.Button("Crear Sala (Host)"))
            {
                NetworkManager.Singleton.StartHost();
            }
            if (GUILayout.Button("Unirse (Cliente)"))
            {
                NetworkManager.Singleton.StartClient();
            }
        }

        GUILayout.EndArea();
    }
}