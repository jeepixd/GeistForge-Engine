using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

[RequireComponent(typeof(Rigidbody))]
public class KillerDroneAgent : Agent
{
    
    [Range(0.0f, 1.0f)] public float aggression = 0.0f;
    public Transform target;
    public Rigidbody targetRb;
    public LayerMask obstacleMask;

    //flight kinematics tunning 
    public float maxSpeed = 12f;
    public float surgeAccel = 35f;
    public float swayAccel = 30f;
    public float heaveAccel = 25f;
    public float maxYawRateDeg = 240f;
    public float maxSensorRange = 20f;

    //cosmetics banking 
    public Transform visualModel;
    public float maxBankAngle = 22f;
    public float randomDraw;

    private Rigidbody _rb;

    public override void Initialize()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.mass = 1.5f;
        _rb.linearDamping = 4.0f;
        _rb.angularDamping = 8.0f;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;    
    }

    public override void OnEpisodeBegin()
    {
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;

        if (Academy.Instance.IsCommunicatorOn)
        {

            randomDraw = Random.value;

        }
        if(randomDraw < 0.4f)
        {
            aggression = 0.0f;
        }else if(randomDraw < 0.7f)
        {
            aggression = 1.0f;
        }
        else
        {
            aggression = Random.Range(0f, 1f);
        }
    }
}
