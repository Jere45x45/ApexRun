using UnityEngine;

public class WeatherTester : MonoBehaviour
{
    private void Update()
    {
        if (WeatherManager.Instance == null)
            return;

        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            WeatherManager.Instance.SetNormal();
            Debug.Log("Clima cambiando a: Normal");
        }
        
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            WeatherManager.Instance.SetRain();
            Debug.Log("Clima cambiando a: Lluvia");
        }
        
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            WeatherManager.Instance.SetSnow();
            Debug.Log("Clima cambiando a: Nieve");
        }
    }
}