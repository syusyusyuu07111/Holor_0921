using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Tutorial : MonoBehaviour
{
    // ========== �e�L�X�g�^�^�C�v���o ==========
    public TextMeshProUGUI BottomText;
    public float CharsPerSecond = 40f;
    public float LineInterval = 0.6f;
    public bool HideWhenDone = true;

    [TextArea] public string[] Step1Lines = { "�c�c�����͂ǂ����낤�B", "�������܂ł̋L�����B�����B", "�Ƃɂ����A�o����T���Ȃ��ƁB" };
    [TextArea] public string[] Step3Lines = { "�c�c���������������I", "�����T���Ă݂悤�B" }; // ���I�J�n�Ɠ����ɏo��
    Coroutine _typing;

    // ========== �i�s�x�Q�� ==========
    [Header("�i�s�x�Q��")]
    public HintText HintRef;
    public bool AutoFindHintRef = true;
    public int MinProgressToEnableDoor = 1;

    // ========== OpenDoor ���� ==========
    [Header("����ΏہiOpenDoor�̂݁j")]
    public List<OpenDoor> DoorScripts = new();
    private int _lastAppliedProgress = int.MinValue;

    // ========== �h�A�F���b�N���̓��̓t�b�N�iStep2�g���K�j ==========
    [Header("�h�A�F���b�N���̓��̓t�b�N")]
    public Transform Player;
    public float DoorInteractDistance = 1.6f;
    public bool DoorRequireFacingSide = false;
    [Range(-1f, 1f)] public float DoorFacingDotThreshold = 0f;
    public string DoorLockedMessage = "�h�A�͂����Ȃ��悤���c";
    public float DoorLockedCooldown = 1.0f;
    private float _doorMsgCD = 0f;

    private InputSystem_Actions _input;

    // ========== �����p�l���i�H��j���ꎞ��~ ==========
    [Header("�����`���[�g���A���摜�i�H��j")]
    public GameObject Step4Panel_StateAny;   // ���߂ėH�삪������
    public GameObject Step5Panel_State2;     // ���߂� state=2 ������
    private bool _didStep4 = false;
    private bool _didStep5 = false;

    // ========== �����p�l���i�B���j ==========
    [Header("�B���`���[�g���A���摜")]
    public HideCroset HideRef;               // HideCroset ���A�T�C��
    public GameObject HidePanel;             // �B��`���[�g���A���摜
    private bool _didHidePanel = false;

    // ========== �p�l�����ʁF�ꎞ��~�̃Q�[�g ==========
    private bool _pauseGate = false;         // �p�l���\�����͓���/���o���~�߂������Ɏg��

    // ========== ���I�J�n�iStep3�j���� ==========
    [Header("�H��X�|�i�[�iEnemyAI�j")]
    public List<EnemyAI> Spawners = new();   // AutoStart=false �����i�ی���Start�Ŏ~�߂�j
    public float StartSpawnDelayAfterStep2 = 2f; // Step2�e�L�X�g����������̑ҋ@

    private bool _didStep2 = false;          // �h�A���b�N�����񌟒m������
    private bool _didStep3 = false;          // ���I�J�n�����s������

    // ========== ���C�t�T�C�N�� ==========
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
        _input.UI.Enable();

        // �H��E�i�s�x�̃C�x���g
        if (HintRef)
        {
            HintRef.OnFirstGhostSeen.AddListener(Step4_ShowPanel);
            HintRef.OnFirstState2Seen.AddListener(Step5_ShowPanel);
            HintRef.OnProgressChanged.AddListener(OnProgressChanged);
        }

        // �B��ē� ����\���C�x���g�iHideCroset������j
        if (HideRef) HideRef.OnFirstHidePromptShown.AddListener(ShowHidePanelOnce);
    }

    private void OnDisable()
    {
        if (HintRef)
        {
            HintRef.OnFirstGhostSeen.RemoveListener(Step4_ShowPanel);
            HintRef.OnFirstState2Seen.RemoveListener(Step5_ShowPanel);
            HintRef.OnProgressChanged.RemoveListener(OnProgressChanged);
        }
        if (HideRef) HideRef.OnFirstHidePromptShown.RemoveListener(ShowHidePanelOnce);

        _input.Player.Disable();
        _input.UI.Disable();

        if (Time.timeScale == 0f) Time.timeScale = 1f; // �O�̂��ߕ���
    }

    private void Start()
    {
        if (BottomText) { BottomText.text = ""; BottomText.gameObject.SetActive(false); }
        if (Step4Panel_StateAny) Step4Panel_StateAny.SetActive(false);
        if (Step5Panel_State2) Step5Panel_State2.SetActive(false);
        if (HidePanel) HidePanel.SetActive(false);

        // �O�̂��ߎ����J�n���~�߂�iAutoStart=false���������ی��j
        for (int i = 0; i < Spawners.Count; i++)
            if (Spawners[i]) Spawners[i].StopSpawning();

        ApplyDoorEnableByProgress(HintRef ? HintRef.ProgressStage : 0);
        Step1();
    }

    private void Update()
    {
        if (HintRef && HintRef.ProgressStage != _lastAppliedProgress)
            ApplyDoorEnableByProgress(HintRef.ProgressStage);

        if (!_pauseGate) HandleLockedDoorTapFeedback(); // �p�l�����͗}�~
    }

    // ========== Step2�F�h�A���b�N���� �� ���̌�Step3�i���I�J�n�j ==========

    private void HandleLockedDoorTapFeedback()
    {
        if (!Player) return;
        if (_doorMsgCD > 0f) { _doorMsgCD -= Time.deltaTime; return; }

        bool pressed =
            _input.Player.DoorOpen.WasPressedThisFrame() ||
            _input.Player.Interact.WasPressedThisFrame() ||
            _input.Player.Jump.WasPressedThisFrame();

        if (!pressed) return;

        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od) continue;

            // ���ɊJ������i�K�Ȃ�X���[
            if (od.enabled) continue;

            // ����
            if (Vector3.Distance(Player.position, od.transform.position) > DoorInteractDistance) continue;

            // �\���`�F�b�N
            if (DoorRequireFacingSide)
            {
                Vector3 toPlayer = (Player.position - od.transform.position).normalized;
                float dot = Vector3.Dot(od.transform.forward, toPlayer);
                if (dot < DoorFacingDotThreshold) continue;
            }

            // Step2�F���b�N�����iOneShot�j
            ShowOneShot(DoorLockedMessage);
            _doorMsgCD = DoorLockedCooldown;

            // Step3 �̗\��i���񂾂��j
            if (!_didStep2)
            {
                _didStep2 = true;
                StartCoroutine(CoAfterStep2_StartStep3());
            }
            break;
        }
    }

    private IEnumerator CoAfterStep2_StartStep3()
    {
        // �u�h�A�͂����Ȃ��悤���c�v�� OneShot ��������̂�҂iHideWhenDone=true�O��j
        while (BottomText && BottomText.gameObject.activeSelf) yield return null;

        // �����Ԃ�u��
        yield return new WaitForSeconds(StartSpawnDelayAfterStep2);

        // Step3 ���s�i���I�J�n�{�����j
        DoStep3();
    }

    public void DoStep3()
    {
        if (_didStep3) return;
        _didStep3 = true;

        // 1) ���I�J�n�iEnemyAI��BeginSpawning���Ăԁj
        for (int i = 0; i < Spawners.Count; i++)
            if (Spawners[i]) Spawners[i].BeginSpawning();

        // 2) �����𗬂��i�����������o�͎��ۂ̃X�|�[���ƃY���Ȃ��悤�A�J�n���}�����j
        if (Step3Lines != null && Step3Lines.Length > 0)
        {
            if (_typing != null) StopCoroutine(_typing);
            BottomText.gameObject.SetActive(true);
            _typing = StartCoroutine(CoTypeLines(Step3Lines));
        }
    }

    // ========== Step4�F���߂ėH�삪��ʂɉf�����istate��킸�j ==========

    public void Step4_ShowPanel()
    {
        if (_didStep4) return;
        _didStep4 = true;
        if (_pauseGate) return;
        StartCoroutine(CoShowPausePanel(Step4Panel_StateAny));
    }

    // ========== Step5�F���߂� state=2 �̗H�삪�f���� ==========

    public void Step5_ShowPanel()
    {
        if (_didStep5) return;
        _didStep5 = true;
        if (_pauseGate) return;
        StartCoroutine(CoShowPausePanel(Step5Panel_State2));
    }

    // ========== Step6�F���߂āu�B���ē��v���\�����ꂽ��p�l�� ==========

    public void ShowHidePanelOnce()
    {
        if (_didHidePanel) return;
        _didHidePanel = true;
        if (_pauseGate) return;
        StartCoroutine(CoShowPausePanel(HidePanel));
    }

    // ========== ���ʁF�p�l���\�����ꎞ��~��UI.Submit�ŕ��� ==========

    private IEnumerator CoShowPausePanel(GameObject panel)
    {
        if (!panel) yield break;

        _pauseGate = true;

        panel.SetActive(true);
        float prevScale = Time.timeScale;
        Time.timeScale = 0f;

        // 1�t���[���҂��Ă�����͑҂�
        yield return null;
        while (!_input.UI.Submit.WasPressedThisFrame())
            yield return null;

        panel.SetActive(false);
        Time.timeScale = prevScale;

        _pauseGate = false;
    }

    // ========== �h�A���� ==========
    private void ApplyDoorEnableByProgress(int progress)
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
    private void OnProgressChanged(int newProgress) => ApplyDoorEnableByProgress(newProgress);

    // ========== �^�C�v���o ==========
    public void Step1()
    {
        if (!BottomText) return;
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeLines(Step1Lines));
    }

    public void ShowOneShot(string line)
    {
        if (!BottomText || string.IsNullOrEmpty(line)) return;
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(CoTypeOneShot(line));
    }

    private IEnumerator CoTypeOneShot(string line)
    {
        yield return StartCoroutine(CoTypeOne(line));
        yield return new WaitForSeconds(LineInterval);
        BottomText.gameObject.SetActive(false);
        _typing = null;
    }

    private IEnumerator CoTypeLines(string[] lines)
    {
        for (int li = 0; li < lines.Length; li++)
        {
            yield return StartCoroutine(CoTypeOne(lines[li]));
            if (li < lines.Length - 1) yield return new WaitForSeconds(LineInterval);
        }
        if (HideWhenDone) BottomText.gameObject.SetActive(false);
        _typing = null;
    }

    private IEnumerator CoTypeOne(string text)
    {
        BottomText.text = "";
        if (CharsPerSecond <= 0f) { BottomText.text = text; yield break; }

        float interval = 1f / CharsPerSecond;
        float acc = 0f; int i = 0;
        while (i < text.Length)
        {
            // ���Ԓ�~���̓^�C�v���~�߂����̂� deltaTime ���g�p�iunscaled �ɂ���Ǝ~�܂�Ȃ��j
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
