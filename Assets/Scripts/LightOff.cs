using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class LightOff : MonoBehaviour
{
    public GameObject Player;
    public GameObject Light;
    public float PushDistance = 3.0f;
    public bool OnLight = true;
    public GameObject Ghost;

    //���C�g�I�u�W�F�N�g
    [SerializeField] private List<Light> LightLists = new();

    InputSystem_Actions input;

    public TextMeshProUGUI text;

    private void Awake()
    {
        input = new InputSystem_Actions();
    }
    private void OnEnable()
    {
        input.Player.Enable();
    }
    void Update()
    {


        //���C�g����������S���́@���C�g�������֐��Ăяo��-------------------------------------------------------------------------
        float distance = Vector3.Distance(Player.transform.position, Light.transform.position);
        //���C�g�I���@�I�t�̃e�L�X�g�\��
        if (distance < PushDistance)
        {
            text.gameObject.SetActive(true);
        }
        else
        {
            text.gameObject.SetActive(false);
        }

        //���C�g�̋߂��@�{�^�������@���C�g�����Ă�t���O�ɂȂ��Ă�Ƃ�
        if (distance < PushDistance && input.Player.Interact.triggered && OnLight == true)
        {
            Off();
        }
        else if (distance < PushDistance && input.Player.Interact.triggered && OnLight == false)
        {
            On();
        }
    }
    //�S���̃��C�g������---------------------------------------------------------------------------------------------------------
    void Off()
    {
        foreach (Light light in LightLists)
        {
            light.enabled = false;
            OnLight = false;
            Debug.Log("������");
            Destroy(Ghost.gameObject);
        }
    }
    //���C�g���I���ɂ���--------------------------------------------------------------------------------------------------------------
    void On()
    {
        foreach (Light light in LightLists)
        {
            light.enabled = true;
            OnLight = true;
        }
    }
}
