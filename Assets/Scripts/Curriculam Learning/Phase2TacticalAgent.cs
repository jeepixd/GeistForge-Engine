using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class Phase2TacticalAgent : Agent
{
    [Header("GeistForge Live Slider")]
    [Range(0f, 1f)] [SerializeField] private float aggression = 1f;

    [Header("References & Settings")]
    [SerializeField] private Transform target;
    [SerializeField] private float moveForce = 20f;
    [SerializeField] private float turnSpeed = 200f;

    private int episodeStepCount = 0;
    private int warHornStep = 300;
    private Rigidbody rb;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
    }

    public override void OnEpisodeBegin()
    {
        episodeStepCount = 0;
        warHornStep = Random.Range(250, 350); // Randomize flip timing so it can't memorize a clock

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.localPosition = new Vector3(0f, 1f, -5f);
        transform.localRotation = Quaternion.identity;

        float targetX = Random.Range(-4f, 4f);
        float targetZ = Random.Range(2.5f, 5f);
        target.localPosition = new Vector3(targetX, 0.5f, targetZ);

        // Only override your slider when Python training is actually running!
        if (Academy.Instance.IsCommunicatorOn)
        {
            aggression = (Random.value < 0.3f) ? 1.0f : 0.0f;
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 toTarget = transform.InverseTransformPoint(target.position);
        sensor.AddObservation(toTarget.x);
        sensor.AddObservation(toTarget.y);
        sensor.AddObservation(toTarget.z);

        Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
        sensor.AddObservation(localVelocity.x);
        sensor.AddObservation(localVelocity.z);

        sensor.AddObservation(aggression);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        episodeStepCount++;
        float forwardInput = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float turnInput = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);

        Vector3 dirToTarget = (target.position - transform.position).normalized;
        float facingDot = Vector3.Dot(transform.forward, dirToTarget);

        // War Horn Flip (Only auto-flips during Python training)
        if (Academy.Instance.IsCommunicatorOn && aggression < 0.5f && episodeStepCount >= warHornStep)
        {
            aggression = 1.0f;
        }

        // Movement: 100% forward, 50% backpedal
        float turn = turnInput * turnSpeed * Time.fixedDeltaTime;
        transform.Rotate(0f, turn, 0f);

        float thrust = forwardInput >= 0f ? forwardInput : forwardInput * 0.5f;
        rb.AddForce(transform.forward * (thrust * moveForce));

        AddReward(-0.0005f);
        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        if (aggression >= 0.5f)
        {
            // STRIKE MODE
            if (distanceToTarget < 1.4f)
            {
                AddReward(1f);
                EndEpisode();
            }
        }
        else
        {
            // STALK / SPACING MODE (2.0m to 3.0m sweet spot while looking at target)
            if (distanceToTarget >= 2f && distanceToTarget <= 3f && facingDot > 0.5f)
            {
                AddReward(0.005f);
            }
            else if (distanceToTarget < 2.0f)
            {
                // Too close! Push it to use its new reverse gear
                AddReward(-0.005f);
            }
        }

        if (transform.localPosition.y < -0.5f)
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
