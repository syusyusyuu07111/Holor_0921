using TMPro;
using UnityEngine;

public class OpenText : MonoBehaviour
{
    public TextMeshProUGUI opentext;
    public Transform player;
    public Transform Door;
    public float openDistance;
    public bool CanOpen { get; private set; }  // �� �v���p�e�B�͂��̂܂�
    // public static OpenText instance; �� ����

    void Start()
    {
        opentext.enabled = false;
        CanOpen = false;
    }

    void Update()
    {
        float dist = Vector3.Distance(player.position, Door.position);
        bool can = (dist < openDistance);
        if (opentext.enabled != can) opentext.enabled = can;
        CanOpen = can;
    }
}
