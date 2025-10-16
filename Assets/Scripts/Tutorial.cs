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

    // ========== �O�i�`���[�g���A���i�ړ��^���_�^�_�b�V���j ==========
    [Header("�O�i�`���[�g���A���i�ړ��^���_�^�_�b�V���j")]
    public bool EnableBasicTutorial = true;
    public Transform CameraTransform;
    public PlayerController PlayerCtrl;   // �� PlayerController �����ԎQ��

    [TextArea] public string BasicMoveText = "�ړ����Ă݂悤�iWASD / ���X�e�B�b�N�j";
    [TextArea] public string BasicLookText = "�J�����𓮂����Ă݂悤�i�}�E�X / �E�X�e�B�b�N�j";
    [TextArea] public string BasicDashText = "�V�t�g�������Ȃ���_�b�V�����Ă݂悤";
    [TextArea] public string BasicDoneText = "OK�I���������B";

    [Header("�O�i�`���[�g���A���F�������l")]
    public float BasicLookYawTotal = 20f;        // ���[�̍��v�p�x
    public float BasicLookPitchTotal = 10f;      // �s�b�`�̍��v�p�x
    public float BasicMoveMinDuration = 0.15f;   // �u�����Ă���v�p������
    public float BasicDashMinDuration = 0.15f;   // �u�_�b�V�����v�p������

    // --- �ړ��N���A�ɍ��v���������΂� ---
    public float BasicMoveTotalDistanceRequired = 1.5f; // XZ���v[m]
    public bool BasicMoveCountOnlyWhenInput = true;   // ���͂����鎞����������ώZ
    public float BasicMoveMaxStepPerFrame = 2.0f;   // �e���|���̋}�����𖳎�

    // �����i�O�i�`���[�g���A���j
    private bool _basicRunning = false;
    private bool _basicDone = false;
    private Quaternion _basicPrevCamRot;
    private float _basicAccYaw = 0f;
    private float _basicAccPitch = 0f;
    private Vector3 _basicMovePrevPos;
    private float _basicMoveTotal = 0f;

    // ========== ��������ǉ��F�h�A�p�~�b�V�����i�Ɨ��e�L�X�g�j ==========
    [Header("�h�A�p�~�b�V�����i�ʃe�L�X�gUI�j")]
    public bool EnableDoorMission = true;

    // �~�b�V������p�̓Ɨ��e�L�X�g�iBottomText�Ƃ͕ʂ�TMP���V�[���ɗp�ӂ��Ċ��蓖�āj
    public TextMeshProUGUI MissionText;
    public float MissionCharsPerSecond = 40f;
    public float MissionLineInterval = 0.4f;
    public bool MissionHideWhenDone = false;

    [TextArea] public string Mission_DoorCheck = "�h�A������ׂĂ݂悤";
    [TextArea] public string Mission_FindGhost = "���͋߂��ɂ���H��������Ă݂悤";
    [TextArea] public string Mission_HearVoiceGoNext = "���͗H��̐��𕷂��Ď��̕����ɍs����";
    [TextArea] public string Mission_AllDone = "�~�b�V��������";

    private enum DoorMissionStage { None, DoorCheck, FindGhost, HearVoiceGoNext, AllDone }
    private DoorMissionStage _doorMission = DoorMissionStage.None;
    private Coroutine _typingMission;
    private bool _heardVoice = false; // state=2 ���m�t���O
    // ========== �ǉ������܂� ==========

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

        // �O�i�`���[�g���A���J�n
        if (EnableBasicTutorial) StartCoroutine(CoRunBasicTutorial());

        // �h�A�p�~�b�V�����J�n�i�Ɨ��\���j
        if (EnableDoorMission) StartDoorMissionIfNeeded();
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

        // MissionText ������
        if (MissionText) { MissionText.text = ""; MissionText.gameObject.SetActive(false); }
    }

    private void Update()
    {
        if (HintRef && HintRef.ProgressStage != _lastAppliedProgress)
            ApplyDoorEnableByProgress(HintRef.ProgressStage);

        if (!_pauseGate) HandleLockedDoorTapFeedback(); // �p�l�����͗}�~

        // �~�b�V����3�F���𕷂�����A�L���ȃh�A�ւ̃C���^���N�g�Ŋ���
        if (EnableDoorMission && _doorMission == DoorMissionStage.HearVoiceGoNext && !_pauseGate)
        {
            TryCompleteDoorMissionByEnabledDoorInteract();
        }
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

            // ���ɊJ������i�K�Ȃ�X���[�i�� �~�b�V����3�̕ʏ����ň����j
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

            // �� �h�A�p�~�b�V�����F�X�e�[�W1�B���i���b�N���̃h�A�𒲂ׂ��j
            if (EnableDoorMission && _doorMission == DoorMissionStage.DoorCheck)
            {
                AdvanceDoorMissionTo(DoorMissionStage.FindGhost);
            }

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

    // ========== Step4/5/6�F���� UI ==========
    public void Step4_ShowPanel()
    {
        if (_didStep4) return;
        _didStep4 = true;
        if (_pauseGate) return;
        StartCoroutine(CoShowPausePanel(Step4Panel_StateAny));

        // �� �h�A�p�~�b�V�����F�X�e�[�W2�B���i�H����������j
        if (EnableDoorMission && _doorMission == DoorMissionStage.FindGhost)
            AdvanceDoorMissionTo(DoorMissionStage.HearVoiceGoNext);
    }

    public void Step5_ShowPanel()
    {
        if (_didStep5) return;
        _didStep5 = true;
        if (_pauseGate) return;
        StartCoroutine(CoShowPausePanel(Step5Panel_State2));

        // ���𕷂���
        _heardVoice = true;

        // �~�b�V����3�̕��������߂ĕ\���i���������j
        if (EnableDoorMission && _doorMission == DoorMissionStage.HearVoiceGoNext)
            ShowMissionText(Mission_HearVoiceGoNext);
    }

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

    // ========== �O�i�`���[�g���A���{�� ==========
    private IEnumerator CoRunBasicTutorial()
    {
        if (_basicRunning || _basicDone) yield break;
        if (!BottomText) yield break;

        _basicRunning = true;

        // �Q�Ƃ̏�����
        if (CameraTransform) _basicPrevCamRot = CameraTransform.rotation;

        // 1�t���[���҂��āiStart() �̏������I���̂�҂j���e�L�X�g���㏑��
        yield return null;

        // ---- �ړ����Ă݂悤 ----
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        BottomText.gameObject.SetActive(true);
        yield return StartCoroutine(CoTypeOne(BasicMoveText));

        // ���v�����g���b�L���O������
        _basicMoveTotal = 0f;
        _basicMovePrevPos = Player ? Player.position : Vector3.zero;

        float moveTimer = 0f;
        while (true)
        {
            // 1) �u�����Ă��邩�v����iPlayerController ������΂�����g�p�j
            bool moving = PlayerCtrl ? PlayerCtrl.IsMovingNow : (_input.Player.Move.ReadValue<Vector2>() != Vector2.zero);

            // 2) ���v������ώZ�iXZ�̂݁j�B�K�v�Ȃ�u���͂����鎞�����v�J�E���g
            if (Player)
            {
                Vector3 cur = Player.position;
                Vector3 delta = cur - _basicMovePrevPos; delta.y = 0f;

                float step = delta.magnitude;
                step = Mathf.Min(step, BasicMoveMaxStepPerFrame); // �e���|/�ُ�l�h�~

                if (!BasicMoveCountOnlyWhenInput || moving)
                    _basicMoveTotal += step;

                _basicMovePrevPos = cur;
            }

            // 3) �p�����ԃJ�E���g
            if (moving) moveTimer += Time.deltaTime;
            else moveTimer = 0f;

            // 4) �����݂�������N���A
            if (moveTimer >= BasicMoveMinDuration && _basicMoveTotal >= BasicMoveTotalDistanceRequired)
                break;

            yield return null;
        }

        // ---- �J�����𓮂����Ă݂悤 ----
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        BottomText.gameObject.SetActive(true);
        yield return StartCoroutine(CoTypeOne(BasicLookText));

        _basicAccYaw = 0f; _basicAccPitch = 0f;
        while (true)
        {
            if (CameraTransform)
            {
                Quaternion cur = CameraTransform.rotation;

                // �O���x�N�g�����烈�[�^�s�b�`���ߎ�
                Vector3 fPrev = _basicPrevCamRot * Vector3.forward;
                Vector3 fCur = cur * Vector3.forward;

                float yawPrev = Mathf.Atan2(fPrev.x, fPrev.z) * Mathf.Rad2Deg;
                float yawCur = Mathf.Atan2(fCur.x, fCur.z) * Mathf.Rad2Deg;
                float dyaw = Mathf.DeltaAngle(yawPrev, yawCur);
                _basicAccYaw += Mathf.Abs(dyaw);

                float pitchPrev = Mathf.Asin(Mathf.Clamp(fPrev.y, -1f, 1f)) * Mathf.Rad2Deg;
                float pitchCur = Mathf.Asin(Mathf.Clamp(fCur.y, -1f, 1f)) * Mathf.Rad2Deg;
                float dpitch = Mathf.DeltaAngle(pitchPrev, pitchCur);
                _basicAccPitch += Mathf.Abs(dpitch);

                _basicPrevCamRot = cur;

                if (_basicAccYaw >= BasicLookYawTotal && _basicAccPitch >= BasicLookPitchTotal)
                    break;
            }
            yield return null;
        }

        // ---- �_�b�V�����Ă݂悤�iPlayerController ���画��j----
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        BottomText.gameObject.SetActive(true);
        yield return StartCoroutine(CoTypeOne(BasicDashText));

        float dashTimer = 0f;
        float decayPerSec = 0.5f; // ��u�̗������݂ɗP�\
        while (true)
        {
            bool dashing = PlayerCtrl ? PlayerCtrl.IsDashingNow : false;

            if (dashing) dashTimer += Time.deltaTime;
            else dashTimer = Mathf.Max(0f, dashTimer - Time.deltaTime * decayPerSec);

            if (dashTimer >= BasicDashMinDuration) break;
            yield return null;
        }

        // ����
        if (_typing != null) { StopCoroutine(_typing); _typing = null; }
        BottomText.gameObject.SetActive(true);
        yield return StartCoroutine(CoTypeOne(BasicDoneText));
        yield return new WaitForSeconds(LineInterval);
        if (HideWhenDone) BottomText.gameObject.SetActive(false);

        _basicDone = true;
        _basicRunning = false;

        // �{�҃`���[�g���A����
        Step1();

        // �h�A�p�~�b�V���������J�n�Ȃ�J�n
        if (EnableDoorMission) StartDoorMissionIfNeeded();
    }

    // ========== ��������ǉ��F�h�A�p�~�b�V��������i�ʃe�L�X�gUI�j ==========
    // ========== ��������ǉ��F�h�A�p�~�b�V��������i�ʃe�L�X�gUI�j ==========
    // �� �����̃��\�b�h�����̓��e�ɍ����ւ�
    private void StartDoorMissionIfNeeded()
    {
        // ���łɊJ�n���Ă����牽�����Ȃ�
        if (_doorMission != DoorMissionStage.None) return;

        // �O�i�`���[�g���A�����L���ȂƂ��́A�����܂Ń~�b�V�������o���Ȃ�
        if (EnableBasicTutorial && !_basicDone) return;

        // �����ɗ������_���O�i�`���[�g���A�����I����Ă���ior �����j
        _doorMission = DoorMissionStage.DoorCheck;
        ShowMissionText(Mission_DoorCheck); // �u�h�A������ׂĂ݂悤�v
    }


    private void AdvanceDoorMissionTo(DoorMissionStage next)
    {
        _doorMission = next;
        switch (_doorMission)
        {
            case DoorMissionStage.FindGhost:
                ShowMissionText(Mission_FindGhost);
                break;
            case DoorMissionStage.HearVoiceGoNext:
                ShowMissionText(Mission_HearVoiceGoNext);
                break;
            case DoorMissionStage.AllDone:
                ShowMissionText(Mission_AllDone);
                if (MissionHideWhenDone && MissionText) StartCoroutine(CoHideMissionAfter(MissionLineInterval));
                break;
        }
    }

    private void ShowMissionText(string line)
    {
        if (!MissionText || string.IsNullOrEmpty(line)) return;

        // �~�b�V�����p�^�C�v���o�� BottomText �ƓƗ�
        if (_typingMission != null) { StopCoroutine(_typingMission); _typingMission = null; }
        MissionText.gameObject.SetActive(true);
        _typingMission = StartCoroutine(CoTypeOne_Mission(line));
    }

    private IEnumerator CoTypeOne_Mission(string text)
    {
        MissionText.text = "";
        if (MissionCharsPerSecond <= 0f) { MissionText.text = text; yield break; }

        float interval = 1f / MissionCharsPerSecond;
        float acc = 0f; int i = 0;
        while (i < text.Length)
        {
            acc += Time.deltaTime;
            while (acc >= interval && i < text.Length)
            {
                acc -= interval; i++;
                MissionText.text = text.Substring(0, i);
            }
            yield return null;
        }
    }

    private IEnumerator CoHideMissionAfter(float wait)
    {
        yield return new WaitForSeconds(wait);
        if (MissionText) MissionText.gameObject.SetActive(false);
    }

    private void TryCompleteDoorMissionByEnabledDoorInteract()
    {
        // �u���𕷂����v�K�{�ɂ������ꍇ�͈ȉ��̃K�[�h��߂�
        // if (!_heardVoice) return;

        bool pressed =
            _input.Player.DoorOpen.WasPressedThisFrame() ||
            _input.Player.Interact.WasPressedThisFrame();

        if (!pressed || !Player) return;

        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od) continue;
            if (!od.enabled) continue; // �L���ȃh�A�̂�

            // ����
            if (Vector3.Distance(Player.position, od.transform.position) > DoorInteractDistance) continue;

            // �\���`�F�b�N�i�K�v�Ȃ�j
            if (DoorRequireFacingSide)
            {
                Vector3 toPlayer = (Player.position - od.transform.position).normalized;
                float dot = Vector3.Dot(od.transform.forward, toPlayer);
                if (dot < DoorFacingDotThreshold) continue;
            }

            // �����𖞂�������~�b�V��������
            AdvanceDoorMissionTo(DoorMissionStage.AllDone);
            break;
        }
    }
    // ========== �ǉ������܂� ==========
}
