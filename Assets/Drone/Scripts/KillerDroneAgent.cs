using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.VectorGraphics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering.Universal.Internal;

[RequireComponent(typeof(Rigidbody))]
public class KillerDroneAgent : Agent
{
    
    [Range(0.0f, 1.0f)] public float aggression = 0.0f;
    public Transform target;
    public Rigidbody targetRb;
    public LayerMask obstacleMask;

    public TrainingArenaEnvironment arena;

    //flight kinematics tunning 
    public float maxSpeed = 12f;
    public float surgeAccel = 45f;
    public float swayAccel = 30f;
    public float heaveAccel = 25f;
    public float maxYawRateDeg = 380f;
    public float maxSensorRange = 20f;

    //cosmetics banking 
    public Transform visualModel;
    public float maxBankAngle = 22f;
    public float randomDraw;

    [Header("Visual Telemetry (URP Emission glow")]
    public MeshRenderer droneMeshRenderer;
    [ColorUsage(true, true)] public Color stalkEyeColor = new Color(0f, 2.5f, 4f); //cyan
    [ColorUsage(true, true)] public Color strikeEyeColor = new Color(5.0f, 0.2f, 0.1f); //crimsome color
    private MaterialPropertyBlock _propBlock;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");



    private Rigidbody _rb;

    public override void Initialize()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.mass = 1.5f;
        _rb.linearDamping = 5.0f;
        _rb.angularDamping = 8.0f;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;    
    }

    public override void OnEpisodeBegin()
    {
        if (arena != null) arena.ResetArena();

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

      /* float groundDist = 10f;
        if (Physics.Raycast(
            transform.position,
            Vector3.down,
            out RaycastHit hit,
            10f,
            obstacleMask))
        {
            groundDist = hit.distance;
        }*/
        // float normalizedGroundDist = Mathf.Clamp((groundDist - 2f) / 5f, -1f, 1f);
        //sensor.AddObservation(normalizedGroundDist);
        // Observation 14: Short-range warning (< 1.5m). Above 1.5m it outputs 0.0!
        float groundDist = Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 10f, obstacleMask)
            ? hit.distance : 10f;
        sensor.AddObservation(Mathf.Clamp01((1.5f - groundDist) / 1.5f));
        sensor.AddObservation(Mathf.Clamp01(aggression));

        
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        var cont = actions.ContinuousActions;
        float surge = Mathf.Clamp(cont[0], -1f, 1f);
        float sway = Mathf.Clamp(cont[1], -1f, 1f);
        float heave = Mathf.Clamp(cont[2], -1f, 1f);
        float yawCmd = Mathf.Clamp(cont[3], -1f, 1f);
        //the omnidirectional thruster logic
        float mult = 1f;
        if(surge < 0f)
        {
            mult = 0.85f;
        }
        Vector3 totalForce = (transform.forward * surge * surgeAccel * mult) + (transform.right * sway * swayAccel) + (transform.up * heave * heaveAccel);

        // =========================================================================
        // CONTROL BARRIER ACTUATOR SHIELD (Deterministic Safe-RL Air Cushion)
        // =========================================================================
        // 1. Tangential Obstacle Deflection Shield (Anti-Corner Clipping at high speed)
        if (_rb.linearVelocity.sqrMagnitude > 1.0f)
        {
            if (Physics.SphereCast(transform.position, 0.45f, _rb.linearVelocity.normalized, out RaycastHit obsHit, 1.35f, obstacleMask))
            {
                Vector3 hitNormal = obsHit.normal;
                hitNormal.y = 0f;
                if (hitNormal.sqrMagnitude > 0.001f) hitNormal.Normalize();
                Vector3 deflection = Vector3.ProjectOnPlane(_rb.linearVelocity, hitNormal).normalized;
                float urgency = Mathf.Clamp01((1.35f - obsHit.distance) / 1.35f);
                totalForce += (deflection * 15f + hitNormal * 25f) * urgency;
            }
        }

        // 2. Matador Evasion Shield (Close-Range Bull-Rush Evasion at alpha < 0.5)
        if (aggression < 0.5f && target != null)
        {
            Vector3 toTarget = target.position - transform.position;
            float dXZ = new Vector2(toTarget.x, toTarget.z).magnitude;
            Vector3 targetVel = targetRb != null ? targetRb.linearVelocity : Vector3.zero;
            float closingSpeed = Vector3.Dot(_rb.linearVelocity - targetVel, toTarget.normalized);

            if (dXZ < 2.0f && closingSpeed > 1.5f)
            {
                Vector3 lateralEvade = Vector3.Cross(Vector3.up, toTarget.normalized).normalized;
                if (Vector3.Dot(_rb.linearVelocity, lateralEvade) < 0f) lateralEvade = -lateralEvade;
                Vector3 matadorImpulse = (lateralEvade * 35f) + (Vector3.up * 20f);
                float breachUrgency = (2.0f - dXZ) / 2.0f;
                totalForce += matadorImpulse * breachUrgency;
            }
        }
        // =========================================================================

        _rb.AddForce(totalForce, ForceMode.Acceleration);
        _rb.angularVelocity = new Vector3(0f, yawCmd * maxYawRateDeg * Mathf.Deg2Rad, 0f);
        EvaluateCombatReward();
        
    }
    public void EvaluateCombatReward()
    {
        if (target == null)
            return;

        //distance and direction 
        Vector3 toTarget = target.position - transform.position;
        float dist3D = toTarget.magnitude;
        Vector3 dirToTarget = toTarget / Mathf.Max(dist3D, 0.001f);

        //decompose into planar distance and elevation
        float dXZ = new Vector2(toTarget.x, toTarget.z).magnitude;
        float deltaY = transform.position.y - target.position.y;

        // Multiplicative facing gate (planar XZ horizontal yaw alignment)
        Vector3 dirToTargetXZ = new Vector3(toTarget.x, 0f, toTarget.z).normalized;
        float rawDotXZ = Vector3.Dot(transform.forward, dirToTargetXZ);
        float facingGate = rawDotXZ > 0f ? (rawDotXZ * rawDotXZ) : 0f;

        Vector3 targetVel = targetRb != null ? targetRb.linearVelocity : Vector3.zero;
        Vector3 relVel = _rb.linearVelocity - targetVel;
        float closingSpeed = Vector3.Dot(_rb.linearVelocity - targetVel, dirToTarget);
        float retreatSpeed = - Vector3.Dot(_rb.linearVelocity, dirToTarget);


        //stalk mode 
        
            float perchExponent = -((dXZ - 3.0f) * (dXZ - 3.0f)) / (2f * 1.44f)
                          - ((deltaY - 2.0f) * (deltaY - 2.0f)) / (2f * 1.0f);
            float rPerch = facingGate * Mathf.Exp(perchExponent);

            float rApproach = dXZ > 5.0f ? (0.2f * facingGate * Mathf.Clamp(closingSpeed / maxSpeed, -1f, 1f)) : 0f;
            float rBreach = 0f;
            if (dXZ < 2.0f)
            {
                float intrusionRatio = (2.0f - dXZ) / 2.0f;
                float penalty = -0.5f * (intrusionRatio * intrusionRatio); // Quadratic repulsion
                float backpedalBonus = 0.6f * facingGate * Mathf.Clamp(retreatSpeed / maxSpeed, -1f, 1f);
                rBreach = penalty + backpedalBonus;
            }

            //tactical circle strafe bonus 
            float strafeSpeed = Mathf.Abs(Vector3.Dot(_rb.linearVelocity, transform.right));
            float rStrafe = (dXZ >= 2.2f && dXZ <= 3.8f) ? (0.15f * facingGate * Mathf.Clamp01(strafeSpeed / maxSpeed)) : 0f;

            float rStalk = rPerch + rApproach + rBreach + rStrafe;


        // 2. strike mode 
        // 3D closing speed + vertical dive speed 
        float vertStrikeVel = Mathf.Sign(toTarget.y) * relVel.y;
        float rClose = 0.8f * Mathf.Clamp(closingSpeed / maxSpeed, -1f, 1f) + 0.4f * Mathf.Clamp(vertStrikeVel / maxSpeed, -1f, 1f);
        //terminal magnet inside 2.5m
        float rMagnet = dist3D < 2.5f ? 0.6f * Mathf.Exp(-Mathf.Pow(Mathf.Max(0f, dist3D - 1.10f), 2f) / (2f * 0.45f * 0.45f)) : 0f;

        float rUrgency = -0.35f;
        float rStrike = facingGate * (rClose + rMagnet) + rUrgency;

           /*       //strike mode 
            // Pure closing speed in strike mode (no passive loitering reward)
            float rStrike = facingGate * Mathf.Clamp(closingSpeed / maxSpeed, -1f, 1f);
            
        */
        float stepReward = 0.01f * ((1f - aggression) * rStalk + aggression * rStrike);
        AddReward(stepReward);

        if(dist3D < 1.15f)
        {
            if(aggression >= 0.5f)
            {
                AddReward(3.5f * aggression * facingGate);
            }
            else
            {
                AddReward(-1.0f);
            }
            EndEpisode();
        }

    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Obstacle"))
        {

            //floor mercy rule : if diving in strike mode and scraping horizontal floor , minor penalty , dont end episdoe !
            bool isHorizontalFloor = collision.contactCount > 0 && collision.GetContact(0).normal.y > 0.7f;
            if(aggression >= 0.5f && isHorizontalFloor)
            {
                AddReward(-0.02f);
                return;
            }

            AddReward(-1.0f);
            EndEpisode();
        }
    }
    private void LateUpdate()
    {
        // 1. Dynamic Eye Emission Color
        if (droneMeshRenderer != null)
        {
            _propBlock ??= new MaterialPropertyBlock();
            Color currentEyeGlow = Color.Lerp(stalkEyeColor, strikeEyeColor, aggression);
            // Slot 2 is Mat_Eye_Emissive
            droneMeshRenderer.GetPropertyBlock(_propBlock, 2);
            _propBlock.SetColor(EmissionColorId, currentEyeGlow);
            droneMeshRenderer.SetPropertyBlock(_propBlock, 2);
        }
        if (visualModel == null || _rb == null) return;
        // 2. Cosmetic Predator Banking
        Vector3 localVel = transform.InverseTransformDirection(_rb.linearVelocity);
        float targetPitch = Mathf.Clamp(localVel.z / maxSpeed, -1f, 1f) * maxBankAngle;
        float targetRoll = -Mathf.Clamp(localVel.x / maxSpeed, -1f, 1f) * maxBankAngle;
        Quaternion targetRot = Quaternion.Euler(targetPitch, 0f, targetRoll);
        visualModel.localRotation = Quaternion.Slerp(visualModel.localRotation, targetRot, Time.deltaTime * 10f);
    }
}
 