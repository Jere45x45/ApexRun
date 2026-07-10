using UnityEngine;

public class KartController : MonoBehaviour
{
    public float acceleration = 5000f;
    public float maxSpeed = 20f;

    public float steeringForce = 1000f;

    private Rigidbody rb;

    private float move;
    private float turn;

    private float speed;


    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }


    void Update()
    {
        move = Input.GetAxis("Vertical");
        turn = Input.GetAxis("Horizontal");
    }

    void FixedUpdate()
    {
        speed = rb.velocity.magnitude;

        rb.AddForce(transform.forward * move * acceleration);

        rb.AddTorque(Vector3.up * turn * steeringForce * speed * Time.fixedDeltaTime);

        if (rb.velocity.magnitude > maxSpeed)
        {
            rb.velocity = rb.velocity.normalized * maxSpeed;
        }
    }
}