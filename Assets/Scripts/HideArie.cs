using System.Collections;
using TMPro;
using UnityEngine;

// �L�����N�^�[���R�ڂ̕����ɓ��������ɗH�삪�o�Ă��邩��}���ŉB���M�~�b�N�ł�============================================================================
public class HideArie : MonoBehaviour
{
    public AudioSource audioSource = null;
    public AudioClip KnockSe;

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

    private GameObject currentghost;         // ���������H��̎Q��
    public float GhostLifetime = 10f;        // �H��̎���

    // ���ǉ�: �J�����Q�ƁiDisplay�ؑւ͎g�킸�Aenabled�̐ؑւ̂݁j
    public Camera MainCamera;                // �ʏ펞�Ɏg���J����
    public Camera SubCamera;                 // �B��Ă���ԂɎg���J����

    // ��x�N���������x�ƋN�����Ȃ����߂̃t���O
    private bool started = false;

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

        // �N�����̃J������ԁi����Display1��ŁAenabled��ON/OFF�݂̂Őؑցj
        if (MainCamera) { MainCamera.enabled = true; }
        if (SubCamera) { SubCamera.enabled = false; }

        // AudioListener�̓�d�L�����h�~
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

                // �B���{�^������������B���
                if (input.Player.Interact.WasPerformedThisFrame())
                {
                    if (HidePosition) Player.transform.position = HidePosition.position;
                    Hide = true;
                    if (text) text.gameObject.SetActive(false);

                    // �B�ꂽ�u�ԂɃT�u�J�����֐ؑ�
                    SwitchToSubCamera();
                }
            }
            else
            {
                if (text) text.gameObject.SetActive(false);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.tag == "Player")
        {
            // ��x�����N�� �ȍ~�̓G���A���o���肵�Ă��ċN�����Ȃ�
            if (!started)
            {
                started = true;

                // �J�E���g�J�n�i���̌�̓G���A���o�Ă��~�܂�Ȃ��j
                StartCoroutine(Encount());

                if (audioSource && KnockSe) audioSource.PlayOneShot(KnockSe);

                // �ăg���K�[�h�~�̂��߁A���̃R���C�_�[�𖳌���
                var col = GetComponent<Collider>();
                if (col) col.enabled = false;
            }
        }
    }

    IEnumerator Encount()
    {
        // �N����A�w��b�҂��Ă��画��
        yield return new WaitForSeconds(AttackWaitTime);

        // �B��Ă��Ȃ��Ȃ�P���Ă���
        if (!Hide)
        {
            if (Ghost && Door)
            {
                currentghost = Instantiate(Ghost, Door.transform.position, Quaternion.identity);
                Destroy(currentghost, GhostLifetime); // 10�b��Ɏ����ŏ�����
                StartCoroutine(FollowGhost());        // �v���C���[��ǔ�
            }
        }
        else
        {
            // �B��Ă���Ƃ��͉������Ȃ��@���Ƃŏ���
        }
    }

    // �H�삪Transform�Ńv���C���[��ǂ�������
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
    }

    // �J�����ؑցiDisplay�͌Œ�Benabled ��ON/OFF�����ؑցj
    void SwitchToSubCamera()
    {
        if (MainCamera) { MainCamera.enabled = false; var ml = MainCamera.GetComponent<AudioListener>(); if (ml) ml.enabled = false; }
        if (SubCamera) { SubCamera.enabled = true; var sl = SubCamera.GetComponent<AudioListener>(); if (sl) sl.enabled = true; }
    }
    void SwitchToMainCamera()
    {
        if (MainCamera) { MainCamera.enabled = true; var ml = MainCamera.GetComponent<AudioListener>(); if (ml) ml.enabled = true; }
        if (SubCamera) { SubCamera.enabled = false; var sl = SubCamera.GetComponent<AudioListener>(); if (sl) sl.enabled = false; }
    }
}
