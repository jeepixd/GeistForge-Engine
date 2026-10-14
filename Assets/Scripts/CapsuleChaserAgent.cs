using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.InferenceEngine.Tokenization.Normalizers;

public class CapsuleChaserAgent : Agent
{
    [SerializeField] private Transform target;
    [SerializeField] private float moveForce = 20f;
    [SerializeField] private float turnSpeed = 200f;

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
        transform.localPosition = new Vector3(0f, 1f, -5f);
        transform.localRotation = Quaternion.identity;





        //teleport target sphere to random position on floor 
        //target.localPosition = new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
        float targetX = Random.Range(-4f, 4f);
        float targetZ = Random.Range(2.5f, 5f);
        target.localPosition = new Vector3(targetX, 0.5f, targetZ);

        previousTargetPos = target.localPosition;


    }

    //eyes inputs we send to pytorch
    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(target.localPosition);
        sensor.AddObservation(transform.localPosition);
        sensor.AddObservation(rb.linearVelocity.x);
        sensor.AddObservation(rb.linearVelocity.z);
        var targetVel = (target.localPosition - previousTargetPos) / Time.fixedDeltaTime;
        sensor.AddObservation(targetVel.x);
        sensor.AddObservation(targetVel.z);

        //update for next step
        previousTargetPos = target.localPosition;



    }
    //brain and muscles: receive numbers from pytorch and move 
    public override void OnActionReceived(ActionBuffers actions)
    {
        float forwardInput = actions.ContinuousActions[0];
        float turnInput = actions.ContinuousActions[1];

        //rotate the capsule around y (yaws)
        float turn = turnInput * turnSpeed * Time.fixedDeltaTime;
        transform.Rotate(0f, turn, 0f);

        //push the capsule into the faced directions
        rb.AddForce(transform.forward * forwardInput * moveForce);

        AddReward(-0.001f);

        float distanceToTarget = Vector3.Distance(transform.position, target.position);
        if(distanceToTarget < 1.4f)
        {
            AddReward(1.0f);
            EndEpisode();
        }
        if(transform.localPosition.y < -0.5f)
        {
            SetReward(-1.0f);
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

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        continuousActions[0] = Input.GetAxisRaw("Vertical");
        continuousActions[1] = Input.GetAxisRaw("Horizontal");

    }

}
