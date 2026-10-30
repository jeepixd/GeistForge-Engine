using UnityEngine;
using System;
using Unity.VisualScripting;

[RequireComponent(typeof(KillerDroneAgent))]

public class DroneDetonator : MonoBehaviour
{
    [Header("payload settings")]
    [Tooltip("optional explosion particle prefab")]
    public GameObject explosionVfxPrefab;
    [Tooltip("distance to target that triggers the payload ")]
    public float detonationRadius = 1.25f;

    //the event swarmdirector will listen to this 
    public event Action<KillerDroneAgent> OnDetonated;

    private KillerDroneAgent _agent;
    private bool _hasDetonated = false;

    private void Awake()
    {
        _agent = GetComponent<KillerDroneAgent>();

    }

    private void Update()
    {
        if (_hasDetonated || _agent == null || _agent.target == null) return;

        //proximity detonation:
        float distanceToTarget = Vector3.Distance(transform.position, _agent.target.position);
        if(_agent.aggression >= 0.5f && distanceToTarget <= detonationRadius)
        {
            //call detonation method here 
            Detonate();

        }
    }
    private void OnCollisionEnter(Collision collision)
    {
        if (_hasDetonated || _agent == null || _agent.target == null) return;
        // Physical Impact Detonation:
        // If we slam directly into the target collider while striking
        if (_agent.aggression >= 0.5f && collision.transform == _agent.target)
        {
            Detonate();
        }
    }

    public void Detonate()
    {
        if (_hasDetonated) return;
        _hasDetonated = true;

        if(explosionVfxPrefab != null)
        {
            Instantiate(explosionVfxPrefab, transform.position, Quaternion.identity);

        }
        else
        {
            Debug.Log("Yo pali ko tihar ma pataka na vhayesi gas cylinder nai padhkaidim kay ho");
        }

        //broadcast the event
        OnDetonated?.Invoke(_agent);

        Destroy(gameObject);


    }
}
