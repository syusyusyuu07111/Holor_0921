using System.Collections;
using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    bool GhostSpawn=false;
    public Transform Player;
    public GameObject Ghost;
    public Vector3 GhostPosition;
    void Start()
    {
        GhostPosition = new Vector3(Player.transform.position.x+Random.Range(10f,50f),
        Player.transform.position.y, Player.transform.position.z+Random.Range(10f,50f));
    }
    void Update()
    {
        if(GhostSpawn==false)
        {
            StartCoroutine("Spawn");
        }
        else if(GhostSpawn==true)
        {
            Instantiate(Ghost, GhostPosition, Quaternion.identity);
        }

    }
    IEnumerator Spawn()
    {

        yield return new WaitForSeconds(30.0f);
    }
}
