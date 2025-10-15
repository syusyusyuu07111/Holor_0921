using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Tutorial : MonoBehaviour
{
    // ===== ��ʉ��e�L�X�g�i�^�C�v���o�j =====
    [Header("���ʃe�L�X�g")]
    public TextMeshProUGUI BottomText;
    public float CharsPerSecond = 40f;
    public float LineInterval = 0.6f;
    public bool HideWhenDone = true;

    [TextArea]
    public string[] Step1Lines = { "�c�c�����͂ǂ����낤�B", "�������܂ł̋L�����B�����B", "�Ƃɂ����A�o����T���Ȃ��ƁB" };

    // Step2�F�h�A���J���Ȃ����b�Z�[�W
    [Header("Step2�i���b�N���h�A�j")]
    public string DoorLockedMessage = "�h�A�͂����Ȃ��悤���c";
    public Transform Player;                    // �h�A�ߐڔ���p
    public List<OpenDoor> DoorScripts = new();  // �L��/������؂�ւ���ΏہiOpenDoor�R���|�[�l���g�̂݁j
    public float DoorInteractDistance = 1.6f;
    public bool DoorRequireFacingSide = false;
    [Range(-1f, 1f)] public float DoorFacingDotThreshold = 0f;
    public float DoorLockedCooldown = 1.2f;
    private float _doorMsgCD = 0f;

    // Step3�F���߂ėN�����u�Ԃ̈ꌾ�iSE�͖����Ńe�L�X�g�̂݁j
    [Header("Step3�i���N�����A�N�V�����j")]
    [TextArea]
    public string Step3Line = "�c�c���̉��́H�@�߂���T���Ă݂悤�B";
    public List<EnemyAI> EnemyControllers = new(); // ������EnemyAI��o�^
    public float EnemySpawnEnableDelay = 2.0f;     // Step2�̃e�L�X�g�������Ă��牽�b��ɒ��I�J�n
    private bool _spawnWasEnabled = false;         // ��x�������I�J�n������
    private bool _didFirstSpawnText = false;       // Step3����x����

    // Step4�F���߂ĉ�ʂɗH�삪�f������p�l���\�������Ԓ�~
    [Header("Step4�i�����`���[�g���A���摜�j")]
    public HintText HintRef;                       // Ghost���J�����ɉf�������o�������Ă���
    public bool AutoFindHintRef = true;
    public GameObject Step4Panel;                  // �\������UI
    public bool PauseTimeOnStep4 = true;           // ���Ԓ�~ON
    private bool _didStep4 = false;
    private Coroutine _step4Co;

    // �i�s�x�F0�̂Ƃ���OpenDoor�𖳌�
    [Header("�i�s�x")]
    public int MinProgressToEnableDoor = 1;
    private int _lastAppliedProgress = int.MinValue;

    // ����
    private InputSystem_Actions _input;

    void Awake()
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

    void OnEnable()
    {
        _input.Player.Enable();
        _input.UI.Enable();

        // Ghost���g�N�����u�ԁh�� Step3�e�L�X�g
        foreach (var ai in EnemyControllers)
            if (ai) ai.OnGhostSpawned.AddListener(OnFirstGhostSpawned);

        // Ghost���g��ʂɉf�����u�ԁh�� Step4�p�l��
        if (HintRef) HintRef.OnFirstGhostSeen.AddListener(Step4);

        // �i�s�x�ύX�Ńh�AON/OFF�iHintText�ɃC�x���g������z��j
        if (HintRef) HintRef.OnProgressChanged.AddListener(OnProgressChanged);
    }

    void OnDisable()
    {
        foreach (var ai in EnemyControllers)
            if (ai) ai.OnGhostSpawned.RemoveListener(OnFirstGhostSpawned);

        if (HintRef)
        {
            HintRef.OnFirstGhostSeen.RemoveListener(Step4);
            HintRef.OnProgressChanged.RemoveListener(OnProgressChanged);
        }

        _input.UI.Disable();
        _input.Player.Disable();
    }

    void Start()
    {
        // ���e�L�X�g������
        if (BottomText) { BottomText.text = ""; BottomText.gameObject.SetActive(false); }

        // Step4 UI ������\��
        if (Step4Panel) Step4Panel.SetActive(false);

        // �i�s�x�Ńh�A�L��/����
        ApplyDoorEnableByProgress(HintRef ? HintRef.ProgressStage : 0);

        // Step1�J�n
        Step1();
    }

    void Update()
    {
        // �i�s�x�|�[�����O�i�ی��j
        if (HintRef && HintRef.ProgressStage != _lastAppliedProgress)
            ApplyDoorEnableByProgress(HintRef.ProgressStage);

        // �i�s�x0�̂Ƃ��F���b�N���h�A�փC���^���N�g������Step2�e�L�X�g
        HandleLockedDoorTapFeedback();
    }

    // ====== Step1�F�����e�L�X�g ======
    public void Step1()
    {
        if (!BottomText) return;
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeLines(Step1Lines));
    }

    // ====== Step2�F�h�A���J���Ȃ����b�Z�[�W�i�\������������ �� 2�b��ɒ��I�J�n�j ======
    void HandleLockedDoorTapFeedback()
    {
        if (!Player) return;
        if (_doorMsgCD > 0f) { _doorMsgCD -= Time.deltaTime; return; }

        bool pressed =
            _input.Player.DoorOpen.WasPressedThisFrame() ||
            _input.Player.Interact.WasPressedThisFrame() ||
            _input.Player.Jump.WasPressedThisFrame();

        if (!pressed) return;

        // ������OpenDoor�ɑ΂��Ă̂ݔ���
        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od) continue;
            if (od.enabled) continue; // �J�������ԂȂ�X���[

            // ��������
            if (Vector3.Distance(Player.position, od.transform.position) > DoorInteractDistance) continue;

            // ���ʌ���Ȃ�`�F�b�N
            if (DoorRequireFacingSide)
            {
                Vector3 toPlayer = (Player.position - od.transform.position).normalized;
                float dot = Vector3.Dot(od.transform.forward, toPlayer);
                if (dot < DoorFacingDotThreshold) continue;
            }

            // Step2�e�L�X�g�\��
            ShowOneShot(DoorLockedMessage);

            // �\���I����҂��Ă��璊�I�J�n�i���J�n�̂Ƃ������j
            if (!_spawnWasEnabled) StartCoroutine(CoEnableEnemySpawningAfterStep2());
            _doorMsgCD = DoorLockedCooldown;
            break;
        }
    }

    IEnumerator CoEnableEnemySpawningAfterStep2()
    {
        // ���o���Ă���^�C�v���o�̏I����҂�
        while (_typing != null) yield return null;

        // �w��b�ҋ@�i�f�U�C���v���FStep2�e�L�X�g�������Ă���2�b�j
        yield return new WaitForSeconds(EnemySpawnEnableDelay);

        // ���I�J�n�i�`���[�g���A���̍��}�ł̂݁j
        foreach (var ai in EnemyControllers)
            if (ai && !ai.IsSpawning) ai.BeginSpawning();

        _spawnWasEnabled = true;
    }

    // ====== Step3�F���߂ėH�삪�N�����u�Ԃ̈ꌾ�iSE�����j ======
    private void OnFirstGhostSpawned()
    {
        if (_didFirstSpawnText) return;   // ��x����
        _didFirstSpawnText = true;

        if (!string.IsNullOrEmpty(Step3Line))
            ShowOneShot(Step3Line);
    }

    // ====== Step4�F���߂ĉ�ʂɗH�삪�f������摜�{���Ԓ�~�iUI.Submit�ŕ���j ======
    public void Step4()
    {
        if (_didStep4) return;
        _didStep4 = true;

        if (_step4Co != null) StopCoroutine(_step4Co);
        _step4Co = StartCoroutine(CoStep4Panel());
    }

    IEnumerator CoStep4Panel()
    {
        if (Step4Panel) Step4Panel.SetActive(true);

        float prevTimeScale = Time.timeScale;
        if (PauseTimeOnStep4) Time.timeScale = 0f;

        // UI.Submit���������܂őҋ@�i���Ԓ�~���ł�Update�͉��̂�OK�j
        while (!_input.UI.Submit.WasPressedThisFrame())
            yield return null;

        if (Step4Panel) Step4Panel.SetActive(false);
        if (PauseTimeOnStep4) Time.timeScale = prevTimeScale;

        _step4Co = null;
    }

    // ====== �i�s�x �� �h�A�̗L��/���� ======
    void OnProgressChanged(int newProgress) => ApplyDoorEnableByProgress(newProgress);

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

    // ====== �e�L�X�g���[�e�B���e�B ======
    Coroutine _typing;

    public void ShowOneShot(string line)
    {
        if (!BottomText || string.IsNullOrEmpty(line)) return;
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeOneShot(line));
    }

    IEnumerator CoTypeLines(string[] lines)
    {
        for (int li = 0; li < lines.Length; li++)
        {
            yield return StartCoroutine(CoTypeOne(lines[li]));
            if (li < lines.Length - 1) yield return new WaitForSeconds(LineInterval);
        }
        if (HideWhenDone) BottomText.gameObject.SetActive(false);
        _typing = null;
    }

    IEnumerator CoTypeOneShot(string line)
    {
        yield return StartCoroutine(CoTypeOne(line));
        yield return new WaitForSeconds(LineInterval);
        BottomText.gameObject.SetActive(false);
        _typing = null;
    }

    IEnumerator CoTypeOne(string text)
    {
        BottomText.text = "";
        if (CharsPerSecond <= 0f) { BottomText.text = text; yield break; }

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
