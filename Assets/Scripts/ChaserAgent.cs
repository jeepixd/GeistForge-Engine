using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class ChaserAgent : Agent
{
    [SerializeField] private Transform target;
    [SerializeField] private float moveSpeed = 10f;
    private Rigidbody rb;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
    }
     
    //called everytime agent wins or falls off the platform
    public override void OnEpisodeBegin()
    {
        if(transform.localPosition.y < 0)//if cube falls off the platform
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            transform.localPosition = new Vector3(0, 0.5f, 0);

        }

        //teleport target sphere to random position on floor 
        target.localPosition = new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));



    }
    
    //eyes inputs we send to pytorch
    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(target.localPosition);
        sensor.AddObservation(transform.localPosition);
        sensor.AddObservation(rb.linearVelocity.x);
        sensor.AddObservation(rb.linearVelocity.z);


    }
    //brain and muscles: receive numbers from pytorch and move 
    public override void OnActionReceived(ActionBuffers actions)
    {
        float moveX = actions.ContinuousActions[0];
        float moveZ = actions.ContinuousActions[1];

        rb.AddForce(new Vector3(moveX, 0f, moveZ) * moveSpeed);

        //condition a did we touch the target
        float distanceToTarget = Vector3.Distance(transform.localPosition, target.localPosition);
        if(distanceToTarget < 1.4f)
        {
            SetReward(1.0f);
            EndEpisode();
        }


        if(transform.localPosition.y < 0)
        {
            EndEpisode();
        }
    }

 }
