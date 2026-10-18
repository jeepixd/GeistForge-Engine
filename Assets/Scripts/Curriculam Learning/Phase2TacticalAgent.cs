using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class Phase2TacticalAgent : Agent
{
    [SerializeField] private float aggression = 1f;
    [SerializeField] private Transform target;
    [SerializeField] private float moveForce = 20f;
    [SerializeField] private float turnSpeed = 200f;
    private int episodeStepCount = 0;

    private Rigidbody rb;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();

    }
    public override void OnEpisodeBegin()
    {
        episodeStepCount = 0;
       
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.localPosition = new Vector3(0f, 1f, -5f);
        transform.localRotation = Quaternion.identity;

        float targetX = Random.Range(-4f, 4f);
        float targetZ = Random.Range(2.5f, 5f);
        target.localPosition = new Vector3(targetX, 0.5f, targetZ);
        // 30% Strike (maintenance), 70% Stalk (heavy learning)
        aggression = (Random.value < 0.3f) ? 1.0f : 0.0f;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 toTarget = transform.InverseTransformPoint(target.position);
        sensor.AddObservation(toTarget.x);
        sensor.AddObservation(toTarget.y);
        sensor.AddObservation(toTarget.z);

        //velocity relative to my body
        Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
        sensor.AddObservation(localVelocity.x);
        sensor.AddObservation(localVelocity.z);

        sensor.AddObservation(aggression);

    }
    public override void OnActionReceived(ActionBuffers actions)
    {
        episodeStepCount++;
        float forwardInput = actions.ContinuousActions[0];
        float turnInput = actions.ContinuousActions[1];

        Vector3 dirToTarget = (target.position - transform.position).normalized;
        float facingDot = Vector3.Dot(transform.forward, dirToTarget);

        if (aggression < 0.5f && episodeStepCount >= 150)
        {
            aggression = 1.0f;
        }
        float turn = turnInput * turnSpeed * Time.fixedDeltaTime;
        transform.Rotate(0f, turn, 0f);

        //float forwardThrust = Mathf.Clamp01(forwardInput);
        float thrust = forwardInput >= 0f ? forwardInput : forwardInput * 05f;
        rb.AddForce(transform.forward * thrust * moveForce);
        AddReward(-0.0005f);
        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        if (aggression >= 0.5f)
        {
            if(distanceToTarget < 1.4f)
            {
                SetReward(1f);
                EndEpisode();
            }
        }
        else
        {
            if(distanceToTarget >= 2f && distanceToTarget <= 3f && facingDot > 0.5f)
            {
                AddReward(0.005f);

            }
            if(distanceToTarget < 1.5f)
            {
                AddReward(-0.005f);
            }
        }
        if(transform.localPosition.y < -0.5f)
        {
            SetReward(-1f);
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
