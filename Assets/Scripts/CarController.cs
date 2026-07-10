using UnityEngine;

public class CarController : MonoBehaviour
{
    public float acceleration = 5000f;
    public float maxSpeed = 20f;
    public float turnSpeed = 100f;

    private Rigidbody rb;

    private float move;
    private float turn;


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
        rb.AddForce(transform.forward * move * acceleration);

        if (rb.velocity.magnitude > 0.5f)
        {
            rb.MoveRotation(rb.rotation * Quaternion.Euler(0, turn * turnSpeed * Time.fixedDeltaTime, 0));
        }

        if (rb.velocity.magnitude > maxSpeed)
        {
            rb.velocity = rb.velocity.normalized * maxSpeed;
        }
    }
}