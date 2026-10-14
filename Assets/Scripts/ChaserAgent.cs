using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.InferenceEngine.Tokenization.Normalizers;

public class ChaserAgent : Agent
{
    [SerializeField] private Transform target;
    [SerializeField] private float moveSpeed = 10f;
    private Rigidbody rb;
    private Vector3 previousTargetPos;
   

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        previousTargetPos = target.localPosition;
    }
     
    //called everytime agent wins or falls off the platform
    public override void OnEpisodeBegin()
    {
        
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            transform.localPosition = new Vector3(0, 0.5f, 0);
            

        

        //teleport target sphere to random position on floor 
        target.localPosition = new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
        previousTargetPos = target.localPosition;


    }
    
    //eyes inputs we send to pytorch
    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(target.localPosition);
        sensor.AddObservation(transform.localPosition);
        sensor.AddObservation(rb.linearVelocity.x);
        sensor.AddObservation(rb.linearVelocity.z);
        var targetVel = (target.localPosition - previousTargetPos)/Time.fixedDeltaTime;
        sensor.AddObservation(targetVel.x);
        sensor.AddObservation(targetVel.z);

        //update for next step
        previousTargetPos = target.localPosition;



    }
    //brain and muscles: receive numbers from pytorch and move 
    public override void OnActionReceived(ActionBuffers actions)
    {
        float moveX = actions.ContinuousActions[0];
        float moveZ = actions.ContinuousActions[1];

        // Replace rb.AddForce(...) with this:
        //rb.AddForce(new Vector3(moveX, 0f, moveZ) * moveSpeed);
        rb.linearVelocity = new Vector3(moveX * moveSpeed, rb.linearVelocity.y, moveZ * moveSpeed);
        AddReward(-0.001f);
    
    
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
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Obstacle"))
        {
            AddReward(-0.3f);
        }
    }

}
