using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class LightOff : MonoBehaviour
{
    public GameObject Player;                               // �v���C���[
    public GameObject Light;                                // �ߐڔ���̈ʒu�i�X�C�b�`���j
    public float PushDistance = 3.0f;                       // �C���^���N�g����
    public bool OnLight = true;                             // �����C�g���_���Ă��邩
    public GameObject Ghost;                                // �������ɏ����Ώہi�C�Ӂj

    [SerializeField] private List<Light> LightLists = new();// ����Ώۃ��C�g�Q

    InputSystem_Actions input;                              // �VInputSystem

    // --------------- �������������ǉ��i2��ނ̃e�L�X�g�j ---------------
    public TextMeshProUGUI PromptText;                      // �߂Â����������o���u�L�[�ē��v
    public TextMeshProUGUI MsgText;                         // �_����/�������g�u�Ԃ����h�o�����b�Z�[�W
    // --------------- �����܂Ŏ�����ǉ� ---------------

    // --------------- �������������ǉ��i����/�\�����ԁj ---------------
    [Header("�����ݒ�")]
    public string PromptOn = "�yE�z���C�g������";           // �_�����ɋ߂Â������̈ē�
    public string PromptOff = "�yE�z���C�g��_����";         // �������ɋ߂Â������̈ē�
    public string MsgTurnedOff = "���C�g���������悤��";     // ����������̃��b�Z�[�W
    public string MsgTurnedOn = "���C�g���_�����悤��";     // �_��������̃��b�Z�[�W

    [Header("�\������")]
    public float EventMsgDuration = 5.0f;                   // ���b�Z�[�W�\���b��
    private float _msgTimer = 0f;                           // �c��\������
    // --------------- �����܂Ŏ�����ǉ� ---------------

    private void Awake()
    {
        input = new InputSystem_Actions();                  // ���̓N���X����
    }

    private void OnEnable()
    {
        input.Player.Enable();                              // �A�N�V�����L����
        // --------------- �������������ǉ��i�����͔�\���j ---------------
        if (PromptText) { PromptText.text = ""; PromptText.gameObject.SetActive(false); }
        if (MsgText) { MsgText.text = ""; MsgText.gameObject.SetActive(false); }
        // --------------- �����܂Ŏ�����ǉ� ---------------
    }

    private void OnDisable()
    {
        input.Player.Disable();                             // �A�N�V����������
    }

    void Update()
    {
        if (!Player || !Light) return;                      // �Q�ƌ����K�[�h

        // �����`�F�b�N ---------------------------------------------------------------
        float distance = Vector3.Distance(Player.transform.position, Light.transform.position);
        bool inRange = (distance < PushDistance);

        // �߂Â�����L�[�ē���\���^���ꂽ���\�� -----------------------------------
        if (PromptText) PromptText.gameObject.SetActive(inRange);
        if (inRange && PromptText)
        {
            PromptText.text = OnLight ? PromptOn : PromptOff;
        }

        // ���͂Ńg�O�� ---------------------------------------------------------------
        if (inRange && input.Player.Jump.triggered)        // �� Jump���C���^���N�g�Ɏg�p��
        {
            if (OnLight) Off(); else On();
        }

        // ���b�Z�[�W�����Ǘ� ---------------------------------------------------------
        if (_msgTimer > 0f)
        {
            _msgTimer -= Time.deltaTime;
            if (_msgTimer <= 0f && MsgText)
            {
                MsgText.text = "";
                MsgText.gameObject.SetActive(false);       // 5�b�o���������
            }
        }
    }

    // --------------- �������������ǉ��i�S���C�gOFF�j ---------------
    void Off()
    {
        foreach (var l in LightLists)
        {
            if (l) l.enabled = false;
        }
        OnLight = false;
        Debug.Log("���C�g��������");

        if (Ghost) Destroy(Ghost.gameObject);              // �d�l�F�������ɃS�[�X�g�j���i�C�Ӂj

        ShowEventMessage(MsgTurnedOff);                    // �u�������悤���v
    }
    // --------------- �����܂Ŏ�����ǉ� ---------------

    // --------------- �������������ǉ��i�S���C�gON�j ---------------
    void On()
    {
        foreach (var l in LightLists)
        {
            if (l) l.enabled = true;
        }
        OnLight = true;
        Debug.Log("���C�g��_����");

        ShowEventMessage(MsgTurnedOn);                     // �u�_�����悤���v
    }
    // --------------- �����܂Ŏ�����ǉ� ---------------

    // --------------- �������������ǉ��i���b�Z�[�W�\�����ʁj ---------------
    void ShowEventMessage(string msg)
    {
        if (!MsgText) return;
        MsgText.text = msg;
        MsgText.gameObject.SetActive(true);
        _msgTimer = Mathf.Max(0.01f, EventMsgDuration);    // �\�����ԃ��Z�b�g
    }
    // --------------- �����܂Ŏ�����ǉ� ---------------
}
