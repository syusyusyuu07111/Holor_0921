using UnityEngine;

public class GhostRotateFix : MonoBehaviour
{
    public Transform Target;


    // Update is called once per frame
    void Update()
    {
        transform.LookAt(Target);
    }
}
