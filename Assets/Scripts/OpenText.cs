using TMPro;
using UnityEngine;

public class OpenText : MonoBehaviour
{
    public TextMeshProUGUI opentext;
    public Transform player;
    public Transform Door;
    public float openDistance;
    public bool CanOpen { get; set; }
    public static OpenText instance { get; set; }
    void Start()
    {
        opentext.enabled = false;
        CanOpen = false;
    }
    private void Awake()
    {
        instance = this;
    }
    void Update()
    {
        // 1) ‹——£”»’è
        float dist = Vector3.Distance(player.position, Door.position);
        if (dist >= openDistance)
        {
            opentext.enabled = false;
            CanOpen = false;
        }
        else
        {
            opentext.enabled = true;
            CanOpen = true;
        }
    }
}
