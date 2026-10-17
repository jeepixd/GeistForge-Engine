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
        // 30% Strike (maintenance), 70% Stalk (heavy learning)
        aggression = (Random.value < 0.3f) ? 1.0f : 0.0f;
    }
    
}
