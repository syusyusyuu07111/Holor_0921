using System.Collections;
using TMPro;
using UnityEngine;

// �L�����N�^�[���R�ڂ̕����ɓ��������ɗH�삪�o�Ă��邩��}���ŉB���M�~�b�N�ł�============================================================================
public class HideArie : MonoBehaviour
{
    public AudioSource audioSource = null;
    public AudioClip KnockSe;
    public AudioClip KnockVoice;

    public bool Hide = false;                // �B��Ă��邩
    public GameObject HidePlace;             // �B���ꏊ
    InputSystem_Actions input;
    public float HideDistance;               // �B��锻�苗��
    public GameObject Player;                // �v���C���[
    public TextMeshProUGUI text;             // �u�B���v�K�C�_���X
    public Transform HidePosition;           // �B�ꂽ�Ƃ��Ɉړ�������ʒu

    public float AttackWaitTime = 10.0f;     // �N����A�P���ɗ���܂ł̑҂�����
    public GameObject Ghost;                 // �H��̃v���n�u
    public GameObject Door;                  // �o���ʒu�ȂǂɎg���Q��

    public float GhostSpeed = 2.0f;          // �H��̈ړ����x
    public float GhostStopDistance = 0.2f;   // �v���C���[�ɋ߂Â����������~���鋗��
    public float GhostLifetime = 10f;        // �H��̎����i�o�����炱�̕b���ŏ�����j

    // �J�����iDisplay�ؑւ͎g�킸�Aenabled �̐ؑւ̂݁j
    public Camera MainCamera;                // �ʏ펞�Ɏg���J����
    public Camera SubCamera;                 // �B��Ă���ԂɎg���J����

    // ��x�N���������x�ƋN�����Ȃ����߂̃t���O
    private bool started = false;

    // �B���O�̃v���C���[�ʒu/��]���L�^
    private Vector3 savedPlayerPos;
    private Quaternion savedPlayerRot;
    private bool savedPlayerValid = false;

    // �B��ʒu���炱�̋����𒴂��č��E(����)�ɓ������玩������
    public float AutoExitDistance = 0.6f;

    // ���������H��̎Q��
    private GameObject currentghost;

    [Header("Disappear Animation")]
    public string DisappearBoolName = "IsDisappearing";           // Animator Bool
    public string DisappearStateTag = "GhostDisappearStateTag";   // ������X�e�[�g��Tag

    //  �C���^���N�g�̘A�łŁg�����đ������h��h���N�[���_�E��
    private float interactCooldownUntil = 0f; // ���̎����܂ł͉�������𖳎�

    // ��d�����h�~�t���O
    private bool isSpawningGhost = false;

    private void Awake()
    {
        input = new InputSystem_Actions();
    }
    private void OnEnable()
    {
        input.Player.Enable();
    }

    private void Start()
    {
        if (text) text.gameObject.SetActive(false);

        if (MainCamera) { MainCamera.enabled = true; }
        if (SubCamera) { SubCamera.enabled = false; }

        var ml = MainCamera ? MainCamera.GetComponent<AudioListener>() : null;
        var sl = SubCamera ? SubCamera.GetComponent<AudioListener>() : null;
        if (ml) ml.enabled = true;
        if (sl) sl.enabled = false;
    }

    void Update()
    {
        // �B���ꏊ�ɋ߂Â�����e�L�X�g�\���^�B��鏈��
        if (Player && HidePlace)
        {
            float distance = Vector3.Distance(Player.transform.position, HidePlace.transform.position);

            if (distance < HideDistance)
            {
                if (text) text.gameObject.SetActive(true);

                // ����Ƃ��� WasPressedThisFrame() ��
                if (input.Player.Interact.WasPressedThisFrame() && !Hide)
                {
                    // �B���O�̈ʒu/��]��ۑ��i�ŏ��̈�񂾂��j
                    if (!savedPlayerValid)
                    {
                        savedPlayerPos = Player.transform.position;
                        savedPlayerRot = Player.transform.rotation;
                        savedPlayerValid = true;
                    }

                    if (HidePosition) Player.transform.position = HidePosition.position;
                    Hide = true;

                    // �B��Ă���Ԃ��K�C�_���X�e�L�X�g�͏o��������
                    if (text && !text.gameObject.activeSelf) text.gameObject.SetActive(true);

                    // �T�u�J�����֐ؑ�
                    SwitchToSubCamera();

                    // ���̏u�Ԃ��班���̊Ԃ͉����L�[�𖳎�
                    interactCooldownUntil = Time.time + 0.15f;
                }
            }
            else
            {
                if (!Hide && text) text.gameObject.SetActive(false);
            }
        }

        // �B��Ă���Œ�
        if (Hide)
        {
            if (text && !text.gameObject.activeSelf) text.gameObject.SetActive(true);

            // ������ WasPressedThisFrame() + �N�[���_�E��
            if (Time.time >= interactCooldownUntil && input.Player.Interact.WasPressedThisFrame())
            {
                ExitHide(false); // ���ύX: �ď����͂����ł͂��Ȃ��i�v���ɍ��킹�ăV���v���Ɂj
                interactCooldownUntil = Time.time + 0.15f;
            }

            // �B��ʒu���獶�E(����)�ֈ�苗���������玩������
            if (HidePosition && Player)
            {
                Vector3 p = Player.transform.position; p.y = 0f;
                Vector3 h = HidePosition.position; h.y = 0f;
                if (Vector3.Distance(p, h) > AutoExitDistance)
                {
                    ExitHide(false); // ������F�����ł��ď������Ȃ�
                    interactCooldownUntil = Time.time + 0.15f;
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.tag == "Player")
        {
            if (!started)
            {
                started = true;
                StartCoroutine(Encount());

                if (audioSource && KnockSe) audioSource.PlayOneShot(KnockSe);

                var col = GetComponent<Collider>();
                if (col) col.enabled = false;
            }
        }
    }

    IEnumerator Encount()
    {
        // ��KnockSe �Đ���A���������Ă� AttackWaitTime ��ɕK���o��������
        yield return new WaitForSeconds(AttackWaitTime);

        // �������_�ŒǐՂ��邩�����߂�i���̏u�� Hide �Ȃ�ǐՂ��Ȃ��j
        bool chaseOnSpawn = !Hide;

        // ��d�����h�~���܂ߋ��ʊ֐��Ő���
        SpawnGhostIfNeeded(true, chaseOnSpawn);
    }

    IEnumerator GhostLifetimeRoutine()
    {
        yield return new WaitForSeconds(GhostLifetime);

        if (currentghost != null)
        {
            StartDisappear(currentghost);
        }
    }

    void StartDisappear(GameObject g)
    {
        if (g == null) return;

        var anim = g.GetComponent<Animator>();
        if (anim != null && !string.IsNullOrEmpty(DisappearBoolName))
        {
            anim.SetBool(DisappearBoolName, true);
        }

        StartCoroutine(WaitDisappearAndDestroy(g, anim));
    }

    IEnumerator WaitDisappearAndDestroy(GameObject g, Animator anim)
    {
        float maxWait = 3f;

        if (anim != null && !string.IsNullOrEmpty(DisappearStateTag))
        {
            float t = 0f;
            while (t < maxWait)
            {
                var info = anim.GetCurrentAnimatorStateInfo(0);
                if (info.IsTag(DisappearStateTag))
                {
                    float waitLen = Mathf.Max(0.05f, info.length);
                    yield return new WaitForSeconds(waitLen);
                    break;
                }
                t += Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSeconds(1.0f);
        }

        if (g != null) Destroy(g);
        currentghost = null;

        OnGhostEnd(); // ���ď������Ȃ�
    }

    IEnumerator FollowGhost()
    {
        while (currentghost != null && Player != null && !Hide)
        {
            Vector3 to = Player.transform.position - currentghost.transform.position;
            float dist = to.magnitude;

            if (dist > GhostStopDistance)
            {
                Vector3 dir = to.normalized;
                currentghost.transform.position += dir * Time.deltaTime * GhostSpeed;

                if (dir.sqrMagnitude > 0.0001f)
                {
                    currentghost.transform.rotation = Quaternion.Slerp(
                        currentghost.transform.rotation,
                        Quaternion.LookRotation(dir, Vector3.up),
                        10f * Time.deltaTime
                    );
                }
            }
            yield return null;
        }

        if (currentghost == null)
        {
            OnGhostEnd(); // �ď������Ȃ�
        }
    }

    void OnGhostEnd()
    {
        ExitHide(false); // ���ȑO�̎d�l���ێ��F�H�삪�������猳�̈ʒu�����C���J�����֖߂�
    }

    void ExitHide(bool _notUsedRespawn)
    {
        Hide = false;

        if (Player && savedPlayerValid)
        {
            Player.transform.SetPositionAndRotation(savedPlayerPos, savedPlayerRot);
            savedPlayerValid = false;
        }

        SwitchToMainCamera();
    }

    // ���ύX�F�S�[�X�g�������ꌳ�Ǘ��B�������_�ŒǐՂ����邩�ichaseOnSpawn�j���w��
    void SpawnGhostIfNeeded(bool fromEncount, bool chaseOnSpawn)
    {
        if (currentghost != null) return;     // ���ɂ���
        if (isSpawningGhost) return;          // ������
        if (!Ghost || !Door) return;

        StartCoroutine(SpawnGhostRoutine(fromEncount, chaseOnSpawn));
    }

    IEnumerator SpawnGhostRoutine(bool fromEncount, bool chaseOnSpawn)
    {
        isSpawningGhost = true;               // �������t���OON
        yield return null;                    // 1�t���[���҂��ďՓ˓I�ȓ����Ăяo�������

        if (currentghost == null && Ghost && Door)
        {
            currentghost = Instantiate(Ghost, Door.transform.position, Quaternion.identity);

            if (audioSource && KnockVoice && !fromEncount)
            {
                audioSource.PlayOneShot(KnockVoice); // �����ł̍ďo���������炷���A�K�v�Ȃ�
            }

            // ��Ɏ����R���[�`���͊J�n�i�B��Ă��Ă���莞�Ԃŏ�����j
            StartCoroutine(GhostLifetimeRoutine());

            // ���ǐՂ́u���������u�ԂɉB��Ă��Ȃ������ꍇ�̂݁v�J�n
            if (chaseOnSpawn)
            {
                StartCoroutine(FollowGhost());
            }
        }

        isSpawningGhost = false;              // �������t���OOFF
    }

    void SwitchToSubCamera()
    {
        if (MainCamera)
        {
            MainCamera.enabled = false;
            var ml = MainCamera.GetComponent<AudioListener>();
            if (ml) ml.enabled = false;
        }
        if (SubCamera)
        {
            SubCamera.enabled = true;
            var sl = SubCamera.GetComponent<AudioListener>();
            if (sl) sl.enabled = true;
        }
    }
    void SwitchToMainCamera()
    {
        if (MainCamera)
        {
            MainCamera.enabled = true;
            var ml = MainCamera.GetComponent<AudioListener>();
            if (ml) ml.enabled = true;
        }
        if (SubCamera)
        {
            SubCamera.enabled = false;
            var sl = SubCamera.GetComponent<AudioListener>();
            if (sl) sl.enabled = false;
        }
    }
}
