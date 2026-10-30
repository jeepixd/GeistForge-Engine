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


    
}
