using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

/// <summary>
/// TacticalBrainTester: Real-time evaluation script for testing the trained ONNX model.
/// Allows dynamic control of the Aggression slider via Inspector or on-screen UI in Play Mode.
/// </summary>
public class TacticalBrainTester : Agent
{
    [Header("Combat Director Slider")]
    [Range(0f, 1f)]
    [Tooltip("0.0 = Stalk / Intimidate (~2.5m spacing) | 1.0 = Full Aggression Strike")]
    public float aggression = 0f;

    [Header("Scene References")]
    [SerializeField] private Transform target;
    [SerializeField] private float moveForce = 20f;
    [SerializeField] private float turnSpeed = 200f;

    [Header("Test Mode Settings")]
    [Tooltip("If true, resets the agent to spawn when it hits the target in Strike Mode.")]
    [SerializeField] private bool resetOnStrikeHit = true;
    [SerializeField] private bool showOnScreenUI = true;

    private Rigidbody rb;
    private Vector3 initialAgentPos;
    private Quaternion initialAgentRot;
    private float distanceToTarget;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        initialAgentPos = transform.localPosition;
        initialAgentRot = transform.localRotation;
    }

    public override void OnEpisodeBegin()
    {
        // Reset physics
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Reset to initial spawn position
        transform.localPosition = initialAgentPos;
        transform.localRotation = initialAgentRot;

        // Note: We DO NOT overwrite 'aggression' here!
        // The user maintains full manual control over the slider.
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 1-3: Ego-centric target position
        Vector3 toTarget = transform.InverseTransformPoint(target.position);
        sensor.AddObservation(toTarget.x);
        sensor.AddObservation(toTarget.y);
        sensor.AddObservation(toTarget.z);

        // 4-5: Ego-centric velocity
        Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
        sensor.AddObservation(localVelocity.x);
        sensor.AddObservation(localVelocity.z);

        // 6: The Aggression Slider (controlled live by user)
        sensor.AddObservation(aggression);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float forwardInput = actions.ContinuousActions[0];
        float turnInput = actions.ContinuousActions[1];

        // Apply yaw rotation
        float turn = turnInput * turnSpeed * Time.fixedDeltaTime;
        transform.Rotate(0f, turn, 0f);

        // Apply forward throttle (clamped to forward only)
        float forwardThrust = Mathf.Clamp01(forwardInput);
        rb.AddForce(transform.forward * forwardThrust * moveForce);

        distanceToTarget = Vector3.Distance(transform.position, target.position);

        // In Strike Mode, if it lands the attack
        if (aggression >= 0.5f && distanceToTarget < 1.4f)
        {
            if (resetOnStrikeHit)
            {
                EndEpisode();
            }
        }

        // Safety: Reset if it falls off the platform
        if (transform.localPosition.y < -0.5f)
        {
            EndEpisode();
        }
    }

    private void OnGUI()
    {
        if (!showOnScreenUI) return;

        // Draw an interactive control panel in the top-left of the Game View
        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.fontSize = 12;

        GUILayout.BeginArea(new Rect(20, 20, 320, 160), boxStyle);
        GUILayout.Label("<b><size=14>GeistForge Tactical AI Tester</size></b>");
        GUILayout.Space(5);

        GUILayout.Label($"Distance to Target: <b>{distanceToTarget:F2}m</b>");

        string stateText = aggression >= 0.5f
            ? "<color=#FF4444><b>STRIKE MODE (Attacking!)</b></color>"
            : "<color=#44DDFF><b>STALK MODE (Holding 2.5m)</b></color>";
        GUILayout.Label($"Tactical State: {stateText}");

        GUILayout.Space(5);
        GUILayout.Label($"Aggression Slider: <b>{aggression:F2}</b>");
        aggression = GUILayout.HorizontalSlider(aggression, 0f, 1f);

        GUILayout.Space(5);
        if (GUILayout.Button("Reset Episode"))
        {
            EndEpisode();
        }

        GUILayout.EndArea();
    }
}
