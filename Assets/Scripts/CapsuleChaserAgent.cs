using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.InferenceEngine.Tokenization.Normalizers;

public class CapsuleChaserAgent : Agent
{
    [Header("Combat Director")]
    [Range(0f, 1f)]
    [SerializeField] private float aggression = 1f;
    [SerializeField] private bool randomizeInTraining = true;

    private int episodeStepCount = 0;

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
        episodeStepCount = 0;

        if (randomizeInTraining)
        {
            aggression = Random.value > 0.5f ? 1.0f : 0.0f;

        }
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

        //ego centric 
        //where is the target relative to my body and nose
        Vector3 toTarget = transform.InverseTransformPoint(target.position);
        sensor.AddObservation(toTarget.x);
        sensor.AddObservation(toTarget.y);
        sensor.AddObservation(toTarget.z);

        //where is my velocity relative to my body?
        Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
        sensor.AddObservation(localVelocity.x);
        sensor.AddObservation(localVelocity.z);

        //0f =space 1f = strike
        sensor.AddObservation(aggression);



        /*sensor.AddObservation(target.localPosition);
        sensor.AddObservation(transform.localPosition);
        sensor.AddObservation(rb.linearVelocity.x);
        sensor.AddObservation(rb.linearVelocity.z);
        var targetVel = (target.localPosition - previousTargetPos) / Time.fixedDeltaTime;
        sensor.AddObservation(targetVel.x);
        sensor.AddObservation(targetVel.z);
        
        */
        //update for next step
        previousTargetPos = target.localPosition;



    }
    //brain and muscles: receive numbers from pytorch and move 
    public override void OnActionReceived(ActionBuffers actions)
    {
        episodeStepCount++;

        // 1. FIX: Only allow moving FORWARD (0 to 1). No driving backwards blindly!
        float forwardInput = Mathf.Clamp01(actions.ContinuousActions[0]);
        float turnInput = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);

        // Rotate yaw
        float turn = turnInput * turnSpeed * Time.fixedDeltaTime;
        transform.Rotate(0f, turn, 0f);

        // Push forward
        rb.AddForce(transform.forward * forwardInput * moveForce);

        // Tiny step penalty so it doesn't idle
        AddReward(-0.0005f);

        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        // --- MODE A: AGGRESSIVE (Catch the target) ---
        if (aggression > 0.5f)
        {
            if (distanceToTarget < 1.4f)
            {
                SetReward(1.0f); // WIN!
                EndEpisode();
            }
        }
        // --- MODE B: DEFENSIVE (Keep distance between 2.0m and 3.5m) ---
        else
        {
            // Give reward for keeping good combat distance
            if (distanceToTarget >= 2.0f && distanceToTarget <= 3.5f)
            {
                AddReward(0.01f); // Good spacing!
            }
            else if (distanceToTarget < 1.8f)
            {
                AddReward(-0.01f); // Too close! Danger!
            }

            // WIN CONDITION FOR DEFENSIVE: Survived and spaced well for 300 steps!
            if (episodeStepCount >= 300)
            {
                SetReward(1.0f); // WIN! You successfully survived and kept distance!
                EndEpisode();
            }
        }

        // --- CLIFF CHECK ---
        if (transform.localPosition.y < -0.5f)
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
