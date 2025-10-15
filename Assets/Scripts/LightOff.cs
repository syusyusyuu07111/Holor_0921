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
    public TextMeshProUGUI MsgText;                         // �_����/�������g�u�Ԃ����h�o�郁�b�Z�[�W
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

    // --------------- �������������ǉ��i�i�s�x�A�g�j ---------------
    [Header("�i�s�x�i�~�b�V�����j")]
    public HintText HintRef;                                // �q���g/�i�s�x�Ǘ��ւ̎Q�Ɓi�C�Ӂj
    public bool AutoFindHintRef = true;                     // ���ݒ�Ȃ玩������
    public int AdvanceAmountOnOff = 1;                      // �����Ői�ޒi���i�ʏ�1�j
    public int DecreaseAmountOnOn = 1;                      // �_���ŉ�����i���i�ʏ�1�j

    [Tooltip("�������C�g�ł͍ŏ��̏���������i�s�x�ɃJ�E���g����")]
    public bool CountOnlyOncePerThisLight = false;          // ��񂫂�ɂ��邩�H
    private bool _alreadyCounted = false;                   // ���̃��C�g�Ŋ��ɉ��Z������

    [Tooltip("�g�O���̘A�łł̑��d�J�E���g�h�~�i�b�j")]
    public float ToggleDebounceSeconds = 0.25f;             // �f�o�E���X�b�i����/�_���̗����Ŏg�p�j
    private float _lastToggleTime = -999f;                   // ���߃g�O������
    // --------------- �����܂Ŏ�����ǉ� ---------------

    private void Awake()
    {
        input = new InputSystem_Actions();                  // ���̓N���X����
        // --------------- �������������ǉ��i�i�s�x�̎����擾�F�񐄏��u���Ή��j ---------------
        if (AutoFindHintRef && !HintRef)
        {
#if UNITY_2023_1_OR_NEWER
            HintRef = UnityEngine.Object.FindAnyObjectByType<HintText>(FindObjectsInactive.Include);
#else
            HintRef = FindObjectOfType<HintText>(true);
#endif
        }
        // --------------- �����܂Ŏ�����ǉ� ---------------
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

        // �ߐڃ`�F�b�N ---------------------------------------------------------------
        float distance = Vector3.Distance(Player.transform.position, Light.transform.position);
        bool inRange = (distance < PushDistance);

        // �L�[�ē��i�ߐڎ��̂݁j -----------------------------------------------------
        if (PromptText) PromptText.gameObject.SetActive(inRange);
        if (inRange && PromptText)
        {
            PromptText.text = OnLight ? PromptOn : PromptOff;
        }

        // ���̓g�O�� ----------------------------------------------------------------
        if (inRange && input.Player.Jump.triggered)         // �� Jump ���C���^���N�g�Ɏg�p��
        {
            if (OnLight) Off(); else On();
        }

        // ���b�Z�[�W���� -------------------------------------------------------------
        if (_msgTimer > 0f)
        {
            _msgTimer -= Time.deltaTime;
            if (_msgTimer <= 0f && MsgText)
            {
                MsgText.text = "";
                MsgText.gameObject.SetActive(false);        // 5�b�o���������
            }
        }
    }

    // --------------- �������������ǉ��i�S���C�gOFF�F�i�s�x�{�j ---------------
    void Off()
    {
        // �f�o�E���X�i����/�_�����ʁj ------------------------------------------------
        if (Time.time - _lastToggleTime < ToggleDebounceSeconds) return;
        _lastToggleTime = Time.time;

        foreach (var l in LightLists)
        {
            if (l) l.enabled = false;
        }
        OnLight = false;
        Debug.Log("���C�g��������");

        if (Ghost) Destroy(Ghost.gameObject);               // �d�l�F�������ɃS�[�X�g�j���i�C�Ӂj

        ShowEventMessage(MsgTurnedOff);                     // �u�������悤���v

        // �i�s�x��i�߂�i��񂫂胂�[�h�l���j
        if (!CountOnlyOncePerThisLight || (CountOnlyOncePerThisLight && !_alreadyCounted))
        {
            if (HintRef && AdvanceAmountOnOff > 0)
            {
                for (int i = 0; i < AdvanceAmountOnOff; i++)
                    HintRef.AdvanceProgress();              // �i�s�x +1�i�����j
            }
            _alreadyCounted = true;                         // ��񂫂�t���O
        }
    }
    // --------------- �����܂Ŏ�����ǉ� ---------------

    // --------------- �������������ǉ��i�S���C�gON�F�i�s�x�|�j ---------------
    void On()
    {
        // �f�o�E���X�i����/�_�����ʁj ------------------------------------------------
        if (Time.time - _lastToggleTime < ToggleDebounceSeconds) return;
        _lastToggleTime = Time.time;

        foreach (var l in LightLists)
        {
            if (l) l.enabled = true;
        }
        OnLight = true;
        Debug.Log("���C�g��_����");

        ShowEventMessage(MsgTurnedOn);                      // �u�_�����悤���v

        // --------------- �������������ǉ��i�_���Ői�s�x��������j ---------------
        // �����F���O���u�����Ă����v��� �� ���_�����̂Ō���������
        if (HintRef && DecreaseAmountOnOn > 0)
        {
            for (int i = 0; i < DecreaseAmountOnOn; i++)
            {
                // SetProgress �ŉ����N�����v�����O��B����1�i��������B
                HintRef.SetProgress(HintRef.ProgressStage - 1);
            }
        }
        // �� ��񂫂萧��́u�����v�ɂ͓K�p���Ȃ��i�d�l�j�B�K�v�Ȃ瓯�l�̃t���O��ǉ����ĂˁB
        // --------------- �����܂Ŏ�����ǉ� ---------------
    }
    // --------------- �����܂Ŏ�����ǉ� ---------------

    // --------------- �������������ǉ��i���b�Z�[�W�\�����ʁj ---------------
    void ShowEventMessage(string msg)
    {
        if (!MsgText) return;
        MsgText.text = msg;
        MsgText.gameObject.SetActive(true);
        _msgTimer = Mathf.Max(0.01f, EventMsgDuration);     // �\�����ԃ��Z�b�g
    }
    // --------------- �����܂Ŏ�����ǉ� ---------------
}
