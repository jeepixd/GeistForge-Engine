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
            if (randomDraw < 0.4f)
            {
                aggression = 0.0f;
            }
            else if (randomDraw < 0.7f)
            {
                aggression = 1.0f;
            }
            else
            {
                aggression = Random.Range(0f, 1f);
            }

        }
        
    }
    public override void CollectObservations(VectorSensor sensor)
    {
        if (target == null) return;
        Vector3 relativePosition = transform.InverseTransformPoint(target.position);
        Vector3 normalizedPosition = Vector3.ClampMagnitude(relativePosition / maxSensorRange, 1f);
        sensor.AddObservation(normalizedPosition);//3 floats x y z 

        Vector3 toTarget = target.position - transform.position;
        float normalizedDistance = Mathf.Clamp01(toTarget.magnitude / maxSensorRange);
        sensor.AddObservation(normalizedDistance);

        Vector3 unitDirection = toTarget.sqrMagnitude > 0.000001f
    ? transform.InverseTransformDirection(toTarget.normalized)
    : Vector3.zero;

        sensor.AddObservation(unitDirection); // 3 floats: local X, Y, Z

        Vector3 localVelocity = Vector3.ClampMagnitude(transform.InverseTransformDirection(_rb.linearVelocity) / maxSpeed, 1f);
        Vector3 targetVel = targetRb != null ? targetRb.linearVelocity : Vector3.zero;
        Vector3 targetlocalVel = Vector3.zero;
        if(targetVel != null)
        {
             targetlocalVel = Vector3.ClampMagnitude((transform.InverseTransformDirection(targetVel - _rb.linearVelocity)) / maxSpeed, 1f);

        }
        
        sensor.AddObservation(localVelocity);//3 floats
        sensor.AddObservation(targetlocalVel);//3 floats

        //spartial awareness and aggression
        float maxYawRateRad = maxYawRateDeg * Mathf.Deg2Rad;
        float normalizedYawRate = Mathf.Clamp(_rb.angularVelocity.y / maxYawRateRad, -1f, 1f);
        sensor.AddObservation(normalizedYawRate);//1 float 

        float groundDist = 10f;
        if (Physics.Raycast(
            transform.position,
            Vector3.down,
            out RaycastHit hit,
            10f,
            obstacleMask))
        {
            groundDist = hit.distance;
        }

        float normalizedGroundDist = Mathf.Clamp((groundDist - 2f) / 5f, -1f, 1f);
        sensor.AddObservation(normalizedGroundDist);
        sensor.AddObservation(Mathf.Clamp01(aggression));

        
    }
}
