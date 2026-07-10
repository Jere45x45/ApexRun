using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CarController : MonoBehaviour
{
    public float speed = 10f;
    public float turnSpeed = 100f;

    void Update()
    {
        float move = Input.GetAxis("Vertical");
        float turn = Input.GetAxis("Horizontal");

        transform.Translate(Vector3.forward * move * speed * Time.deltaTime);
        transform.Rotate(Vector3.up * turn * turnSpeed * Time.deltaTime);
    }
}
