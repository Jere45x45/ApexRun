using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BotController : MonoBehaviour
{
     public KartBehaviour kart;

    void Update()
    {
        kart.SetInputs(1f, 0f, false);
    }
}
