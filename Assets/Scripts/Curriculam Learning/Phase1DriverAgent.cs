using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;


public class Phase1DriverAgent : Agent
{
    [SerializeField] private float Aggression = 1f;

    [SerializeField] private Transform target;
    [SerializeField] private float moveForce = 20f;
    [SerializeField] private float turnSpeed = 200f;

    private Rigidbody rb;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();

    }
    public override void OnEpisodeBegin()
    {

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.localPosition = new Vector3(0f, 1f, -5f);
        transform.localRotation = Quaternion.identity;
    }
    
}
