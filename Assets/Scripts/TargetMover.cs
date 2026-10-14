using UnityEngine;

public class TargetMover : MonoBehaviour
{
 


    private void Update()
    {
        transform.localPosition = new Vector3(Mathf.PingPong(Time.time * 3f, 6f) - 3f, 0.5f, transform.localPosition.z);

    }
}
