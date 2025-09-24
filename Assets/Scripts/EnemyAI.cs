using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    bool GhostSpawn = false;
    public Transform Player;
    public GameObject Ghost;
    public Vector3 GhostPosition;
    public int GhostEncountChance;

    //オーディオ系=========================================================================================
    public AudioClip SpawnSE;
    AudioSource audioSource;
    void Start()
    {
        StartCoroutine("Spawn");
        audioSource = GetComponent<AudioSource>();
    }
    private void Update()
    {
        GhostPosition = new Vector3(Player.transform.position.x + Random.Range(10f, 50f),
        Player.transform.position.y, Player.transform.position.z + Random.Range(10f, 50f));
    }
    IEnumerator Spawn()
    {
        //抽選が当たるまでは抽選し続ける==========================================================
        while (GhostSpawn == false)
        {
            GhostEncountChance = Random.Range(0, 50);
            if (GhostEncountChance > 30)
            {
                GhostSpawn = true;
            }
            yield return new WaitForSeconds(5.0f);
        }
        //抽選が当たった時の処理=================================================================
        if (GhostSpawn == true)
        {
            audioSource.PlayOneShot(SpawnSE);
            Instantiate(Ghost, GhostPosition, Quaternion.identity);
            GhostSpawn = false;
            StartCoroutine("Spawn");
        }
    }
}
