using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.VectorGraphics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering.Universal.Internal;

[RequireComponent(typeof(Rigidbody))]
public class GCRLDroneAgent : Agent
{
    [Range(0.0f, 1.0f)] public float aggression = 0.0f;
    public Transform target;
    public Rigidbody targetRb;
    public LayerMask obstacleMask;

    public GCRLArenaEnvironment arena;

    // flight kinematics tuning (Stable Combat Kinematics)
    public float maxSpeed = 13.0f;
    public float surgeAccel = 45.0f;
    public float swayAccel = 30.0f;
    public float heaveAccel = 25.0f;
    public float maxYawRateDeg = 380.0f;
    public float maxSensorRange = 25.0f;

    // GCRL Goal Controls (Exposed for runtime testing)
    [Header("GCRL Interactive Testing")]
    [Tooltip("Check this in Play Mode to control distance, height, and aggression manually via Inspector sliders without the Arena overriding them.")]
    public bool manualDebugControl = false;
    [Range(1.5f, 12.0f)] public float goalDist = 3.0f;
    [Range(0.5f, 6.0f)] public float goalHeight = 2.0f;

    public float minGoalDist = 2.0f;
    public float minGoalHeight = 0.5f;
    public float maxGoalDist = 12.0f;
    public float maxGoalHeight = 6.0f;

    //cached geometric state 
    private float _cachedDXZ;
    private float _cachedDeltaY;
    private float _cachedGroundDist = 10f;


    // cosmetics banking
    public Transform visualModel;
    public float maxBankAngle = 22f;
    public float randomDraw;

    [Header("Visual Telemetry (URP Emission glow)")]
    public MeshRenderer droneMeshRenderer;
    [ColorUsage(true, true)] public Color stalkEyeColor = new Color(0f, 2.5f, 4f); // cyan
    [ColorUsage(true, true)] public Color strikeEyeColor = new Color(5.0f, 0.2f, 0.1f); // crimson color
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

        if (!manualDebugControl && Academy.Instance.IsCommunicatorOn)
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
        sensor.AddObservation(normalizedPosition); // 3 floats x y z

        Vector3 toTarget = target.position - transform.position;
        _cachedDXZ = new Vector2(toTarget.x, toTarget.z).magnitude;
        _cachedDeltaY = transform.position.y - target.position.y;
        float normalizedDistance = Mathf.Clamp01(toTarget.magnitude / maxSensorRange);
        sensor.AddObservation(normalizedDistance); // 1 float

        Vector3 unitDirection = toTarget.sqrMagnitude > 0.000001f
            ? transform.InverseTransformDirection(toTarget.normalized)
            : Vector3.zero;

        sensor.AddObservation(unitDirection); // 3 floats: local X, Y, Z

        Vector3 localVelocity = Vector3.ClampMagnitude(transform.InverseTransformDirection(_rb.linearVelocity) / maxSpeed, 1f);
        Vector3 targetVel = targetRb != null ? targetRb.linearVelocity : Vector3.zero;
        Vector3 targetlocalVel = Vector3.zero;
        if (targetVel != null)
        {
            targetlocalVel = Vector3.ClampMagnitude((transform.InverseTransformDirection(targetVel - _rb.linearVelocity)) / maxSpeed, 1f);
        }
        
        sensor.AddObservation(localVelocity); // 3 floats
        sensor.AddObservation(targetlocalVel); // 3 floats

        // spatial awareness and aggression
        float maxYawRateRad = maxYawRateDeg * Mathf.Deg2Rad;
        float normalizedYawRate = Mathf.Clamp(_rb.angularVelocity.y / maxYawRateRad, -1f, 1f);
        sensor.AddObservation(normalizedYawRate); // 1 float

        _cachedGroundDist = Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 10f, obstacleMask) ? hit.distance : 10f;
        sensor.AddObservation(Mathf.Clamp01((1.5f - _cachedGroundDist) / 1.5f)); //1 float 
        sensor.AddObservation(Mathf.Clamp01(aggression)); // 1 float

        //gcrl observation 
        //normalized vdist in[0,1]
        sensor.AddObservation(Mathf.InverseLerp(minGoalDist, maxGoalDist, goalDist));
        //normalized vheight in [0,1]
        sensor.AddObservation(Mathf.InverseLerp(minGoalHeight, maxGoalHeight, goalHeight));
        //signed normalized planar dist error
        float distError = (_cachedDXZ - goalDist) / maxGoalDist;
        sensor.AddObservation(Mathf.Clamp(distError, -1f, 1f));
        //signed normalized elevation error 
        float heightError = (_cachedDeltaY - goalHeight) / maxGoalHeight;
        sensor.AddObservation(Mathf.Clamp(heightError, -1f, 1f));
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        var cont = actions.ContinuousActions;
        float surge = Mathf.Clamp(cont[0], -1f, 1f);
        float sway = Mathf.Clamp(cont[1], -1f, 1f);
        float heave = Mathf.Clamp(cont[2], -1f, 1f);
        float yawCmd = Mathf.Clamp(cont[3], -1f, 1f);

        // omnidirectional thruster logic
        float mult = 1f;
        if (surge < 0f)
        {
            mult = 0.85f;
        }
        Vector3 totalForce = (transform.forward * surge * surgeAccel * mult) + (transform.right * sway * swayAccel) + (transform.up * heave * heaveAccel);

        _rb.AddForce(totalForce, ForceMode.Acceleration);
        _rb.angularVelocity = new Vector3(0f, yawCmd * maxYawRateDeg * Mathf.Deg2Rad, 0f);
        EvaluateCombatReward();
    }

    public void EvaluateCombatReward()
    {
        if (target == null)
            return;

        // distance and direction
        Vector3 toTarget = target.position - transform.position;
        float dist3D = toTarget.magnitude;
        Vector3 dirToTarget = toTarget / Mathf.Max(dist3D, 0.001f);

        // decompose into planar distance and elevation
        float dXZ = new Vector2(toTarget.x, toTarget.z).magnitude;
        float deltaY = transform.position.y - target.position.y;

        // Multiplicative facing gate (planar XZ horizontal yaw alignment)
        Vector3 dirToTargetXZ = new Vector3(toTarget.x, 0f, toTarget.z).normalized;
        float rawDotXZ = Vector3.Dot(transform.forward, dirToTargetXZ);
        float facingGate = rawDotXZ > 0f ? (rawDotXZ * rawDotXZ) : 0f;

        Vector3 targetVel = targetRb != null ? targetRb.linearVelocity : Vector3.zero;
        Vector3 relVel = _rb.linearVelocity - targetVel;
        float closingSpeed = Vector3.Dot(_rb.linearVelocity - targetVel, dirToTarget);
        float retreatSpeed = -Vector3.Dot(_rb.linearVelocity, dirToTarget);

        // --- STALK MODE (GOAL-CONDITIONED) ---
        // 1. Adaptive Sigmas: Tighter precision at 2m, slightly wider margin at 12m
        float normDist = Mathf.InverseLerp(minGoalDist, maxGoalDist, goalDist);
        float sigmaD = Mathf.Lerp(0.8f, 1.8f, normDist);
        float normHeight = Mathf.InverseLerp(minGoalHeight, maxGoalHeight, goalHeight);
        float sigmaH = Mathf.Lerp(0.7f, 1.4f, normHeight);

        // Dynamic 3D Gaussian Perch
        float perchExponent = -((dXZ - goalDist) * (dXZ - goalDist)) / (2f * sigmaD * sigmaD)
                              - ((deltaY - goalHeight) * (deltaY - goalHeight)) / (2f * sigmaH * sigmaH);
        float rPerch = facingGate * Mathf.Exp(perchExponent);

        // 2. Dynamic Approach: Triggers when further than 1.4x beyond the goal ring
        float approachThreshold = goalDist * 1.4f;
        float rApproach = dXZ > approachThreshold ? (0.2f * facingGate * Mathf.Clamp(closingSpeed / maxSpeed, -1f, 1f)) : 0f;

        // 3. Dynamic Breach Buffer: Proportional to goalDist (65% of goalDist, clamped [0.8m, 2.5m])
        float breachRadius = Mathf.Clamp(goalDist * 0.65f, 0.8f, 2.5f);
        float rBreach = 0f;
        if (dXZ < breachRadius)
        {
            float intrusionRatio = (breachRadius - dXZ) / breachRadius;
            float penalty = -0.5f * (intrusionRatio * intrusionRatio); // Quadratic repulsion
            float backpedalBonus = 0.6f * facingGate * Mathf.Clamp(retreatSpeed / maxSpeed, -1f, 1f);
            rBreach = penalty + backpedalBonus;
        }

        // 4. Dynamic Circle Strafe Band: Centered on goalDist (+- 0.7m)
        float strafeSpeed = Mathf.Abs(Vector3.Dot(_rb.linearVelocity, transform.right));
        float strafeMin = Mathf.Max(0.5f, goalDist - 0.7f);
        float strafeMax = goalDist + 0.7f;
        float rStrafe = (dXZ >= strafeMin && dXZ <= strafeMax) ? (0.15f * facingGate * Mathf.Clamp01(strafeSpeed / maxSpeed)) : 0f;

        float rStalk = rPerch + rApproach + rBreach + rStrafe;

        // 2. strike mode
        // 3D closing speed + vertical dive speed
        float vertStrikeVel = Mathf.Sign(toTarget.y) * relVel.y;
        float rClose = 0.8f * Mathf.Clamp(closingSpeed / maxSpeed, -1f, 1f) + 0.4f * Mathf.Clamp(vertStrikeVel / maxSpeed, -1f, 1f);
        
        // terminal magnet inside 2.5m
        float rMagnet = dist3D < 2.5f ? 0.6f * Mathf.Exp(-Mathf.Pow(Mathf.Max(0f, dist3D - 1.10f), 2f) / (2f * 0.45f * 0.45f)) : 0f;

        float rUrgency = -0.35f;
        float rStrike = facingGate * (rClose + rMagnet) + rUrgency;
        // 5. Continuous Proximity Clearance (Ground Warning)
        float rProximity = (_cachedGroundDist < 1.0f) ? (-0.2f * (1.0f - _cachedGroundDist)) : 0f;

        // 6. Action Smoothness Regularization (Penalize excessive erratic yaw jerk)
        float yawJerk = Mathf.Abs(_rb.angularVelocity.y);
        float rSmooth = -0.005f * Mathf.Clamp01(yawJerk / (maxYawRateDeg * Mathf.Deg2Rad));

        float stepReward = 0.01f * ((1f - aggression) * (rStalk + rProximity + rSmooth) + aggression * (rStrike + rProximity));
        AddReward(stepReward);

        if (dist3D < 1.15f)
        {
            if (aggression >= 0.5f)
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
            // floor mercy rule: if diving in strike mode and scraping horizontal floor, minor penalty, don't end episode!
            bool isHorizontalFloor = collision.contactCount > 0 && collision.GetContact(0).normal.y > 0.7f;
            if (aggression >= 0.5f && isHorizontalFloor)
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
