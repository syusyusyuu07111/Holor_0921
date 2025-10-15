using UnityEngine;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Events;

public class HintText : MonoBehaviour
{
    public Transform Player;                               // �v���C���[
    public Transform Ghost;                                // �S�[�X�g���S�i�����ǔ��ŏ㏑������邱�Ƃ���j
    public SearchChase ChaseRef;                           // �S�[�X�g���(1/2)
    public HideCroset HideRef;                             // �B���ԁi�C�Ӂj

    // --------------- ��������ǉ��i�N���[���ǔ��ݒ�j ---------------
    [Header("�S�[�X�g�����ǔ�")]
    public bool AutoTrackNearestGhost = true;              // �Ŋ��̃S�[�X�g�i�N���[���܂ށj�������Œǔ�
    public string GhostTag = "Ghost";                      // �S�[�X�g�ɕt����Tag
    public float RetargetInterval = 0.3f;                  // �ǔ���̍Č����Ԋu�i�b�j
    public bool AutoDeriveChaseRefFromGhost = true;        // �ǔ���� SearchChase �������ŏE��
    private float _retargetTimer = 0f;                     // �����^�C�}�[
    private Transform _lastGhost;                          // ���O�̒ǔ��Ώ�
    // --------------- �����܂Œǉ� -----------------------------------

    // --------------- �\���iUI/3D ���Ή��j ---------------
    public TMP_Text[] HintLabels = new TMP_Text[5];        // UI�ł�3D�ł�OK
    public Canvas UICanvas;                                 // Screen Space �� Canvas
    public bool ScreenSpaceUI = true;                       // true�Ȃ�UI���W�Ŕz�u

    // --------------- �i�s�Ǘ� ---------------
    [System.Serializable] public class HintSet { [TextArea] public string[] State1 = new string[5]; [TextArea] public string[] State2 = new string[5]; }
    public List<HintSet> Stages = new List<HintSet>();     // �X�e�[�W���Ƃ̃q���g
    public int ProgressStage = 0;                           // ����0

    // �i�s�C�x���g�i�C�Ӂj
    public UnityEvent<int> OnProgressChanged;              // �������V���� ProgressStage

    // --------------- ����/�J�� ---------------
    public float VisibleDistance = 10f;                     // �����Ō����n�߂鋗��
    public float RevealDistance = 7f;                       // �J�����i�ދ���
    public float RevealCharsPerSecond = 6f;                 // �b������J��������
    public char MaskChar = '��';                             // ��������

    // --------------- �q���g�ԃN�[���^�C���i�ǉ��j ---------------
    public float NextHintCooldown = 1.0f;                   // ���̍s���J�����J�n����܂ł̑ҋ@�i�b�j

    // --------------- �����i�s�i�S���J����j ---------------
    public bool AutoAdvanceWhenAllRevealed = true;          // 5�s���ׂĊJ�����玩���Ŏ���
    public float AutoAdvanceDelay = 1.0f;                   // �S�J�����玟�֐i�ނ܂ł̑ҋ@
    private float _autoAdvanceTimer = -1f;                  // <0 �͖��J�E���g

    // --------------- �z�u���o ---------------
    public float RingRadius = 1.8f;
    public float OrbitSpeed = 20f;
    public float BobAmplitude = 0.15f;
    public float BobSpeed = 2.0f;
    public float HeightOffset = 1.6f;

    // --------------- ��ʂɉf���Ă��鎞�����\������ݒ� ---------------
    public bool OnlyWhenGhostOnScreen = true;
    public float OnScreenMargin = 0.05f;
    public bool CheckOcclusion = false;
    public LayerMask Occluders;
    public float CameraEyeHeight = 0.0f;

    // --------------- ���� ---------------
    private string[] activeLines = new string[5];
    private int currentIndex = 0;                           // ���J�����̍s
    private float revealProgressChars = 0f;                 // ���s�̊J��������
    private bool waitingCooldown = false;                   // �N�[���^�C������
    private float cooldownTimer = 0f;                       // �c��N�[���^�C��
    private int cachedState = -1, cachedStage = -1;

    void Start()
    {
        ProgressStage = Mathf.Max(0, ProgressStage);       // ������0
        SelectLinesByStageAndState();                      // �����I��
        ApplyMaskedAll();                                  // �S�����ŏ�����
        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].gameObject.SetActive(false);
    }

    void Update()
    {
        // �ǔ��F�Ŋ��̃S�[�X�g�i�N���[�����܂ށj�����I�ɑI�ђ��� --------------------
        if (AutoTrackNearestGhost)
        {
            _retargetTimer -= Time.deltaTime;
            if (_retargetTimer <= 0f)
            {
                _retargetTimer = RetargetInterval;
                var newGhost = FindNearestGhostByTag();
                if (newGhost != _lastGhost)
                {
                    Ghost = newGhost;
                    _lastGhost = newGhost;

                    if (AutoDeriveChaseRefFromGhost)
                        ChaseRef = Ghost ? Ghost.GetComponent<SearchChase>() : null;

                    // �ǔ��Ώۂ��ς������\�����Z�b�g�i���R�ɐ؂�ւ��j
                    currentIndex = 0;
                    revealProgressChars = 0f;
                    waitingCooldown = false;
                    cooldownTimer = 0f;
                    _autoAdvanceTimer = -1f;
                    ApplyMaskedAll();
                    // ���(1/2)�̕ω����f
                    SelectLinesByStageAndState();
                }
            }
        }

        if (!Player || !Ghost)
        {                           // �S�[�X�g�s�� �� ��\���ɂ��ċA��
            for (int i = 0; i < HintLabels.Length; i++)
                if (HintLabels[i]) HintLabels[i].gameObject.SetActive(false);
            return;
        }

        CheckAndMaybeAdvanceProgress();                    // �i�s����
        SelectLinesByStageAndState();                      // ���/�X�e�[�W�ω��ɒǐ�

        float dist = Vector3.Distance(Player.position, Ghost.position);
        bool visibleByDistance = dist <= VisibleDistance;
        bool visibleByCamera = !OnlyWhenGhostOnScreen || IsGhostOnScreen();
        bool show = visibleByDistance && visibleByCamera;

        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].gameObject.SetActive(show);
        if (!show) return;

        AnimateRingLayout();                               // �z�u�X�V

        // ---- �J���i�s�F�N�[���^�C�����l�� ---------------------------------------
        if (dist <= RevealDistance && currentIndex < 5)
        {
            if (waitingCooldown)
            {
                cooldownTimer -= Time.deltaTime;           // �N�[���^�C������
                if (cooldownTimer <= 0f) waitingCooldown = false; // �I���Ŏ��s�J�n
            }
            else
            {
                // �ʏ�̊J��
                revealProgressChars += RevealCharsPerSecond * Time.deltaTime;
                UpdateMaskedLine(currentIndex, revealProgressChars);

                if (IsFullyRevealed(activeLines[currentIndex], revealProgressChars))
                {
                    // �s���J���؂��� �� ���̍s�ֈڂ�O�ɃN�[���^�C��
                    currentIndex = Mathf.Min(currentIndex + 1, 4);
                    revealProgressChars = 0f;
                    waitingCooldown = true;                // �N�[���^�C���J�n
                    cooldownTimer = Mathf.Max(0f, NextHintCooldown);
                }
            }
        }

        // �s�\���̐����F�J����/������/�i�s�� ---------------------------------------
        for (int i = 0; i < 5; i++)
        {
            if (!HintLabels[i]) continue;

            if (i < currentIndex) HintLabels[i].text = activeLines[i]; // ���S�J��
            else if (i == currentIndex && !waitingCooldown) { /* UpdateMaskedLine�Ŕ��f */ }
            else HintLabels[i].text = MaskAll(activeLines[i]);         // �������CT���͑S����
        }
    }

    // --------------- ��������ǉ��i�Ŋ��S�[�X�g�����j ---------------
    private Transform FindNearestGhostByTag()
    {
        if (string.IsNullOrEmpty(GhostTag) || !Player) return Ghost;

        // Tag �őS�擾�i�񐄏��ł͂Ȃ��j
        var gos = GameObject.FindGameObjectsWithTag(GhostTag);
        if (gos == null || gos.Length == 0) return null;

        Transform best = null;
        float bestSqr = float.MaxValue;
        Vector3 p = Player.position;

        for (int i = 0; i < gos.Length; i++)
        {
            var t = gos[i]?.transform;
            if (!t) continue;
            float d2 = (t.position - p).sqrMagnitude;
            if (d2 < bestSqr)
            {
                bestSqr = d2;
                best = t;
            }
        }
        return best;
    }
    // --------------- �����܂Œǉ� -------------------------------------

    // --------------- �J�����ɉf���Ă��邩 ---------------
    private bool IsGhostOnScreen()
    {
        Camera cam = Camera.main;
        if (!cam) return true;
        Vector3 worldPos = Ghost.position + Vector3.up * HeightOffset;
        Vector3 vp = cam.WorldToViewportPoint(worldPos);
        if (vp.z <= 0f) return false;
        if (vp.x < -OnScreenMargin || vp.x > 1f + OnScreenMargin) return false;
        if (vp.y < -OnScreenMargin || vp.y > 1f + OnScreenMargin) return false;

        if (CheckOcclusion)
        {
            Vector3 camEye = cam.transform.position + Vector3.up * CameraEyeHeight;
            if (Physics.Linecast(camEye, worldPos, out RaycastHit hit, Occluders)) return false;
        }
        return true;
    }

    // --------------- �X�e�[�W����Ԃŕ�����I�� ---------------
    private void SelectLinesByStageAndState()
    {
        int state = (ChaseRef ? ChaseRef.GetState() : 1);
        if (Stages == null || Stages.Count == 0) { EnsureActiveEmpty(); return; }

        int stage = Mathf.Clamp(ProgressStage, 0, Stages.Count - 1);
        var set = Stages[stage];
        var source = (state == 2) ? set.State2 : set.State1;

        if (cachedState == state && cachedStage == stage && IsSameLines(activeLines, source)) return;

        for (int i = 0; i < 5; i++)
            activeLines[i] = (source != null && i < source.Length && !string.IsNullOrEmpty(source[i])) ? source[i] : "";

        // �������ς������ŏ�����
        currentIndex = 0;
        revealProgressChars = 0f;
        waitingCooldown = false;
        cooldownTimer = 0f;
        _autoAdvanceTimer = -1f;

        ApplyMaskedAll();

        cachedState = state;
        cachedStage = stage;
    }

    private bool IsSameLines(string[] a, string[] b)
    {
        if (a == null || b == null) return false;
        for (int i = 0; i < 5; i++)
        {
            var aa = (i < a.Length) ? a[i] : null;
            var bb = (i < b.Length) ? b[i] : null;
            if (aa != bb) return false;
        }
        return true;
    }

    private void EnsureActiveEmpty() { for (int i = 0; i < 5; i++) activeLines[i] = ""; }

    // --------------- �i�s�����i�����i�s�j ---------------
    private void CheckAndMaybeAdvanceProgress()
    {
        if (!AutoAdvanceWhenAllRevealed) return;
        if (!AllFiveRevealed()) { _autoAdvanceTimer = -1f; return; }

        if (_autoAdvanceTimer < 0f) _autoAdvanceTimer = AutoAdvanceDelay;      // �J�E���g�J�n
        else
        {
            _autoAdvanceTimer -= Time.deltaTime;
            if (_autoAdvanceTimer <= 0f)
            {
                _autoAdvanceTimer = -1f;
                AdvanceProgress();
            }
        }
    }

    public void AdvanceProgress() { SetProgress(ProgressStage + 1); }

    public void SetProgress(int next)
    {
        int clamped = Mathf.Clamp(next, 0, Mathf.Max(0, (Stages?.Count ?? 1) - 1));
        if (clamped == ProgressStage) return;
        ProgressStage = clamped;

        currentIndex = 0; revealProgressChars = 0f;
        waitingCooldown = false; cooldownTimer = 0f;
        _autoAdvanceTimer = -1f;

        SelectLinesByStageAndState();
        OnProgressChanged?.Invoke(ProgressStage);          // �O���֒ʒm�i�C�Ӂj
    }

    // ---- �\�����[�e�B���e�B ------------------------------------------------------
    private void ApplyMaskedAll()
    {
        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].text = MaskAll(i < activeLines.Length ? activeLines[i] : "");
    }
    private void UpdateMaskedLine(int index, float revealedChars)
    {
        if (index < 0 || index >= activeLines.Length) return;
        if (!HintLabels[index]) return;
        string src = activeLines[index];
        int count = Mathf.Clamp(Mathf.FloorToInt(revealedChars), 0, src.Length);
        HintLabels[index].text = RevealLeftToRight(src, count);
    }
    private string MaskAll(string s) { return string.IsNullOrEmpty(s) ? "" : new string(MaskChar, s.Length); }
    private string RevealLeftToRight(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "";
        n = Mathf.Clamp(n, 0, s.Length);
        return s.Substring(0, n) + new string(MaskChar, s.Length - n);
    }
    private bool IsFullyRevealed(string s, float revealedChars) { return Mathf.FloorToInt(revealedChars) >= (s?.Length ?? 0); }
    private bool AllFiveRevealed()
    {
        if (currentIndex < 4) return false;
        return IsFullyRevealed(activeLines[4], revealProgressChars) || string.IsNullOrEmpty(activeLines[4]);
    }

    // ---- �����O�z�u�iUI/3D �ؑցj ------------------------------------------------
    private void AnimateRingLayout()
    {
        float t = Time.time;
        Camera cam = Camera.main;

        for (int i = 0; i < HintLabels.Length; i++)
        {
            var label = HintLabels[i];
            if (!label) continue;

            float angleDeg = (360f / Mathf.Max(1, HintLabels.Length)) * i + t * OrbitSpeed;
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector3 around = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * RingRadius;
            float bob = Mathf.Sin(t * BobSpeed + i * 0.6f) * BobAmplitude;

            Vector3 worldPos = Ghost.position + around + Vector3.up * (HeightOffset + bob);

            if (ScreenSpaceUI && UICanvas)
            {
                Vector3 screen = cam ? cam.WorldToScreenPoint(worldPos) : worldPos;
                (label.transform as RectTransform).position = screen;
            }
            else
            {
                label.transform.position = worldPos;
                if (cam) label.transform.rotation = Quaternion.LookRotation(label.transform.position - cam.transform.position);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!Ghost) return;
        Gizmos.color = Color.white; Gizmos.DrawWireSphere(Ghost.position, VisibleDistance);
        Gizmos.color = Color.green; Gizmos.DrawWireSphere(Ghost.position, RevealDistance);
    }
}
