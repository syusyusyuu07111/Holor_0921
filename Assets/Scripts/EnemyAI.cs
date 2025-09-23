using System.Collections;
using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    bool GhostSpawn=false;
    public Transform Player;
    void Start()
    {
    }
    void Update()
    {
        if(GhostSpawn)
        {
            StartCoroutine("Spawn");
        }

    }
    IEnumerator Spawn()
    {

        yield return new WaitForSeconds(30.0f);
    }
}
