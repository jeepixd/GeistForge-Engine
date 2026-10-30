using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;


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
        if (drones == null || playerTarget == null) return null;
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
    
}
