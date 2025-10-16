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

    // ---- �����C�x���g�iTutorial ����w�ǁj ----
    [Header("�����C�x���g")]
    public UnityEvent OnFirstGhostSeen;                    // ���߂āg�S�[�X�g���������h�u��
    public UnityEvent OnFirstState2Seen;                   // ���߂āgstate=2�h�������u��
    public UnityEvent<int> OnProgressChanged;              // �i�s�x���ς����

    // ---- �N���[���ǔ� ----
    [Header("�S�[�X�g�����ǔ�")]
    public bool AutoTrackNearestGhost = true;
    public string GhostTag = "Ghost";
    public float RetargetInterval = 0.3f;
    public bool AutoDeriveChaseRefFromGhost = true;
    private float _retargetTimer = 0f;
    private Transform _lastGhost;

    // ---- �\���iUI/3D ���Ή��j----
    [Header("�\��")]
    public TMP_Text[] HintLabels = new TMP_Text[5];
    public Canvas UICanvas;
    public bool ScreenSpaceUI = true;

    // ---- �i�s�Ǘ� ----
    [System.Serializable] public class HintSet { [TextArea] public string[] State1 = new string[5]; [TextArea] public string[] State2 = new string[5]; }
    public List<HintSet> Stages = new List<HintSet>();
    public int ProgressStage = 0;

    // ---- ����/�J�� ----
    [Header("�J�����[��")]
    public float VisibleDistance = 10f;
    public float RevealDistance = 7f;
    public float RevealCharsPerSecond = 6f;
    public char MaskChar = '��';

    [Header("�s�ԃN�[���^�C��")]
    public float NextHintCooldown = 1.0f;

    [Header("�����i�s�i5�s���ׂĊJ����j")]
    public bool AutoAdvanceWhenAllRevealed = true;
    public float AutoAdvanceDelay = 1.0f;
    private float _autoAdvanceTimer = -1f;

    // ---- �z�u���o ----
    [Header("���C�A�E�g�i�����O�j")]
    public float RingRadius = 1.8f;
    public float OrbitSpeed = 20f;
    public float BobAmplitude = 0.15f;
    public float BobSpeed = 2.0f;
    public float HeightOffset = 1.6f;

    // ---- ��ʂɉf���Ă��鎞�����\�� ----
    [Header("��ʓ��`�F�b�N")]
    public bool OnlyWhenGhostOnScreen = true;
    public float OnScreenMargin = 0.05f;
    public bool CheckOcclusion = false;
    public LayerMask Occluders;
    public float CameraEyeHeight = 0.0f;

    // ---- ���� ----
    private string[] activeLines = new string[5];
    private int currentIndex = 0;
    private float revealProgressChars = 0f;
    private bool waitingCooldown = false;
    private float cooldownTimer = 0f;
    private int cachedState = -1, cachedStage = -1;

    // ---- �������o�p ----
    private bool _seenAnyOnce = false;        // �����ꂩ�̏�Ԃ����߂Ď��F������
    private bool _seenState2Once = false;     // state=2 �����߂Ď��F������
    private bool _visiblePrev = false;        // �O�t���[���́u�����Ă������v

    void Start()
    {
        ProgressStage = Mathf.Max(0, ProgressStage);
        SelectLinesByStageAndState();
        ApplyMaskedAll();
        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].gameObject.SetActive(false);
    }

    void Update()
    {
        // ====== �ǔ��i���Ԋu�ōŊ����E�������j ======
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

                    // �؂�ւ����͊J�������Z�b�g
                    currentIndex = 0;
                    revealProgressChars = 0f;
                    waitingCooldown = false;
                    cooldownTimer = 0f;
                    _autoAdvanceTimer = -1f;
                    ApplyMaskedAll();
                    SelectLinesByStageAndState();
                }
            }
        }

        if (!Player || !Ghost)
        {
            // �s�݂Ȃ�UI��\��
            for (int i = 0; i < HintLabels.Length; i++)
                if (HintLabels[i]) HintLabels[i].gameObject.SetActive(false);
            // ���t���O�����Ƃ��i���������舵���j
            _visiblePrev = false;
            return;
        }

        // ====== ������i�܂��̓C�x���g�p�Ɍv�Z�j ======
        float dist = Vector3.Distance(Player.position, Ghost.position);
        bool visibleByDistance = dist <= VisibleDistance;
        bool onScreen = !OnlyWhenGhostOnScreen || IsGhostOnScreen();
        bool visible = visibleByDistance && onScreen;

        // �B��Ă���Ԃ́g�����h���Ƃɂ��Ȃ�
        bool isHiding = (HideRef && HideRef.hide);

        // �� �����オ��ifalse��true�j���g�B��Ă��Ȃ��h�����������C�x���g����
        if (visible && !_visiblePrev && !isHiding)
        {
            if (!_seenAnyOnce)
            {
                _seenAnyOnce = true;
                OnFirstGhostSeen?.Invoke();
            }

            int st = (ChaseRef ? ChaseRef.GetState() : 1);
            if (st == 2 && !_seenState2Once)
            {
                _seenState2Once = true;
                OnFirstState2Seen?.Invoke();
            }
        }
        _visiblePrev = visible;

        // ====== �����EUI�����i�\��/��\���j ======
        CheckAndMaybeAdvanceProgress();
        SelectLinesByStageAndState();

        // �\���t���O�i��������ʓ��j
        bool show = visible;
        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].gameObject.SetActive(show);
        if (!show) return;

        // ���C�A�E�g�X�V
        AnimateRingLayout();

        // ====== �����J���i�N�[���^�C���t���j ======
        if (dist <= RevealDistance && currentIndex < 5)
        {
            if (waitingCooldown)
            {
                cooldownTimer -= Time.deltaTime;
                if (cooldownTimer <= 0f) waitingCooldown = false;
            }
            else
            {
                revealProgressChars += RevealCharsPerSecond * Time.deltaTime;
                UpdateMaskedLine(currentIndex, revealProgressChars);

                if (IsFullyRevealed(activeLines[currentIndex], revealProgressChars))
                {
                    currentIndex = Mathf.Min(currentIndex + 1, 4);
                    revealProgressChars = 0f;
                    waitingCooldown = true;
                    cooldownTimer = Mathf.Max(0f, NextHintCooldown);
                }
            }
        }

        // �s�̍ŏI���f�i�J����/������/�i�s���j
        for (int i = 0; i < 5; i++)
        {
            if (!HintLabels[i]) continue;

            if (i < currentIndex) HintLabels[i].text = activeLines[i];  // ���S�J��
            else if (i == currentIndex && !waitingCooldown)
            {
                // �i�s���� UpdateMaskedLine �����f
            }
            else
            {
                HintLabels[i].text = MaskAll(activeLines[i]);            // ������ or CT��
            }
        }
    }

    // ====== �߂��S�[�X�g��T�� ======
    private Transform FindNearestGhostByTag()
    {
        if (string.IsNullOrEmpty(GhostTag) || !Player) return Ghost;

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
            if (d2 < bestSqr) { bestSqr = d2; best = t; }
        }
        return best;
    }

    // ====== ��ʓ��`�F�b�N ======
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

    // ====== �X�e�[�W����Ԃŕ�����I�� ======
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

        // �������ς�����烊�Z�b�g
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

    // ====== �i�s�i�����j ======
    private void CheckAndMaybeAdvanceProgress()
    {
        if (!AutoAdvanceWhenAllRevealed) return;
        if (!AllFiveRevealed()) { _autoAdvanceTimer = -1f; return; }

        if (_autoAdvanceTimer < 0f) _autoAdvanceTimer = AutoAdvanceDelay;
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
        OnProgressChanged?.Invoke(ProgressStage);
    }

    // ====== �\�����[�e�B���e�B ======
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

    // ====== �����O�z�u ======
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
