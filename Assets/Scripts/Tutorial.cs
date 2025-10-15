using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Tutorial : MonoBehaviour
{
    // ===== �e�L�X�g�^�^�C�v���o =====
    public TextMeshProUGUI BottomText;
    public float CharsPerSecond = 40f;
    public float LineInterval = 0.6f;
    public bool HideWhenDone = true;

    [TextArea] public string[] Step1Lines = { "�c�c�����͂ǂ����낤�B", "�������܂ł̋L�����B�����B", "�Ƃɂ����A�o����T���Ȃ��ƁB" };
    [TextArea] public string[] Step3Lines = { "�c�c���������������I", "�����T���Ă݂悤�B" };

    Coroutine _typing;

    // ===== �i�s�x�Q�� =====
    [Header("�i�s�x�Q��")]
    public HintText HintRef;
    public bool AutoFindHintRef = true;
    public int MinProgressToEnableDoor = 1;

    // ===== OpenDoor ���� =====
    [Header("����ΏہiOpenDoor�̂݁j")]
    public List<OpenDoor> DoorScripts = new();
    private int _lastAppliedProgress = int.MinValue;

    // ===== �h�A�F���b�N���̓��̓t�b�N�iStep2�g���K�j =====
    [Header("�h�A�F���b�N���̓��̓t�b�N")]
    public Transform Player;
    public float DoorInteractDistance = 1.6f;
    public bool DoorRequireFacingSide = false;
    [Range(-1f, 1f)] public float DoorFacingDotThreshold = 0f;
    public string DoorLockedMessage = "�h�A�͂����Ȃ��悤���c";
    public float DoorLockedCooldown = 1.2f;
    private float _doorMsgCD = 0f;

    private InputSystem_Actions _input;

    // ===== �����p�l���iStep4/Step5�j���ꎞ��~ =====
    [Header("�����`���[�g���A���摜")]
    public GameObject Step4Panel_StateAny;   // ���߂ėH�삪������
    public GameObject Step5Panel_State2;     // ���߂�state=2������

    private bool _didStep4 = false;
    private bool _didStep5 = false;
    private bool _pauseGate = false;         // �p�l���\�����̗}�~

    // ===== ���I�J�n�iStep3�j���� =====
    [Header("�H��X�|�i�[�iEnemyAI�j")]
    public List<EnemyAI> Spawners = new();   // AutoStart=false �ɂ��Ă���
    public float StartSpawnDelayAfterStep2 = 2f; // Step2�e�L�X�g����������̑ҋ@

    private bool _didStep2 = false;          // �h�A���b�N�����񌟒m������
    private bool _didStep3 = false;          // ���I�J�n�����s������

    // === Step3 �m�C�YSE�i�e�L�X�g���g��h�ɖ炷���߂̐�pSE�j ===
    [Header("Step3 �m�C�YSE�i�e�L�X�g����ɖ炷�j")]
    public bool PlayNoiseOnStep3 = true;
    public AudioClip Step3NoiseSE;
    public float Step3NoiseVolume = 1.0f;
    public Vector2 Step3NoisePitchRange = new Vector2(0.95f, 1.05f);
    public Transform NoiseAt;                    // null�Ȃ� Player �̈ʒu
    public float Step3TextDelayAfterSE = 0.15f;  // SE ����ɏ����Ԃ�u���Ă���e�L�X�g�\��

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

        if (HintRef)
        {
            HintRef.OnFirstGhostSeen.AddListener(Step4_ShowPanel);
            HintRef.OnFirstState2Seen.AddListener(Step5_ShowPanel);
            HintRef.OnProgressChanged.AddListener(OnProgressChanged);
        }
    }

    private void OnDisable()
    {
        if (HintRef)
        {
            HintRef.OnFirstGhostSeen.RemoveListener(Step4_ShowPanel);
            HintRef.OnFirstState2Seen.RemoveListener(Step5_ShowPanel);
            HintRef.OnProgressChanged.RemoveListener(OnProgressChanged);
        }
        _input.Player.Disable();
        _input.UI.Disable();

        if (Time.timeScale == 0f) Time.timeScale = 1f; // �O�̂��ߕ���
    }

    private void Start()
    {
        if (BottomText) { BottomText.text = ""; BottomText.gameObject.SetActive(false); }
        if (Step4Panel_StateAny) Step4Panel_StateAny.SetActive(false);
        if (Step5Panel_State2) Step5Panel_State2.SetActive(false);

        // �O�̂��߃X�|�i�[�����J�n�͎~�߂�iAutoStart=false ���������A�ی��Ŏ~�߂�j
        for (int i = 0; i < Spawners.Count; i++)
            if (Spawners[i]) Spawners[i].StopSpawning();

        ApplyDoorEnableByProgress(HintRef ? HintRef.ProgressStage : 0);
        Step1();
    }

    private void Update()
    {
        if (HintRef && HintRef.ProgressStage != _lastAppliedProgress)
            ApplyDoorEnableByProgress(HintRef.ProgressStage);

        if (!_pauseGate) HandleLockedDoorTapFeedback(); // �|�[�Y���͗}�~
    }

    // ====== Step2�F�h�A���b�N���� �� ���̌� Step3 ���N�� ======
    void HandleLockedDoorTapFeedback()
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
            if (od.enabled) continue; // ���łɊJ������i�K�Ȃ�X���[
            if (Vector3.Distance(Player.position, od.transform.position) > DoorInteractDistance) continue;

            if (DoorRequireFacingSide)
            {
                Vector3 toPlayer = (Player.position - od.transform.position).normalized;
                float dot = Vector3.Dot(od.transform.forward, toPlayer);
                if (dot < DoorFacingDotThreshold) continue;
            }

            // Step2�F���b�N����
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

    IEnumerator CoAfterStep2_StartStep3()
    {
        // �\����������̂� UI ��Ԃő҂��A�^�C�v�I�[�ő҂������S
        while (_typing != null) yield return null;

        // ����ɏ����Ԃ�u��
        yield return new WaitForSeconds(StartSpawnDelayAfterStep2);

        // Step3 ���s�i���I�J�n�{�g�܂�SE�h�A���̌�e�L�X�g�j
        DoStep3();
    }

    public void DoStep3()
    {
        if (_didStep3) return;
        _didStep3 = true;
        StartCoroutine(CoDoStep3Sequence());
    }

    IEnumerator CoDoStep3Sequence()
    {
        // 1) ���I�J�n�iEnemyAI ���� SpawnLoop ���X�^�[�g�j
        for (int i = 0; i < Spawners.Count; i++)
            if (Spawners[i]) Spawners[i].BeginSpawning();

        // 2) �܂��m�C�YSE��炷�i�e�L�X�g����j
        if (PlayNoiseOnStep3 && Step3NoiseSE)
        {
            var at = NoiseAt ? NoiseAt.position : (Player ? Player.position : Vector3.zero);
            float pitch = Random.Range(Step3NoisePitchRange.x, Step3NoisePitchRange.y);
            PlayClipAtPointPitch(Step3NoiseSE, at, Step3NoiseVolume, pitch);
        }

        // 3) �����Ԃ�u���Ă���e�L�X�g
        if (Step3TextDelayAfterSE > 0f)
            yield return new WaitForSeconds(Step3TextDelayAfterSE);

        if (Step3Lines != null && Step3Lines.Length > 0 && BottomText)
        {
            if (_typing != null) StopCoroutine(_typing);
            BottomText.gameObject.SetActive(true);
            _typing = StartCoroutine(CoTypeLines(Step3Lines));
        }
    }

    // ===== Step4�F���߂ėH�삪��ʂɉf���� =====
    public void Step4_ShowPanel()
    {
        if (_didStep4) return;
        _didStep4 = true;
        if (_pauseGate) return;
        StartCoroutine(CoShowPausePanel(Step4Panel_StateAny));
    }

    // ===== Step5�F���߂� state=2 �̗H�삪�f���� =====
    public void Step5_ShowPanel()
    {
        if (_didStep5) return;
        _didStep5 = true;
        if (_pauseGate) return;
        StartCoroutine(CoShowPausePanel(Step5Panel_State2));
    }

    // ===== ���ʁF�p�l���\�����ꎞ��~��UI.Submit�ŕ��� =====
    IEnumerator CoShowPausePanel(GameObject panel)
    {
        if (!panel) yield break;

        _pauseGate = true;

        panel.SetActive(true);
        float prevScale = Time.timeScale;
        Time.timeScale = 0f;

        yield return null; // 1�t���[���u��
        while (!_input.UI.Submit.WasPressedThisFrame())
            yield return null;

        panel.SetActive(false);
        Time.timeScale = prevScale;

        _pauseGate = false;
    }

    // ===== �h�A���� =====
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

    // ===== �^�C�v���o =====
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

    IEnumerator CoTypeOneShot(string line)
    {
        yield return StartCoroutine(CoTypeOne(line));
        yield return new WaitForSeconds(LineInterval);
        BottomText.gameObject.SetActive(false);
        _typing = null;
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

    IEnumerator CoTypeOne(string text)
    {
        BottomText.text = "";
        if (CharsPerSecond <= 0f) { BottomText.text = text; yield break; }

        float interval = 1f / CharsPerSecond;
        float acc = 0f; int i = 0;
        while (i < text.Length)
        {
            // �|�[�Y���̃^�C�v���~�߂����Ȃ� deltaTime�A�i�߂����Ȃ� unscaledDeltaTime
            acc += Time.deltaTime;
            while (acc >= interval && i < text.Length)
            {
                acc -= interval; i++;
                BottomText.text = text.Substring(0, i);
            }
            yield return null;
        }
    }

    // ===== �����[�e�B���e�B�F�s�b�`�t�� PlayClipAtPoint =====
    void PlayClipAtPointPitch(AudioClip clip, Vector3 pos, float vol, float pitch)
    {
        // �����V���b�g��p�̈ꎞAudioSource�𐶐����Ă����j��
        GameObject go = new GameObject("OneShotSE_Tutorial");
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.spatialBlend = 1f;
        src.volume = Mathf.Clamp01(vol);
        src.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
        src.PlayOneShot(clip, src.volume);
        Destroy(go, clip.length / Mathf.Max(0.1f, src.pitch));
    }
}
