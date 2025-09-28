using TMPro;
using UnityEngine;

public class OpenText : MonoBehaviour
{
    public TextMeshProUGUI opentext;
    public Transform player;
    public Transform Door;
    public float openDistance;
    void Start()
    {
        opentext.enabled = false;
    }

    // Update is called once per frame
    void Update()
    {
        // 1) ‹——£”»’è
        float dist = Vector3.Distance(player.position, Door.position);
        if (dist >= openDistance)
        {
            opentext.enabled = false;
        }
        else
        {
            opentext.enabled = true;
        }
    }
}
