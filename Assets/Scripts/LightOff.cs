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
    public GameObject Ghost;                                // �g�F�����ɏ����Ώہi�C�Ӂj
    public GameObject lever;                                // �񂷃��o�[
    public float RotateLever = 30f;                         // �񂷗ʁiX�x�j

    [Header("���o�[��]�i�ǉ��j")]
    public float LeverRotateSpeed = 180f;                   // ��]���x[deg/sec]
    private bool _isLeverAnimating = false;                 // ��]���t���O

    [SerializeField] private List<Light> LightLists = new();// ����Ώۃ��C�g�Q

    // ==== �g�F�ݒ�iInspector�j ====
    [Header("���C�g�g�F�iOFF���쎞�ɓK�p�j")]
    public Color WarmLightColor = new Color(1.0f, 0.78f, 0.56f, 1f); // �f�t�H�g�g�����F�h

    // ==== �����J�����ؑ� ====
    [Header("�����p�J�����i�C�Ӂj")]
    public Camera MainCamera;                               // �ʏ�\���J�����i���ݒ�Ȃ� Camera.main�j
    public Camera ShowcaseCamera;                           // �����p�̌Œ�/���o�J����
    public float ShowcaseHoldSeconds = 0.5f;                // �F�ύX��Ɂg������h������

    InputSystem_Actions input;                              // �VInputSystem

    // ------- 2��ނ̃e�L�X�g -------
    public TextMeshProUGUI PromptText;                      // �߂Â����������o���u�L�[�ē��v
    public TextMeshProUGUI MsgText;                         // �_����/�������g�u�Ԃ����h�o�郁�b�Z�[�W

    // ------- ����/�\������ -------
    [Header("�����ݒ�")]
    public string PromptOn = "�yE�z�g�F�ɂ���";              // �_�����F�g�F�ɂ���
    public string PromptOff = "�yE�z���C�g��_����";         // �������̈ē�
    public string MsgTurnedOff = "���C�g���g�����F�ɂȂ���";
    public string MsgTurnedOn = "���C�g���_�����悤��";

    [Header("�\������")]
    public float EventMsgDuration = 5.0f;                   // ���b�Z�[�W�\���b��
    private float _msgTimer = 0f;

    // ------- �i�s�x�A�g -------
    [Header("�i�s�x�i�~�b�V�����j")]
    public HintText HintRef;
    public bool AutoFindHintRef = true;
    public int AdvanceAmountOnOff = 1;                      // �g�F���Ői��
    public int DecreaseAmountOnOn = 1;                      // �_���ŉ�����

    [Tooltip("�������C�g�ł͍ŏ��́g�g�F���h������i�s�x�ɃJ�E���g����")]
    public bool CountOnlyOncePerThisLight = false;
    private bool _alreadyCounted = false;

    [Tooltip("�g�O���̘A�łł̑��d�J�E���g�h�~�i�b�j")]
    public float ToggleDebounceSeconds = 0.25f;
    private float _lastToggleTime = -999f;

    // ------- �g��x�_������Œ�ON�h���b�N -------
    private bool _lockedOn = false;
    private bool IsLocked() => _lockedOn;

    // ------- ���o�[���̓Q�[�����Ԓ�~ -------
    private bool _pausedForLever = false;
    private float _timeScaleBeforePause = 1f;
    private void PauseGameForLever()
    {
        if (_pausedForLever) return;
        _timeScaleBeforePause = Time.timeScale;
        Time.timeScale = 0f;                                // �����Ԓ�~
        _pausedForLever = true;
    }
    private void ResumeGameIfPausedForLever()
    {
        if (!_pausedForLever) return;
        Time.timeScale = _timeScaleBeforePause;             // �����ԍĊJ
        _pausedForLever = false;
    }

    private void Awake()
    {
        input = new InputSystem_Actions();
        if (AutoFindHintRef && !HintRef)
        {
#if UNITY_2023_1_OR_NEWER
            HintRef = UnityEngine.Object.FindAnyObjectByType<HintText>(FindObjectsInactive.Include);
#else
            HintRef = FindObjectOfType<HintText>(true);
#endif
        }

        if (!MainCamera && Camera.main) MainCamera = Camera.main;
        if (ShowcaseCamera) ShowcaseCamera.enabled = false; // �����͖���
    }

    private void OnEnable()
    {
        input.Player.Enable();
        if (PromptText) { PromptText.text = ""; PromptText.gameObject.SetActive(false); }
        if (MsgText) { MsgText.text = ""; MsgText.gameObject.SetActive(false); }
    }

    private void OnDisable()
    {
        input.Player.Disable();
    }

    void Update()
    {
        if (!Player || !Light) return;

        // �ߐڃ`�F�b�N
        float distance = Vector3.Distance(Player.transform.position, Light.transform.position);
        bool inRange = (distance < PushDistance);

        // �ē��\���i���b�N/���o�[���͔�\���j
        if (PromptText) PromptText.gameObject.SetActive(inRange && !_isLeverAnimating && !IsLocked());
        if (inRange && PromptText && !IsLocked())
        {
            PromptText.text = OnLight ? PromptOn : PromptOff;
        }

        // ����
        if (inRange && !_isLeverAnimating && !IsLocked() && input.Player.Jump.triggered) // Jump=�C���^���N�g
        {
            if (OnLight) Off(); else On();
        }

        // ���b�Z�[�W����
        if (_msgTimer > 0f)
        {
            _msgTimer -= Time.deltaTime;                    // timeScale�̉e�����󂯂�
            if (_msgTimer <= 0f && MsgText)
            {
                MsgText.text = "";
                MsgText.gameObject.SetActive(false);
            }
        }
    }

    // ========= �gOFF����h���g�F�ɂ��� + �J�������o =========
    void Off()
    {
        if (IsLocked()) return;

        if (Time.time - _lastToggleTime < ToggleDebounceSeconds) return;
        _lastToggleTime = Time.time;

        // ���ԁF���������Ԏ~�߂遄���o�[���J�����ؑց��F�ς��遄���b�҂��J�����߂������Ԗ߂�
        if (lever && RotateLever != 0f)
        {
            if (!_isLeverAnimating) StartCoroutine(CoRotateLeverThenShowcaseThenWarmify());
            return;
        }

        // ���o�[�����F�J�����ؑց��F���҂��߂������Ԗ߂�
        StartCoroutine(CoOnlyShowcaseThenWarmify());
    }

    // ���o�[����F���������Ԏ~�߂遄���o�[���J�����ؑց��F���҂��J�����߂������Ԗ߂�
    private System.Collections.IEnumerator CoRotateLeverThenShowcaseThenWarmify()
    {
        _isLeverAnimating = true;
        PauseGameForLever();                                // �����Ԓ�~

        // ���o�[��]�iX�̂݁A��~���ł��i�ށj
        Transform tf = lever.transform;
        Vector3 euler = tf.localEulerAngles;
        float startX = euler.x;
        float endX = startX + RotateLever;
        float duration = Mathf.Max(0.01f, Mathf.Abs(RotateLever) / Mathf.Max(1f, LeverRotateSpeed));
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;                    // ��~���ł��i�s
            float x = Mathf.LerpAngle(startX, endX, Mathf.Clamp01(t / duration));
            euler = tf.localEulerAngles; euler.x = x;
            tf.localEulerAngles = euler;
            yield return null;
        }
        euler = tf.localEulerAngles; euler.x = endX; tf.localEulerAngles = euler;

        // �J�����ؑցi���C���������j
        SwitchToShowcaseCamera();

        // �����ɐF�ύX
        DoWarmifyInternal();

        // �g�����h�̂��߂̑ҋ@�i�����ԁj
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, ShowcaseHoldSeconds));

        // �J�����߂��i���������C���j
        SwitchBackToMainCamera();

        _isLeverAnimating = false;

        // �Ō�Ɏ��ԍĊJ
        ResumeGameIfPausedForLever();
    }

    // ���o�[�����F�J�����ؑց��F���҂��߂������Ԗ߂�
    private System.Collections.IEnumerator CoOnlyShowcaseThenWarmify()
    {
        PauseGameForLever();                                // �����Ԓ�~

        SwitchToShowcaseCamera();
        DoWarmifyInternal();
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, ShowcaseHoldSeconds));
        SwitchBackToMainCamera();

        ResumeGameIfPausedForLever();                       // �����ԍĊJ
    }

    // �g�F���ienabled�͐G�炸�F�̂݁j
    private void DoWarmifyInternal()
    {
        foreach (var l in LightLists)
        {
            if (!l) continue;
            l.color = WarmLightColor;                       // �� �g�����F��K�p
        }

        OnLight = false;                                    // UI�e�L�X�g��́gOFF���h����
        Debug.Log("���C�g��g�F�ɂ���");

        if (Ghost) Destroy(Ghost.gameObject);               // �C�ӁF�g�F�����ɃS�[�X�g�j��

        ShowEventMessage(MsgTurnedOff);

        // �i�s�x�i�K�v�Ȃ��񂫂�j
        if (!CountOnlyOncePerThisLight || (CountOnlyOncePerThisLight && !_alreadyCounted))
        {
            if (HintRef && AdvanceAmountOnOff > 0)
            {
                for (int i = 0; i < AdvanceAmountOnOff; i++)
                    HintRef.AdvanceProgress();
            }
            _alreadyCounted = true;
        }
    }

    // ========= �S���C�gON�F�i�s�x�|���Œ�ON���b�N =========
    void On()
    {
        if (IsLocked()) return;

        if (Time.time - _lastToggleTime < ToggleDebounceSeconds) return;
        _lastToggleTime = Time.time;

        foreach (var l in LightLists)
        {
            if (l) l.enabled = true;
        }
        OnLight = true;
        Debug.Log("���C�g��_����");

        ShowEventMessage(MsgTurnedOn);

        if (HintRef && DecreaseAmountOnOn > 0)
        {
            for (int i = 0; i < DecreaseAmountOnOn; i++)
                HintRef.SetProgress(HintRef.ProgressStage - 1);
        }

        _lockedOn = true;                                   // �ȍ~�͖�����
        if (PromptText) PromptText.gameObject.SetActive(false);
    }

    // ========= �J�����؂肩�� =========
    private void SwitchToShowcaseCamera()
    {
        if (!MainCamera && Camera.main) MainCamera = Camera.main;

        if (ShowcaseCamera)
        {
            if (MainCamera) MainCamera.enabled = false;
            ShowcaseCamera.enabled = true;
        }
    }

    private void SwitchBackToMainCamera()
    {
        if (ShowcaseCamera) ShowcaseCamera.enabled = false;
        if (!MainCamera && Camera.main) MainCamera = Camera.main;
        if (MainCamera) MainCamera.enabled = true;
    }

    // ========= ���b�Z�[�W�\������ =========
    void ShowEventMessage(string msg)
    {
        if (!MsgText) return;
        MsgText.text = msg;
        MsgText.gameObject.SetActive(true);
        _msgTimer = Mathf.Max(0.01f, EventMsgDuration);     // timeScale�̉e�����󂯂�
    }
}
