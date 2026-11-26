using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

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
    public float surgeAccel = 65.0f;
    public float swayAccel = 45.0f;
    public float heaveAccel = 35.0f;
    public float maxYawRateDeg = 380.0f;
    public float maxYawAccelDeg = 1800.0f;
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

    // cached geometric and spatial sensor state
    private float _cachedDXZ;
    private float _cachedDeltaY;
    private float _cachedGroundDist = 10f;
    private float _cachedCeilingDist = 10f;
    private float _cachedHasLOS = 1f;
    private float _lastSensorUpdateTime = -1f;

    // action history & current step action for smoothness regularization
    private readonly float[] _prevAction = new float[4];
    private float _currSurge;
    private float _currSway;
    private float _currHeave;
    private float _currYawCmd;
    private int _episodeId;

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
        _episodeId++;
        _lastSensorUpdateTime = -1f;
        _cachedGroundDist = 10f;
        _cachedCeilingDist = 10f;
        _cachedHasLOS = 1f;
        System.Array.Clear(_prevAction, 0, _prevAction.Length);
        _currSurge = 0f;
        _currSway = 0f;
        _currHeave = 0f;
        _currYawCmd = 0f;

        if (arena != null) arena.ResetArena();

        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;

        if (!manualDebugControl && Academy.Instance.IsCommunicatorOn)
        {
            randomDraw = Random.value;
            aggression = randomDraw < 0.55f ? 0.0f : 1.0f;
        }
    }

    /// <summary>
    /// Refreshes ground, overhead ceiling, and target line-of-sight sensors once per physics step.
    /// </summary>
    private void UpdateSpatialSensors()
    {
        if (Mathf.Approximately(_lastSensorUpdateTime, Time.fixedTime)) return;
        _lastSensorUpdateTime = Time.fixedTime;

        _cachedGroundDist = Physics.Raycast(transform.position, Vector3.down, out RaycastHit downHit, 10f, obstacleMask, QueryTriggerInteraction.Ignore)
            ? downHit.distance
            : 10f;
        _cachedCeilingDist = Physics.Raycast(transform.position, Vector3.up, out RaycastHit upHit, 10f, obstacleMask, QueryTriggerInteraction.Ignore)
            ? upHit.distance
            : 10f;
        _cachedHasLOS = (target != null && !Physics.Linecast(transform.position, target.position, obstacleMask, QueryTriggerInteraction.Ignore))
            ? 1f
            : 0f;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        UpdateSpatialSensors();

        if (target == null)
        {
            for (int i = 0; i < 26; i++)
            {
                sensor.AddObservation(0f);
            }
            return;
        }

        // [0..2] Target relative position in drone local space, clamped magnitude / maxSensorRange (3)
        Vector3 relativePosition = transform.InverseTransformPoint(target.position);
        Vector3 normalizedPosition = Vector3.ClampMagnitude(relativePosition / maxSensorRange, 1f);
        sensor.AddObservation(normalizedPosition);

        // [3] Normalized 3D distance to target (1)
        Vector3 toTarget = target.position - transform.position;
        _cachedDXZ = new Vector2(toTarget.x, toTarget.z).magnitude;
        _cachedDeltaY = transform.position.y - target.position.y;
        float normalizedDistance = Mathf.Clamp01(toTarget.magnitude / maxSensorRange);
        sensor.AddObservation(normalizedDistance);

        // [4..6] Unit direction to target in drone local space (3)
        Vector3 unitDirection = toTarget.sqrMagnitude > 0.000001f
            ? transform.InverseTransformDirection(toTarget.normalized)
            : Vector3.zero;
        sensor.AddObservation(unitDirection);

        // [7..9] Drone local velocity / maxSpeed, clamped magnitude 1 (3)
        Vector3 localVelocity = Vector3.ClampMagnitude(transform.InverseTransformDirection(_rb.linearVelocity) / maxSpeed, 1f);
        sensor.AddObservation(localVelocity);

        // [10..12] Relative local velocity (targetVel - droneVel) / (maxSpeed + maxTargetSpeed), clamped magnitude 1 (3)
        float maxTargetSpeed = arena != null ? arena.maxTargetSpeed : 8.5f;
        float maxRelSpeed = maxSpeed + maxTargetSpeed;
        Vector3 targetVel = targetRb != null ? targetRb.linearVelocity : Vector3.zero;
        Vector3 relLocalVel = Vector3.ClampMagnitude(transform.InverseTransformDirection(targetVel - _rb.linearVelocity) / maxRelSpeed, 1f);
        sensor.AddObservation(relLocalVel);

        // [13] Normalized yaw rate (1)
        float maxYawRateRad = maxYawRateDeg * Mathf.Deg2Rad;
        float normalizedYawRate = Mathf.Clamp(_rb.angularVelocity.y / maxYawRateRad, -1f, 1f);
        sensor.AddObservation(normalizedYawRate);

        // [14] Ground clearance warning (1)
        sensor.AddObservation(Mathf.Clamp01((1.5f - _cachedGroundDist) / 1.5f));

        // [15] Overhead/ceiling clearance warning (1)
        sensor.AddObservation(Mathf.Clamp01((1.5f - _cachedCeilingDist) / 1.5f));

        // [16] Target Line-of-Sight (0 or 1) (1)
        sensor.AddObservation(_cachedHasLOS);

        // [17] Aggression conditioning scalar (1)
        sensor.AddObservation(Mathf.Clamp01(aggression));

        // [18] Normalized goalDist via InverseLerp (1)
        sensor.AddObservation(Mathf.InverseLerp(minGoalDist, maxGoalDist, goalDist));

        // [19] Normalized goalHeight via InverseLerp (1)
        sensor.AddObservation(Mathf.InverseLerp(minGoalHeight, maxGoalHeight, goalHeight));

        // [20] Signed normalized planar distance error (1)
        float distError = (_cachedDXZ - goalDist) / maxGoalDist;
        sensor.AddObservation(Mathf.Clamp(distError, -1f, 1f));

        // [21] Signed normalized elevation error (1)
        float heightError = (_cachedDeltaY - goalHeight) / maxGoalHeight;
        sensor.AddObservation(Mathf.Clamp(heightError, -1f, 1f));

        // [22..25] Previous step continuous actions: surge, sway, heave, yawCmd (4)
        sensor.AddObservation(_prevAction[0]);
        sensor.AddObservation(_prevAction[1]);
        sensor.AddObservation(_prevAction[2]);
        sensor.AddObservation(_prevAction[3]);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        var cont = actions.ContinuousActions;
        float surge = Mathf.Clamp(cont[0], -1f, 1f);
        float sway = Mathf.Clamp(cont[1], -1f, 1f);
        float heave = Mathf.Clamp(cont[2], -1f, 1f);
        float yawCmd = Mathf.Clamp(cont[3], -1f, 1f);

        // omnidirectional thruster logic
        float mult = surge < 0f ? 0.85f : 1f;
        Vector3 totalForce = (transform.forward * surge * surgeAccel * mult) + (transform.right * sway * swayAccel) + (transform.up * heave * heaveAccel);

        _rb.AddForce(totalForce, ForceMode.Acceleration);

        // Bounded yaw acceleration for realistic rotational inertia
        float targetYawRad = yawCmd * maxYawRateDeg * Mathf.Deg2Rad;
        float nextYawRad = Mathf.MoveTowards(_rb.angularVelocity.y, targetYawRad, maxYawAccelDeg * Mathf.Deg2Rad * Time.fixedDeltaTime);
        _rb.angularVelocity = new Vector3(0f, nextYawRad, 0f);

        _currSurge = surge;
        _currSway = sway;
        _currHeave = heave;
        _currYawCmd = yawCmd;

        int episodeBeforeReward = _episodeId;
        EvaluateCombatReward();
        if (episodeBeforeReward == _episodeId)
        {
            _prevAction[0] = surge;
            _prevAction[1] = sway;
            _prevAction[2] = heave;
            _prevAction[3] = yawCmd;
        }
    }

    public void EvaluateCombatReward()
    {
        UpdateSpatialSensors();

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
        float closingSpeed = Vector3.Dot(relVel, dirToTarget);

        // --- STALK MODE (GOAL-CONDITIONED) ---
        // 1. Adaptive Sigmas: Tighter precision at 2m, slightly wider margin at 12m
        float normDist = Mathf.InverseLerp(minGoalDist, maxGoalDist, goalDist);
        float sigmaD = Mathf.Lerp(0.8f, 1.8f, normDist);
        float normHeight = Mathf.InverseLerp(minGoalHeight, maxGoalHeight, goalHeight);
        float sigmaH = Mathf.Lerp(0.7f, 1.4f, normHeight);

        // Decoupled 3D Gaussian Perch + Broad Pull + LOS Gating
        float rDist = Mathf.Exp(-((dXZ - goalDist) * (dXZ - goalDist)) / (2f * sigmaD * sigmaD));
        float rHeight = Mathf.Exp(-((deltaY - goalHeight) * (deltaY - goalHeight)) / (2f * sigmaH * sigmaH));
        float losFactor = 0.25f + 0.75f * _cachedHasLOS;
        float rPerch = facingGate * losFactor * (0.30f * rDist + 0.30f * rHeight + 0.40f * rDist * rHeight);
        float rBroad = 0.10f * (1f - Mathf.Clamp01(Mathf.Abs(dXZ - goalDist) / maxGoalDist))
                     + 0.10f * (1f - Mathf.Clamp01(Mathf.Abs(deltaY - goalHeight) / maxGoalHeight));

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
            Vector3 relVelXZ = new Vector3(relVel.x, 0f, relVel.z);
            float retreatSpeedXZ = -Vector3.Dot(relVelXZ, dirToTargetXZ);
            float backpedalBonus = 0.4f * facingGate * Mathf.Clamp01(retreatSpeedXZ / maxSpeed);
            rBreach = Mathf.Min(0f, penalty + backpedalBonus);
        }

        // 4. Dynamic Circle Strafe Band: Centered on goalDist (+- 0.7m), gated by height and LOS
        float strafeSpeed = Mathf.Abs(Vector3.Dot(_rb.linearVelocity, transform.right));
        float strafeMin = Mathf.Max(0.5f, goalDist - 0.7f);
        float strafeMax = goalDist + 0.7f;
        float rStrafe = (dXZ >= strafeMin && dXZ <= strafeMax)
            ? (0.15f * facingGate * rHeight * losFactor * Mathf.Clamp01(strafeSpeed / maxSpeed))
            : 0f;

        float rStalk = rPerch + rBroad + rApproach + rBreach + rStrafe;

        // --- STRIKE MODE ---
        // 3D closing speed + vertical dive speed
        float vertStrikeVel = Mathf.Sign(toTarget.y) * relVel.y;
        float rClose = 0.8f * Mathf.Clamp(closingSpeed / maxSpeed, -1f, 1f) + 0.4f * Mathf.Clamp(vertStrikeVel / maxSpeed, -1f, 1f);

        // terminal magnet inside 2.5m
        float rMagnet = dist3D < 2.5f ? 0.6f * Mathf.Exp(-Mathf.Pow(Mathf.Max(0f, dist3D - 1.10f), 2f) / (2f * 0.45f * 0.45f)) : 0f;

        float rUrgency = -0.12f;
        float rStrike = facingGate * (rClose + rMagnet) + rUrgency;

        // 5. Continuous Proximity Clearance (Ground & Overhead Warning)
        float groundPen = _cachedGroundDist < 1.0f ? -0.2f * (1.0f - _cachedGroundDist) : 0f;
        float ceilPen = _cachedCeilingDist < 1.0f ? -0.2f * (1.0f - _cachedCeilingDist) : 0f;
        float rProximity = groundPen + ceilPen;

        // 6. 4-Axis Action-Jerk Smoothness Regularization
        float d0 = _currSurge - _prevAction[0];
        float d1 = _currSway - _prevAction[1];
        float d2 = _currHeave - _prevAction[2];
        float d3 = _currYawCmd - _prevAction[3];
        float rSmooth = -0.02f * ((d0 * d0 + d1 * d1 + d2 * d2 + d3 * d3) * 0.25f);

        float stepReward = 0.01f * ((1f - aggression) * (rStalk + rProximity + rSmooth) + aggression * (rStrike + rProximity + 0.5f * rSmooth));
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
