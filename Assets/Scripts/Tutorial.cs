using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Tutorial : MonoBehaviour
{
    // ===== �\��/�^�C�v�ݒ� =====
    public TextMeshProUGUI BottomText;             // ��ʉ��e�L�X�g
    public float CharsPerSecond = 40f;             // 1�b������\��������
    public float LineInterval = 0.6f;              // �s�ƍs�̊Ԃ̑ҋ@�b
    public bool HideWhenDone = true;               // ���ׂĕ\����ɔ�\��
    public bool ApplyIntervalAfterLastLine = true; // �Ō�̍s�̌�ɂ�LineInterval��K�p

    [TextArea]
    public string[] Step1Lines =
    {
        "�c�c�����͂ǂ����낤�B",
        "�������܂ł̋L�����B�����B",
        "�Ƃɂ����A�o����T���Ȃ��ƁB"
    };

    [TextArea]
    public string[] Step2Lines =
    {
        "�h�A�͂����Ȃ��悤���c"
    };

    [TextArea]
    public string[] Step3Lines =
    {
        "���������������I",
        "�����T���Ă݂悤"
    };

    // ===== �i�s�x/�h�A =====
    [Header("�i�s�x�Q��")]
    public HintText HintRef;
    public bool AutoFindHintRef = true;
    public int MinProgressToEnableDoor = 1;

    [Header("����ΏہiOpenDoor�̂݁j")]
    public List<OpenDoor> DoorScripts = new();

    // ===== ���b�N���̓��̓t�b�N =====
    [Header("�h�A�F���b�N���̓��̓t�b�N")]
    public Transform Player;
    public float DoorInteractDistance = 1.6f;
    public bool DoorRequireFacingSide = false;
    [Range(-1f, 1f)] public float DoorFacingDotThreshold = 0f;
    public float DoorLockedCooldown = 1.2f;

    // ===== �`���[�g���A���F�X�|�[������ =====
    [Header("�`���[�g���A���F�X�|�[������")]
    public List<MonoBehaviour> SpawnScripts = new(); // EnemyAI ���ienabled ON/OFF�j
    public bool AutoDisableSpawnersOnStart = true;
    public float EnableSpawnerDelayAfterStep2 = 2f;

    // --------------- �������������ǉ��i�X�|�[���C�x���g�w�ǁj ---------------
    public List<EnemyAI> Spawners = new();          // EnemyAI ���
    public bool AutoFindSpawners = true;
    // --------------- �����܂Ŏ�����ǉ� ---------------

    // ----- ���� -----
    private Coroutine _typing;
    private InputSystem_Actions _input;
    private int _lastAppliedProgress = int.MinValue;
    private float _doorMsgCD = 0f;

    private bool _isStep2Playing = false;
    private bool _spawnerUnlocked = false;
    private bool _step3Fired = false;

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

        // --------------- �������������ǉ��i�X�|�[�i�[�������W�j ---------------
        if (AutoFindSpawners && (Spawners == null || Spawners.Count == 0))
        {
#if UNITY_2023_1_OR_NEWER
            var found = Object.FindObjectsByType<EnemyAI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var found = FindObjectsOfType<EnemyAI>(true);
#endif
            if (found != null)
            {
                if (Spawners == null) Spawners = new List<EnemyAI>(found.Length);
                foreach (var sp in found) if (sp) Spawners.Add(sp);
            }
        }
        // --------------- �����܂Ŏ�����ǉ� ---------------
    }

    private void OnEnable()
    {
        _input.Player.Enable();
        if (HintRef) HintRef.OnProgressChanged.AddListener(OnProgressChanged);

        // --------------- �������������ǉ��i�X�|�[���C�x���g�w�ǁj ---------------
        SubscribeSpawnerEvents(true);
        // --------------- �����܂Ŏ�����ǉ� ---------------
    }

    private void OnDisable()
    {
        if (HintRef) HintRef.OnProgressChanged.RemoveListener(OnProgressChanged);
        _input.Player.Disable();

        // --------------- �������������ǉ��i�X�|�[���C�x���g�w�ǉ����j ---------------
        SubscribeSpawnerEvents(false);
        // --------------- �����܂Ŏ�����ǉ� ---------------
    }

    private void Start()
    {
        InitBottomTextHidden();
        ApplyDoorEnableByProgress(GetProgressSafely());

        if (AutoDisableSpawnersOnStart) SetSpawnersActive(false); // �G�͂܂��o�Ȃ�

        Step1();
    }

    private void Update()
    {
        var p = GetProgressSafely();
        if (p != _lastAppliedProgress) ApplyDoorEnableByProgress(p);

        HandleLockedDoorTapFeedback(); // �i�s0�Ńh�A��@������ Step2 �𗬂�
    }

    // ===== �i�s�x��OpenDoor.enabled =====
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
    private int GetProgressSafely() => HintRef ? HintRef.ProgressStage : 0;

    // ===== ���b�N���h�A�́g�@�����h���m �� Step2 �Đ� =====
    private void HandleLockedDoorTapFeedback()
    {
        if (!Player) return;
        if (_doorMsgCD > 0f) { _doorMsgCD -= Time.deltaTime; return; }
        if (!WasDoorPressThisFrame()) return;

        if (ExistsLockedDoorNearPlayer())
        {
            Step2();                        // ������ Step2 ���Đ�
            _doorMsgCD = DoorLockedCooldown;
        }
    }

    private bool WasDoorPressThisFrame()
    {
        return
            _input.Player.DoorOpen.WasPressedThisFrame() ||
            _input.Player.Interact.WasPressedThisFrame() ||
            _input.Player.Jump.WasPressedThisFrame();
    }

    private bool ExistsLockedDoorNearPlayer()
    {
        for (int i = 0; i < DoorScripts.Count; i++)
        {
            var od = DoorScripts[i];
            if (!od || od.enabled) continue;

            if (Vector3.Distance(Player.position, od.transform.position) > DoorInteractDistance)
                continue;

            if (DoorRequireFacingSide)
            {
                Vector3 toPlayer = (Player.position - od.transform.position).normalized;
                if (Vector3.Dot(od.transform.forward, toPlayer) < DoorFacingDotThreshold)
                    continue;
            }
            return true;
        }
        return false;
    }

    // ===== �e�L�X�gAPI =====
    public void Step1() => RestartTyping(CoTypeLines(Step1Lines));

    public void Step2()
    {
        if (_isStep2Playing) return;          // ���d�Đ��h�~
        _isStep2Playing = true;
        RestartTyping(CoTypeLines(Step2Lines));
    }

    public void Step3() => RestartTyping(CoTypeLines(Step3Lines)); // �X�|�[���ʒm�ŌĂ΂��

    public void ShowOneShot(string line)
    {
        if (!BottomText || string.IsNullOrEmpty(line)) return;
        RestartTyping(CoTypeOneShot(line));
    }

    private void InitBottomTextHidden()
    {
        if (!BottomText) return;
        BottomText.text = "";
        BottomText.gameObject.SetActive(false);
    }

    private void RestartTyping(IEnumerator routine)
    {
        if (!BottomText) return;
        if (_typing != null) StopCoroutine(_typing);
        BottomText.gameObject.SetActive(true);
        _typing = StartCoroutine(routine);
    }

    private IEnumerator CoTypeOneShot(string line)
    {
        yield return CoTypeOne(line);
        yield return new WaitForSeconds(LineInterval);
        BottomText.gameObject.SetActive(false);
        _typing = null;
    }

    private IEnumerator CoTypeLines(string[] lines)
    {
        if (!BottomText || lines == null || lines.Length == 0) yield break;

        for (int li = 0; li < lines.Length; li++)
        {
            yield return CoTypeOne(lines[li]);

            if (li < lines.Length - 1)
            {
                yield return new WaitForSeconds(LineInterval);
            }
            else if (ApplyIntervalAfterLastLine)
            {
                yield return new WaitForSeconds(LineInterval);
            }
        }

        if (HideWhenDone) BottomText.gameObject.SetActive(false);
        _typing = null;

        // Step2 ���o���؂�������F�e�L�X�g���������g���Ɓh�ɏ����҂��ăX�|�[�����
        if (_isStep2Playing && !_spawnerUnlocked)
        {
            _isStep2Playing = false;
            StartCoroutine(CoEnableSpawnersAfterDelay(EnableSpawnerDelayAfterStep2));
        }
    }

    private IEnumerator CoTypeOne(string text)
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
                acc -= interval;
                i++;
                BottomText.text = text.Substring(0, i);
            }
            yield return null;
        }
    }

    // ===== �X�|�[������ =====
    private void SetSpawnersActive(bool active)
    {
        for (int i = 0; i < SpawnScripts.Count; i++)
        {
            var s = SpawnScripts[i];
            if (s) s.enabled = active;
        }
    }

    private IEnumerator CoEnableSpawnersAfterDelay(float delay)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, delay));
        SetSpawnersActive(true);
        _spawnerUnlocked = true; // ��������iStep3 �̓X�|�[���C�x���g�Ŕ��΁j
    }

    // --------------- �������������ǉ��i�X�|�[���ʒm�w�ǁj ---------------
    private void SubscribeSpawnerEvents(bool subscribe)
    {
        if (Spawners == null) return;
        for (int i = 0; i < Spawners.Count; i++)
        {
            var sp = Spawners[i];
            if (!sp) continue;
            if (subscribe) sp.OnGhostSpawned.AddListener(OnGhostSpawned);
            else sp.OnGhostSpawned.RemoveListener(OnGhostSpawned);
        }
    }

    private void OnGhostSpawned()
    {
        if (_step3Fired) return;  // ��x����
        _step3Fired = true;
        Step3();                  // �� �H��X�|�[���̏u�Ԃ�Step3���J�n
    }
    // --------------- �����܂Ŏ�����ǉ� ---------------
}
