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

        float targetX = Random.Range(-4f, 4f);
        float targetZ = Random.Range(2.5f, 5f);
        target.localPosition = new Vector3(targetX, 0.5f, targetZ);


    }

    public override void CollectObservations(VectorSensor sensor)
    {
        //ego centric
        Vector3 toTarget = transform.InverseTransformPoint(target.position);
        sensor.AddObservation(toTarget.x);
        sensor.AddObservation(toTarget.y);
        sensor.AddObservation(toTarget.z);

        //where is my velocity relative to my body
        Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);

        sensor.AddObservation(localVelocity.x);
        sensor.AddObservation(localVelocity.z);

        sensor.AddObservation(Aggression);



    }

    
}
