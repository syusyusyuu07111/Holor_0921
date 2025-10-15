using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Tutorial : MonoBehaviour
{
    // ===== �e�L�X�g�^�^�C�v���o =====
    [Header("����UI")]
    public TextMeshProUGUI BottomText;
    public float CharsPerSecond = 40f;
    public float LineInterval = 0.6f;            // �s�Ԃ̑ҋ@
    public bool HideWhenDone = true;             // �S����Ɏ����Ŕ�\��

    [TextArea]
    public string[] Step1Lines = {
        "�c�c�����͂ǂ����낤�B", "�������܂ł̋L�����B�����B", "�Ƃɂ����A�o����T���Ȃ��ƁB"
    };

    [Header("Step2�F���b�N���h�A��@�������̃��b�Z�[�W")]
    public string DoorLockedMessage = "�h�A�͂����Ȃ��悤���c";
    public float DoorLockedCooldown = 1.2f;      // �A�ŃK�[�h
    public float AfterDoorMsgDelay = 2.0f;       // Step2�̃e�L�X�g���������g��h�̒ǉ��ҋ@

    [Header("Step3�F�o����̃��b�Z�[�W")]
    [TextArea]
    public string[] Step3Lines = {
        "�c�c���������������I", "�����T���Ă݂悤�B"
    };

    Coroutine _typing;                           // �����Ă���^�C�v�R���[�`��
    float _doorMsgCD = 0f;                       // �A�ŃK�[�h

    // ===== �i�s�x�Q�� =====
    [Header("�i�s�x�Q��")]
    public HintText HintRef;
    public bool AutoFindHintRef = true;
    public int MinProgressToEnableDoor = 1;      // ���ꖢ���ł� OpenDoor �𖳌���

    // ===== OpenDoor ����i�X�N���v�g�����L��/�����j =====
    [Header("OpenDoor�i�R���|�[�l���g�̂ݐؑցj")]
    public List<OpenDoor> DoorScripts = new();
    private int _lastAppliedProgress = int.MinValue;

    // ===== ���͂ƃh�A�@�����m =====
    [Header("�h�A���͌��m")]
    public Transform Player;
    public float DoorInteractDistance = 1.6f;
    public bool DoorRequireFacingSide = false;
    [Range(-1f, 1f)] public float DoorFacingDotThreshold = 0f;

    private InputSystem_Actions _input;

    // ===== �X�|�i�[�i�`���[�g���A�����甭�΁j =====
    [Header("�G�X�|�i�[����")]
    public EnemyAI Spawner;                       // AutoStart=false �ɂ��Ă���
    public bool StartSpawnLoopAfterFirst = true;  // �ŏ��̊m��N����ɒ��I�J�n����

    // ===== Step4�F���߂ėH�삪��ʂɉf������摜��\���i�C�Ӂj =====
    [Header("Step4�i�����`���[�g���A���摜�j")]
    public GameObject Step4Panel;                 // �\���������p�l���i�C�Ӂj
    public bool Step4AutoHide = true;
    public float Step4VisibleSeconds = 3f;
    private bool _didStep4 = false;
    private Coroutine _step4Co;

    // ===== ������� =====
    private bool _didStep2Flow = false;           // Step2��Step3 �̈�A�͈�x����

    private void Awake()
    {
        if (!HintRef && AutoFindHintRef)
        {
#if UNITY_2023_1_OR_NEWER
            HintRef = Object.FindAnyObjectByType<HintText>(FindObjectsInactive.Include);
#else
            HintRef = FindObjectOfType<HintText>(true);
#endif
        }
        _input = new InputSystem_Actions();
    }

    private void OnEnable()
    {
        _input.Player.Enable();

        // �i�s�x�C�x���g
        if (HintRef) HintRef.OnProgressChanged.AddListener(OnProgressChanged);

        // Step4�F���߂ăJ�����ɉf�������}�iHintText ���ɃC�x���g������ꍇ�j
        if (HintRef && HintRef.OnFirstGhostSeen != null)
            HintRef.OnFirstGhostSeen.AddListener(Step4);
    }

    private void OnDisable()
    {
        if (HintRef) HintRef.OnProgressChanged.RemoveListener(OnProgressChanged);
        if (HintRef && HintRef.OnFirstGhostSeen != null)
            HintRef.OnFirstGhostSeen.RemoveListener(Step4);
        _input.Player.Disable();
    }

    private void Start()
    {
        if (BottomText) { BottomText.text = ""; BottomText.gameObject.SetActive(false); }
        if (Step4Panel) Step4Panel.SetActive(false);

        // �i�s�x�ɉ����ăh�A�̃R���|�[�l���g��ؑ�
        ApplyDoorEnableByProgress(HintRef ? HintRef.ProgressStage : 0);

        // Step1 �J�n
        Step1();
    }

    private void Update()
    {
        if (HintRef && HintRef.ProgressStage != _lastAppliedProgress)
            ApplyDoorEnableByProgress(HintRef.ProgressStage);

        HandleLockedDoorTapFeedback();   // Step2 �̓���
        if (_doorMsgCD > 0f) _doorMsgCD -= Time.deltaTime;
    }

    // =========================================================
    // Step1
    // =========================================================
    public void Step1()
    {
        if (!BottomText) return;
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeLines(Step1Lines));
    }

    // =========================================================
    // Step2�F���b�N���h�A��@�����u�Ԃɕ\�� �� �������̂�҂��� 2 �b��ɃX�|�[���m��
    // =========================================================
    void HandleLockedDoorTapFeedback()
    {
        if (!Player) return;
        if (_didStep2Flow) return; // ��x������OK
        if (_doorMsgCD > 0f) return;

        bool pressed =
            _input.Player.DoorOpen.WasPressedThisFrame() ||
            _input.Player.Interact.WasPressedThisFrame() ||
            _input.Player.Jump.WasPressedThisFrame();

        if (!pressed) return;

        // �߂� & OpenDoor �������ȃh�A�����邩
        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od) continue;

            if (od.enabled) continue; // �J�������ԂȂ�`���[�g���A���g���K�[����Ȃ�

            if (Vector3.Distance(Player.position, od.transform.position) > DoorInteractDistance)
                continue;

            if (DoorRequireFacingSide)
            {
                Vector3 toPlayer = (Player.position - od.transform.position).normalized;
                float dot = Vector3.Dot(od.transform.forward, toPlayer);
                if (dot < DoorFacingDotThreshold) continue;
            }

            // ������ Step2 ���J�n
            _didStep2Flow = true;
            StartCoroutine(CoStep2ThenSpawn());  // ���b�Z�[�W��������܂ő҂�2�b���X�|�[��
            break;
        }
    }

    IEnumerator CoStep2ThenSpawn()
    {
        // �u�h�A�͂����Ȃ��悤���c�v�������V���b�g��
        yield return StartCoroutine(CoTypeOneShot(DoorLockedMessage));

        // �g��������h����ɑҋ@
        yield return new WaitForSeconds(AfterDoorMsgDelay);

        // �X�|�[���m��i���I�X�L�b�v�j
        if (Spawner) Spawner.ForceSpawnOnce();

        // Step3 �e�L�X�g
        if (Step3Lines != null && Step3Lines.Length > 0)
        {
            if (_typing != null) StopCoroutine(_typing);
            BottomText.gameObject.SetActive(true);
            _typing = StartCoroutine(CoTypeLines(Step3Lines));
        }

        // �ȍ~�͒��I���[�v�J�n�i�C�Ӂj
        if (Spawner && StartSpawnLoopAfterFirst) Spawner.BeginSpawning();
    }

    // =========================================================
    // Step4�F���߂ėH�삪��ʂɉf������摜���o���iHintText ���̃C�x���g�𗘗p�j
    // =========================================================
    public void Step4()
    {
        if (_didStep4) return;
        _didStep4 = true;
        if (_step4Co != null) StopCoroutine(_step4Co);
        _step4Co = StartCoroutine(CoStep4Show());
    }

    IEnumerator CoStep4Show()
    {
        if (Step4Panel) Step4Panel.SetActive(true);

        if (Step4AutoHide && Step4VisibleSeconds > 0f)
        {
            yield return new WaitForSeconds(Step4VisibleSeconds);
            if (Step4Panel) Step4Panel.SetActive(false);
        }
        _step4Co = null;
    }

    // =========================================================
    // �i�s�x�ɉ����� OpenDoor �R���|�[�l���g�̗L����
    // =========================================================
    void ApplyDoorEnableByProgress(int progress)
    {
        _lastAppliedProgress = progress;
        bool enableDoor = progress >= MinProgressToEnableDoor;

        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od) continue;
            if (od.enabled != enableDoor) od.enabled = enableDoor;
        }
    }

    void OnProgressChanged(int newProgress) => ApplyDoorEnableByProgress(newProgress);

    // =========================================================
    // �^�C�v���o���[�e�B���e�B
    // =========================================================
    public void ShowOneShot(string line)
    {
        if (!BottomText || string.IsNullOrEmpty(line)) return;
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeOneShot(line));
    }

    IEnumerator CoTypeOneShot(string line)
    {
        yield return StartCoroutine(CoTypeOne(line));       // 1��������
        yield return new WaitForSeconds(LineInterval);      // �I����ɂ��C���^�[�o����K�p
        BottomText.gameObject.SetActive(false);
        _typing = null;
    }

    IEnumerator CoTypeLines(string[] lines)
    {
        if (lines == null || lines.Length == 0)
        {
            BottomText.gameObject.SetActive(false);
            _typing = null;
            yield break;
        }

        for (int li = 0; li < lines.Length; li++)
        {
            yield return StartCoroutine(CoTypeOne(lines[li]));
            // �e�s�̂��Ƃɂ��C���^�[�o����K�������
            yield return new WaitForSeconds(LineInterval);
        }

        if (HideWhenDone) BottomText.gameObject.SetActive(false);
        _typing = null;
    }

    IEnumerator CoTypeOne(string text)
    {
        if (!BottomText) yield break;

        BottomText.text = "";
        BottomText.gameObject.SetActive(true);

        if (CharsPerSecond <= 0f)
        {
            BottomText.text = text;
            yield break;
        }

        float interval = 1f / CharsPerSecond;
        float acc = 0f; int i = 0;

        while (i < text.Length)
        {
            acc += Time.deltaTime;
            while (acc >= interval && i < text.Length)
            {
                acc -= interval; i++;
                BottomText.text = text.Substring(0, i);
            }
            yield return null;
        }
    }
}
