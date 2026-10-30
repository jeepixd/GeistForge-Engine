using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using Unity.VisualScripting;


public class SwarmDirector : MonoBehaviour
{
    [Header("Swarm Roster & Target")]
    public Transform playerTarget;
    public List<KillerDroneAgent> drones = new List<KillerDroneAgent>();
    [Header("tactical timers")]
    [Tooltip("how many sec one  drone attack before rotating to another.")]
    public float strikeDuration = 4f;
    [Tooltip("how fast aggression blends (units per seconds)")]
    public float blendSpeed = 2.5f;

    [Header("Tactical scoring weights")]
    public LayerMask obstacleMask;
    public float losBonus = 2.0f;
    public float losBlockedPenalty = 5.0f;
    public float blindspotWeight = 1f;
    public float distancePenatlyWeight = 0.05f;

    //internal states 
    private float _strikeTimer = 0f;
    private KillerDroneAgent _currentStriker;


    private float CalculateTacticalScore(KillerDroneAgent drone)
    {
        if (drone == null || playerTarget == null) return -999f;
        //vector pointing from player to drone 
        Vector3 toDrone = drone.transform.position - playerTarget.position;
        float dist = toDrone.magnitude;
        Vector3 dirToDrone = dist > 0.001f ? (toDrone / dist) : Vector3.zero;
        //blind spot flank bonus 
        float flankScore = -Vector3.Dot(playerTarget.forward, dirToDrone);

        //line of sight check
        bool isBlocked = Physics.Raycast(drone.transform.position, -dirToDrone, dist, obstacleMask);
        float losScore = isBlocked ? -losBlockedPenalty : losBonus;
        //distance penalty 
        float distPenalty = dist * distancePenatlyWeight;

        //final weight sum 
        return (flankScore * blindspotWeight) + losScore - distPenalty;

    }

    private KillerDroneAgent SelectNewStriker()
    {
        if (drones == null || drones.Count== 0 || playerTarget == null) return null;
        if (drones.Count == 1) return drones[0];

        KillerDroneAgent bestCandidate = null;
        float highestScore = float.NegativeInfinity;
        
        for( int i =0; i < drones.Count; i++)
        {
            KillerDroneAgent candidate = drones[i];
            if(candidate == null) continue;

            //dont reselect the drone currently attacking unless its the only one alive 
            if (candidate == _currentStriker && drones.Count > 1)
                continue;

            float score = CalculateTacticalScore(candidate);
            if(score > highestScore)
            {
                highestScore = score;
                bestCandidate = candidate;

            }
        }
        return bestCandidate != null ? bestCandidate : _currentStriker;

    }
    private void RotateStriker()
    {
        _currentStriker = SelectNewStriker();
        _strikeTimer = strikeDuration; //resent the 4 sec cooldown
    }
    public void Start()
    {
        _strikeTimer = 1.5f;

        //subscribe to every drone detonation event
        foreach(var drone in drones)
        {
            if(drone != null && drone.TryGetComponent<DroneDetonator>(out var detonator))
            {
                detonator.OnDetonated += RemoveDrone;
            }
        }

    }
    public void Update()
    {
        //remove all drones that were destroyed 
        drones.RemoveAll(d => d == null);
        if (playerTarget == null || drones == null || drones.Count == 0) return;
        if (_currentStriker == null)
        {
            RotateStriker();

        }

        _strikeTimer -= Time.deltaTime;
        if(_strikeTimer <= 0f)
        {
            RotateStriker();
        }

        //smooth aggression blend for all drones 
        for(int i = 0; i < drones.Count; i++)
        {
            KillerDroneAgent drone = drones[i];
            if (drone == null) continue;

            float targetAlpha = (drone == _currentStriker) ? 1.0f : 0.0f;

            //smooth lside aggression 
            drone.aggression = Mathf.MoveTowards(drone.aggression, targetAlpha, blendSpeed * Time.deltaTime);
        }
    }
    public void RemoveDrone(KillerDroneAgent deadDrone)
    {
        if (deadDrone == null) return;
        drones.Remove(deadDrone);
        if(_currentStriker == deadDrone)
        {
            _currentStriker = null;
            if(drones.Count > 0)
            {
                RotateStriker();

            }
        }

    }

    //ai generated gizmos method 
    private void OnDrawGizmos()
    {
        if (playerTarget == null || drones == null) return;

        for (int i = 0; i < drones.Count; i++)
        {
            KillerDroneAgent drone = drones[i];
            if (drone == null) continue;

            if (drone == _currentStriker)
            {
                // Striker = Crimson line + Target Sphere
                Gizmos.color = Color.red;
                Gizmos.DrawLine(drone.transform.position, playerTarget.position);
                Gizmos.DrawWireSphere(drone.transform.position, 0.6f);
            }
            else
            {
                // Overwatch = Cyan line
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(drone.transform.position, playerTarget.position);
            }
        }
    }

}


