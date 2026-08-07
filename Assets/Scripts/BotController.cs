using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BotController : MonoBehaviour
{
    public KartBehaviour kart;

    void Update()
    {
        float steering = Mathf.Sin(Time.time);

        kart.SetInputs(
            1f,
            steering,
            false
        );
    }
}
