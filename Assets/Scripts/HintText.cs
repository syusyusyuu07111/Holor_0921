using UnityEngine;
using System.Collections.Generic;
using TMPro;                                             // TMP_Text �p
using UnityEngine.UI;

public class HintText : MonoBehaviour
{
    public Transform Player;                             // �v���C���[
    public Transform Ghost;                              // �S�[�X�g���S
    public SearchChase ChaseRef;                         // �S�[�X�g���(1/2)�Q��
    public HideCroset HideRef;                           // �B���ԁi�K�v�Ȃ�j

    // --------------- �\���iUI/3D ���Ή��j ---------------
    public TMP_Text[] HintLabels = new TMP_Text[5];      // 5�̃e�L�X�g�iUI�ł�3D�ł�OK�j
    public Canvas UICanvas;                               // Screen Space �� Canvas�iUI�g�p���j
    public bool ScreenSpaceUI = true;                     // true: Screen Space UI / false: 3D�e�L�X�g

    // --------------- �i�s�Ǘ� ---------------
    [System.Serializable]
    public class HintSet                                   // �X�e�[�W���Ƃ̕����Z�b�g
    {
        [TextArea] public string[] State1 = new string[5]; // ���1�p 5�{
        [TextArea] public string[] State2 = new string[5]; // ���2�p 5�{
    }
    public List<HintSet> Stages = new List<HintSet>();     // �i�s�i�K���Ƃ̃q���g
    public int ProgressStage = 0;                           // ����0�i�C���X�y�N�^�ŏ㏑���j

    // --------------- ����/�J�� ---------------
    public float VisibleDistance = 10f;                    // �������߂��ƕ����ŏo��
    public float RevealDistance = 7f;                      // �������߂��ƊJ�����i��
    public float RevealCharsPerSecond = 6f;                // �b������J��������
    public char MaskChar = '��';                            // ��������

    // --------------- �z�u���o ---------------
    public float RingRadius = 1.8f;                        // �S�[�X�g����̔��a
    public float OrbitSpeed = 20f;                         // ����X�s�[�h�i�x/�b�j
    public float BobAmplitude = 0.15f;                     // �c��炬
    public float BobSpeed = 2.0f;                          // �c��炬���x
    public float HeightOffset = 1.6f;                      // �x�[�X����

    // --------------- ���� ---------------
    private string[] activeLines = new string[5];          // ���݂̃X�e�[�W/��Ԃ�5�s
    private int currentIndex = 0;                          // ���J�����̍s�i0��4�j
    private float revealProgressChars = 0f;                // ���s�̊J��������
    private int cachedState = -1;                          // �O�t���[���̏�ԃL���b�V��
    private int cachedStage = -1;                          // �O�t���[���̃X�e�[�W�L���b�V��

    void Start()
    {
        // �i�s��Ԃ̏����m�F�i������0�Ɋ񂹂�j -----------------------------------
        ProgressStage = Mathf.Max(0, ProgressStage);       // ����0�Œ�
        SelectLinesByStageAndState();                      // �X�e�[�W&��Ԃ���5�s������
        ApplyMaskedAll();                                  // �S�����ŏ�����

        // �����O�͔�\�� -----------------------------------------------------------
        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].gameObject.SetActive(false);
    }

    void Update()
    {
        if (!Player || !Ghost) return;

        // �i�s��Ԃ��i�ޏ����̊m�F�i�t�b�N�^���g�͋󔒁j ---------------------------
        CheckAndMaybeAdvanceProgress();                    // �����ɐi�s�����������i���͋�j

        // ���/�X�e�[�W���ς���Ă���΃��C�����đI�� -----------------------------
        SelectLinesByStageAndState();

        float dist = Vector3.Distance(Player.position, Ghost.position);
        bool visibleNow = dist <= VisibleDistance;

        // ��/�s���ؑ� ---------------------------------------------------------
        for (int i = 0; i < HintLabels.Length; i++)
            if (HintLabels[i]) HintLabels[i].gameObject.SetActive(visibleNow);
        if (!visibleNow) return;

        // �����O�z�u�̍X�V ---------------------------------------------------------
        AnimateRingLayout();

        // �J���i�s�iRevealDistance �� & �܂�5�s�ɓ��B���Ă��Ȃ����j --------------
        if (dist <= RevealDistance && currentIndex < 5)
        {
            revealProgressChars += RevealCharsPerSecond * Time.deltaTime; // �������J��
            UpdateMaskedLine(currentIndex, revealProgressChars);

            if (IsFullyRevealed(activeLines[currentIndex], revealProgressChars))
            {
                currentIndex = Mathf.Min(currentIndex + 1, 4);            // ���̍s��
                revealProgressChars = 0f;                                  // �J�E���^���Z�b�g
            }
        }

        // ���s�̌������𐮂��� -----------------------------------------------------
        for (int i = 0; i < 5; i++)
        {
            if (!HintLabels[i]) continue;

            if (i < currentIndex) HintLabels[i].text = activeLines[i];      // ���S�J����
            else if (i == currentIndex) { /* UpdateMaskedLine�Ŕ��f�ς� */ }
            else HintLabels[i].text = MaskAll(activeLines[i]); // ������͑S����
        }
    }

    // --------------- �X�e�[�W����ԂŃq���g5�{��I�� ---------------
    private void SelectLinesByStageAndState()
    {
        int state = (ChaseRef ? ChaseRef.GetState() : 1); // 1/2�iSearchChase�̌Œ��ԁj
        if (Stages == null || Stages.Count == 0)
        {
            EnsureActiveEmpty();                          // �������ݒ�̈��S��
            return;
        }

        int stage = Mathf.Clamp(ProgressStage, 0, Stages.Count - 1);
        var set = Stages[stage];
        var source = (state == 2) ? set.State2 : set.State1;

        // �ω��������Ȃ�X�L�b�v
        if (cachedState == state && cachedStage == stage && IsSameLines(activeLines, source)) return;

        // ���C�������ւ�
        for (int i = 0; i < 5; i++)
            activeLines[i] = (source != null && i < source.Length && !string.IsNullOrEmpty(source[i])) ? source[i] : "";

        // �ύX���͍ŏ��̍s�����蒼��
        currentIndex = 0;
        revealProgressChars = 0f;
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

    private void EnsureActiveEmpty()
    {
        for (int i = 0; i < 5; i++) activeLines[i] = "";
    }

    // --------------- �i�s��ԃ`�F�b�N�i�󔒃t�b�N�j ---------------
    private void CheckAndMaybeAdvanceProgress()
    {
        // �����Ɂu�i�s��Ԃ�i�߂�����v�������i�󔒁j
        // ��F
        // if ( /* �i�s��i�߂���� */ )
        // {
        //     AdvanceProgress();                          // �X�e�[�W��1�i�߂�
        // }

        // �߂�����������Ȃ�F
        // if ( /* �߂����� */ )
        // {
        //     SetProgress(0);                             // �C�ӂ̒i�K��
        // }
    }

    public void AdvanceProgress() { SetProgress(ProgressStage + 1); } // �i�߂�
    public void SetProgress(int next)
    {
        int clamped = Mathf.Clamp(next, 0, Mathf.Max(0, (Stages?.Count ?? 1) - 1));
        if (clamped == ProgressStage) return;
        ProgressStage = clamped;
        currentIndex = 0; revealProgressChars = 0f;
        SelectLinesByStageAndState();                    // ���f
    }

    // --------------- �\�����[�e�B���e�B ---------------
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

    private string MaskAll(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string(MaskChar, s.Length);
    }

    private string RevealLeftToRight(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "";
        n = Mathf.Clamp(n, 0, s.Length);
        return s.Substring(0, n) + new string(MaskChar, s.Length - n);
    }

    private bool IsFullyRevealed(string s, float revealedChars)
    {
        return Mathf.FloorToInt(revealedChars) >= (s?.Length ?? 0);
    }

    // --------------- �����O�z�u�iUI/3D �ؑցj ---------------
    private void AnimateRingLayout()
    {
        float t = Time.time;
        Camera cam = Camera.main;

        for (int i = 0; i < HintLabels.Length; i++)
        {
            var label = HintLabels[i];
            if (!label) continue;

            float angleDeg = (360f / Mathf.Max(1, HintLabels.Length)) * i + t * OrbitSpeed; // ����
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector3 around = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * RingRadius;
            float bob = Mathf.Sin(t * BobSpeed + i * 0.6f) * BobAmplitude;

            Vector3 worldPos = Ghost.position + around + Vector3.up * (HeightOffset + bob);

            if (ScreenSpaceUI && UICanvas)                      // Screen Space UI
            {
                Vector3 screen = cam ? cam.WorldToScreenPoint(worldPos) : worldPos; // ���[���h���X�N���[��
                (label.transform as RectTransform).position = screen;               // ��ʍ��W�ɔz�u
            }
            else                                                // 3D TextMeshPro
            {
                label.transform.position = worldPos;           // ���[���h���W�ɔz�u
                if (cam) label.transform.rotation = Quaternion.LookRotation(label.transform.position - cam.transform.position); // �J�����֖ʌ���
            }
        }
    }

    // --------------- �f�o�b�OGizmos ---------------
    private void OnDrawGizmosSelected()
    {
        if (!Ghost) return;
        Gizmos.color = Color.white; Gizmos.DrawWireSphere(Ghost.position, VisibleDistance); // �����ŏo������
        Gizmos.color = Color.green; Gizmos.DrawWireSphere(Ghost.position, RevealDistance);  // �J���i�s����
    }
}
