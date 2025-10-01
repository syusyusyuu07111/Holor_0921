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
        //�G�̈ʒu�Ƌ����擾--------------------------------------------------------------------------------------------------------------
        float NorthPictureDistance;
        NorthPictureDistance = Vector3.Distance(Player.transform.position, NorthPicture.transform.position);

        float EastPictureDistance;
        EastPictureDistance = Vector3.Distance(Player.transform.position, EastPicture.transform.position);

        float WestPictureDistance;
        WestPictureDistance = Vector3.Distance(Player.transform.position, WestPicture.transform.position);

        //�ǂꂩ����̊G�ɐG��鋗���ɂ���Ƃ��̏��� �e�L�X�g�\��
        if(NorthPictureDistance<TouchDistance||EastPictureDistance<TouchDistance||WestPictureDistance<TouchDistance)
        {
            text.gameObject.SetActive(true);
        }
        else
        {
            text.gameObject.SetActive(false);
        }


        //northpicture�̊G�Ƌ߂��Ƃ��̏���--------------------------------------------------------------------------------------------

        if (NorthPictureDistance < TouchDistance)//northpicture���G��鋗���ɂ���Ƃ��̋���
        {

        }
        //Eastpicture�̊G�Ƌ߂��Ƃ��̏���---------------------------------------------------------------------------------------------

        if (EastPictureDistance < TouchDistance)//eastpicture���G��鋗���ɂ���Ƃ��̋���
        {

        }

        //Westpicture�̊G�Ƌ߂��Ƃ��̏���---------------------------------------------------------------------------------------------

        if (WestPictureDistance < TouchDistance)//Westpicture���G��鋗���ɂ���Ƃ��̋���
        {

        }
    }
}
