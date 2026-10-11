using UnityEngine;
using Unity.InferenceEngine;



public class BrainRunner : MonoBehaviour
{
    [SerializeField] private ModelAsset modelAsset;

    private Model runtimeModel;
    private Worker worker;
    private Rigidbody rb;
    private Vector3 startPosition;

    void Start()
    {
        rb= GetComponent<Rigidbody>();
        startPosition = transform.position;

        runtimeModel = ModelLoader.Load(modelAsset);
        worker = new Worker(runtimeModel, BackendType.GPUCompute);


    }

    void FixedUpdate()
    {
        Vector3 pos = transform.position - startPosition;
        Vector3 vel = rb.linearVelocity;

        float[] state = new float[]
        {
            pos.x,
            pos.y,
            vel.x,
            vel.y,
            transform.eulerAngles.z * Mathf.Deg2Rad,
            rb.angularVelocity.z,
            0f,
            0f
            
        };
        //feed into the brain 
        using Tensor<float> input = new Tensor<float>(new TensorShape(1, 8), state);
        worker.Schedule(input);

        // read the desicion
        Tensor<float> output = worker.PeekOutput() as Tensor<float>;
        float[] scores = output.DownloadToArray();

        //find the action with highest score
        int bestAction =0;
        for(int i =1; i< scores.Length; i++){
            if(scores[i]> scores[bestAction])
                bestAction=i;
        }

        //apply the force based on actions
        float thrust = 10f;
        switch(bestAction)
        {
            case 0: break;
            case 1: rb.AddForce(thrust* Vector3.left); break;
            case 2: rb.AddForce(thrust* Vector3.up); break;
            case 3: rb.AddForce(thrust* Vector3.right); break;

        }
        

    }
    void OnDestroy()
    {
        worker?.Dispose();
    }
}
