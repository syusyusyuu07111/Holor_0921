using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class PictureGhostEncount : MonoBehaviour
{
    public Transform NorthPicture;
    public GameObject EastPicture;
    public GameObject WestPicture;
    public Transform Player;
    InputSystem_Actions input;
    public float TouchDistance;
    TextMeshProUGUI text;
    public GameObject Ghost;

    public void Awake()
    {
        input = new InputSystem_Actions();
    }
    public void OnEnable()
    {
        input.Player.Enable();
    }
    private void Start()
    {
        text.gameObject.SetActive(false);
    }

    void Update()
    {
        //ŠG‚ÌˆÊ’u‚Æ‹——£æ“¾--------------------------------------------------------------------------------------------------------------
        float NorthPictureDistance;
        NorthPictureDistance = Vector3.Distance(Player.transform.position, NorthPicture.transform.position);

        float EastPictureDistance;
        EastPictureDistance = Vector3.Distance(Player.transform.position, EastPicture.transform.position);

        float WestPictureDistance;
        WestPictureDistance = Vector3.Distance(Player.transform.position, WestPicture.transform.position);

        //‚Ç‚ê‚©‚µ‚ç‚ÌŠG‚ÉG‚ê‚é‹——£‚É‚¢‚é‚Æ‚«‚Ìˆ— ƒeƒLƒXƒg•\¦
        if(NorthPictureDistance<TouchDistance||EastPictureDistance<TouchDistance||WestPictureDistance<TouchDistance)
        {
            text.gameObject.SetActive(true);
        }
        else
        {
            text.gameObject.SetActive(false);
        }


        //northpicture‚ÌŠG‚Æ‹ß‚¢‚Æ‚«‚Ìˆ—--------------------------------------------------------------------------------------------

        if (NorthPictureDistance < TouchDistance)//northpicture‚ªG‚ê‚é‹——£‚É‚ ‚é‚Æ‚«‚Ì‹““®
        {

        }
        //Eastpicture‚ÌŠG‚Æ‹ß‚¢‚Æ‚«‚Ìˆ—---------------------------------------------------------------------------------------------

        if (EastPictureDistance < TouchDistance)//eastpicture‚ªG‚ê‚é‹——£‚É‚ ‚é‚Æ‚«‚Ì‹““®
        {

        }

        //Westpicture‚ÌŠG‚Æ‹ß‚¢‚Æ‚«‚Ìˆ—---------------------------------------------------------------------------------------------

        if (WestPictureDistance < TouchDistance)//Westpicture‚ªG‚ê‚é‹——£‚É‚ ‚é‚Æ‚«‚Ì‹““®
        {

        }
    }
}
