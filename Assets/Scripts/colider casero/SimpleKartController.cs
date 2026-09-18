using UnityEngine;

public class SimpleKartController : MonoBehaviour
{
    [Header("Ruedas Nativas (WheelCollider)")]
    public WheelCollider frontLeft;
    public WheelCollider frontRight;
    public WheelCollider rearLeft;
    public WheelCollider rearRight;

    [Header("Configuración de Prueba")]
    public float maxMotorTorque = 500f; 
    public float maxSteeringAngle = 30f;
    public float brakeTorque = 1000f;

    private void FixedUpdate()
    {
        // 1. Leer Input puro
        float motorInput = Input.GetAxisRaw("Vertical");
        float steerInput = Input.GetAxisRaw("Horizontal");
        bool isBraking = Input.GetKey(KeyCode.Space);

        // 2. Aplicar Dirección (Eje delantero)
        float currentSteer = steerInput * maxSteeringAngle;
        if (frontLeft != null) frontLeft.steerAngle = currentSteer;
        if (frontRight != null) frontRight.steerAngle = currentSteer;

        // 3. Aplicar Tracción y Freno (Eje trasero)
        float currentMotor = motorInput * maxMotorTorque;
        
        if (rearLeft != null && rearRight != null)
        {
            if (isBraking)
            {
                rearLeft.motorTorque = 0f;
                rearRight.motorTorque = 0f;
                rearLeft.brakeTorque = brakeTorque;
                rearRight.brakeTorque = brakeTorque;
            }
            else
            {
                rearLeft.brakeTorque = 0f;
                rearRight.brakeTorque = 0f;
                rearLeft.motorTorque = currentMotor;
                rearRight.motorTorque = currentMotor;
            }
        }
    }
}