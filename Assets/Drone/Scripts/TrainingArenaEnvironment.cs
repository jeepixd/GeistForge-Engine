using UnityEngine;
using Unity.MLAgents;

/// <summary>
/// TrainingArenaEnvironment: Manages the 24m Combat Box, pre-allocated obstacle pool,
/// adjustable ceiling, and the 4-State Adversarial Target Bot driven by ML-Agents Curriculum Parameters.
/// </summary>
public class TrainingArenaEnvironment : MonoBehaviour
{
    [Header("References")]
    public KillerDroneAgent droneAgent;
    public Rigidbody targetRb;
    public Transform ceilingTransform;
    public Transform[] pillarPool;       // 8 Vertical Cylinders/Boxes
    public Transform[] floatingBeamPool; // 4 Horizontal Overhangs

    [Header("Arena Bounds (Local)")]
    public float arenaHalfExtentXZ = 10.5f;
    public float maxTargetSpeed = 9.0f;

    // Curriculum Parameters fetched from ML-Agents YAML
    private EnvironmentParameters _envParams;
    private float _targetSpeedMult = 1.0f;
    private int _activePillars = 6;
    private int _activeBeams = 2;
    private bool _enableKnockback = false;

    // Target Bot State Machine
    private enum BotState { Stationary, BullRush, Serpentine, PillarHug }
    private BotState _currentState;
    private float _stateTimer;
    private Vector3 _moveDir;
    private float _serpentinePhase;

    private void Awake()
    {
        _envParams = Academy.Instance.EnvironmentParameters;
        if (targetRb != null)
        {
            targetRb.interpolation = RigidbodyInterpolation.Interpolate;
            targetRb.constraints = RigidbodyConstraints.FreezeRotation;
        }
    }

    /// <summary>
    /// Called by KillerDroneAgent.OnEpisodeBegin() to reset the entire arena.
    /// </summary>
    public void ResetArena()
    {
        if (_envParams == null)
        {
            _envParams = Academy.Instance.EnvironmentParameters;
        }

        // 1. Read Curriculum Parameters (with sensible defaults for Editor testing)
        _targetSpeedMult = _envParams.GetWithDefault("target_speed_mult", 1.0f);
        _activePillars   = Mathf.RoundToInt(_envParams.GetWithDefault("obstacle_pillars", 6f));
        _activeBeams     = Mathf.RoundToInt(_envParams.GetWithDefault("obstacle_beams", 2f));
        float minCeiling = _envParams.GetWithDefault("min_ceiling_height", 6.0f);
        _enableKnockback = _envParams.GetWithDefault("enable_knockback", 0f) > 0.5f;

        // 2. Randomize Ceiling Height
        float ceilingY = Random.Range(minCeiling, 10.0f);
        if (ceilingTransform != null)
        {
            ceilingTransform.localPosition = new Vector3(0f, ceilingY, 0f);
        }

        // 3. Spawn Target in a clear location
        Vector3 targetLocalPos = new Vector3(
            Random.Range(-arenaHalfExtentXZ * 0.6f, arenaHalfExtentXZ * 0.6f),
            1.0f,
            Random.Range(-arenaHalfExtentXZ * 0.6f, arenaHalfExtentXZ * 0.6f)
        );

        if (targetRb != null)
        {
            targetRb.transform.localPosition = targetLocalPos;
            targetRb.linearVelocity = Vector3.zero;
            targetRb.angularVelocity = Vector3.zero;
        }

        // 4. Spawn Drone 2m to 9m away at random elevation and yaw
        Vector2 randCircle = Random.insideUnitCircle.normalized * Random.Range(2.0f, 9.0f);
        Vector3 droneLocalPos = new Vector3(
            Mathf.Clamp(targetLocalPos.x + randCircle.x, -arenaHalfExtentXZ, arenaHalfExtentXZ),
            Random.Range(1.2f, ceilingY - 1.2f),
            Mathf.Clamp(targetLocalPos.z + randCircle.y, -arenaHalfExtentXZ, arenaHalfExtentXZ)
        );

        if (droneAgent != null)
        {
            droneAgent.transform.localPosition = droneLocalPos;
            droneAgent.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }

        // 5. Randomize Pre-Allocated Obstacles (Ensuring 2.5m Clearance from Spawns)
        if (pillarPool != null && pillarPool.Length > 0)
        {
            RandomizeObstacles(pillarPool, _activePillars, targetLocalPos, droneLocalPos, false, ceilingY);
        }

        if (floatingBeamPool != null && floatingBeamPool.Length > 0)
        {
            RandomizeObstacles(floatingBeamPool, _activeBeams, targetLocalPos, droneLocalPos, true, ceilingY);
        }

        PickNextBotState();
    }

    /// <summary>
    /// Zero-GC obstacle randomizer: toggles SetActive and repositions pre-allocated transforms.
    /// Never calls Instantiate() or Destroy().
    /// </summary>
    private void RandomizeObstacles(Transform[] pool, int activeCount, Vector3 targetPos, Vector3 dronePos, bool isFloatingBeam, float ceilingY)
    {
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] == null) continue;

            if (i >= activeCount)
            {
                pool[i].gameObject.SetActive(false);
                continue;
            }

            pool[i].gameObject.SetActive(true);
            Vector3 candidatePos = Vector3.zero;

            // Try up to 10 times to find a spot outside 2.5m spawn clearance zones
            for (int attempt = 0; attempt < 10; attempt++)
            {
                float x = Random.Range(-arenaHalfExtentXZ, arenaHalfExtentXZ);
                float z = Random.Range(-arenaHalfExtentXZ, arenaHalfExtentXZ);
                float y = isFloatingBeam ? Random.Range(2.5f, ceilingY - 1.0f) : (ceilingY * 0.5f);
                candidatePos = new Vector3(x, y, z);

                if (Vector2.Distance(new Vector2(x, z), new Vector2(targetPos.x, targetPos.z)) > 2.5f &&
                    Vector2.Distance(new Vector2(x, z), new Vector2(dronePos.x, dronePos.z)) > 2.5f)
                {
                    break;
                }
            }

            pool[i].localPosition = candidatePos;

            if (isFloatingBeam)
            {
                pool[i].localScale = new Vector3(Random.Range(3f, 6f), Random.Range(0.5f, 1.2f), Random.Range(0.6f, 1.5f));
                pool[i].localRotation = Quaternion.Euler(0f, Random.Range(0f, 180f), 0f);
            }
            else
            {
                float width = Random.Range(0.6f, 1.6f);
                pool[i].localScale = new Vector3(width, ceilingY, width);
            }
        }
    }

    private void FixedUpdate()
    {
        UpdateTargetBot();

        // Stage 3 Domain Randomization: Occasional Velocity Impulse (Weapon Knockback / Wind)
        if (_enableKnockback && droneAgent != null && Random.value < 0.008f)
        {
            Rigidbody droneRb = droneAgent.GetComponent<Rigidbody>();
            if (droneRb != null)
            {
                droneRb.AddForce(Random.onUnitSphere * Random.Range(3f, 7f), ForceMode.VelocityChange);
            }
        }
    }

    /// <summary>
    /// Rolls one of the 4 adversarial bot behaviors every 2.5 to 4.5 seconds.
    /// </summary>
    private void PickNextBotState()
    {
        _stateTimer = Random.Range(2.5f, 4.5f);

        if (_targetSpeedMult <= 0.01f)
        {
            _currentState = BotState.Stationary;
            return;
        }

        float roll = Random.value;
        if (roll < 0.20f) _currentState = BotState.Stationary;       // 20% Stand still
        else if (roll < 0.55f) _currentState = BotState.BullRush;    // 35% Charge at the drone!
        else if (roll < 0.85f) _currentState = BotState.Serpentine;  // 30% Evasive weaving
        else _currentState = BotState.PillarHug;                      // 15% Orbit around obstacles

        Vector2 randDir = Random.insideUnitCircle.normalized;
        _moveDir = new Vector3(randDir.x, 0f, randDir.y);
    }

    /// <summary>
    /// Drives the target dummy's Rigidbody every FixedUpdate based on the current BotState.
    /// </summary>
    private void UpdateTargetBot()
    {
        if (targetRb == null || droneAgent == null) return;

        _stateTimer -= Time.fixedDeltaTime;
        if (_stateTimer <= 0f) PickNextBotState();

        float speed = maxTargetSpeed * _targetSpeedMult;
        Vector3 desiredVel = Vector3.zero;

        switch (_currentState)
        {
            case BotState.Stationary:
                desiredVel = Vector3.zero;
                break;

            case BotState.BullRush:
                // Charge directly under the drone to breach its < 2.0m personal space!
                Vector3 toDroneXZ = droneAgent.transform.localPosition - targetRb.transform.localPosition;
                toDroneXZ.y = 0f;
                desiredVel = toDroneXZ.normalized * speed;
                break;

            case BotState.Serpentine:
                _serpentinePhase += Time.fixedDeltaTime * 4.0f;
                Vector3 perp = new Vector3(-_moveDir.z, 0f, _moveDir.x);
                desiredVel = (_moveDir + perp * Mathf.Sin(_serpentinePhase) * 0.7f).normalized * speed;
                break;

            case BotState.PillarHug:
                // Orbit around the arena center
                Vector3 toCenter = -targetRb.transform.localPosition;
                toCenter.y = 0f;
                Vector3 tangent = new Vector3(-toCenter.z, 0f, toCenter.x).normalized;
                desiredVel = (tangent * 0.8f + toCenter.normalized * 0.2f) * (speed * 0.75f);
                break;
        }

        // Bounce cleanly off outer arena boundaries
        Vector3 localPos = targetRb.transform.localPosition;
        if (Mathf.Abs(localPos.x) > arenaHalfExtentXZ - 1.0f) _moveDir.x = -Mathf.Sign(localPos.x);
        if (Mathf.Abs(localPos.z) > arenaHalfExtentXZ - 1.0f) _moveDir.z = -Mathf.Sign(localPos.z);

        targetRb.linearVelocity = Vector3.Lerp(targetRb.linearVelocity, desiredVel, Time.fixedDeltaTime * 8f);
    }
}
