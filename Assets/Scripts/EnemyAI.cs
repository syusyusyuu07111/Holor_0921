using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    bool GhostSpawn = false;
    public Transform Player;
    public GameObject Ghost;
    public Vector3 GhostPosition;
    public int GhostEncountChance;

    //=== �ǉ��p�����[�^�i�ŏ����j========================================================
    // �X�|�[�������i�v���C���[����̍ŏ�/�ő勗���j
    public float MinSpawnDistance = 10f;
    public float MaxSpawnDistance = 50f;
    // ���E���ɓˑR�N�����Ȃ����߂̊p�x�i�J�������� ���x�ȓ��͔�����j
    public float MinAngleFromCameraForward = 50f;
    // �������ǂȂǂŎՂ��Ă���ꏊ��D�悵�����ꍇ�̃��C���[�i�C�Ӂj
    public LayerMask LineOfSightBlockers;
    // ���C���J�����Q�Ɓi���w��Ȃ玩���擾�j
    public Camera MainCam;

    //�I�[�f�B�I�n=========================================================================================
    public AudioClip SpawnSE;
    AudioSource audioSource;

    void Start()
    {
        StartCoroutine("Spawn");
        audioSource = GetComponent<AudioSource>();
        if (MainCam == null) MainCam = Camera.main; // �ǉ��F���ݒ莞�͎����擾
    }

    private void Update()
    {
        //=== �ύX�_�F�����Œ����߁A�v���C���[���͂̃����_���ʒu�𐶐� ===============================
        // �E���������̃����_���iinsideUnitCircle�j��������Min~Max�Ń����_��
        // �E�J�������ʂɋ߂���������͔�����i�t�F�A�l�X�j
        // �E�i�C�Ӂj�v���C���[�Ƃ̊ԂɎՕ���������Ƃ��D��
        Vector3 candidate = Player.position;
        bool decided = false;

        // �ߓx�ɏd�����Ȃ����ߎ��s�񐔂ɏ��
        for (int i = 0; i < 12 && !decided; i++)
        {
            Vector2 dir2 = Random.insideUnitCircle.normalized;
            Vector3 dir = new Vector3(dir2.x, 0f, dir2.y);

            // �J�����p�x�`�F�b�N�i�J�����������ꍇ�̓X�L�b�v�j
            if (MainCam != null)
            {
                float angle = Vector3.Angle(MainCam.transform.forward, dir);
                if (angle < MinAngleFromCameraForward) continue; // ���E�ɋ߂���������͔�����
            }

            float dist = Random.Range(MinSpawnDistance, MaxSpawnDistance);
            candidate = Player.position + dir * dist;

            // �����Ւf�i�C�Ӂj�F�v���C���[�����̊ԂɃu���b�J�[�����邩
            bool blocked = false;
            if (LineOfSightBlockers.value != 0)
            {
                blocked = Physics.Linecast(
                    Player.position + Vector3.up * 1.6f,
                    candidate + Vector3.up * 1.6f,
                    LineOfSightBlockers
                );
            }

            // �u���b�J�[�w��Ȃ������̂܂܍̗p / �w�肠�聨�Ղ��Ă����D��
            if (LineOfSightBlockers.value == 0 || blocked)
            {
                decided = true;
            }
        }

        GhostPosition = decided ? candidate : // �����𖞂�����₪��������
                                              // �t�H�[���o�b�N�F���̎d�l�ɋ߂��`�i�������������o��j
            new Vector3(
                Player.transform.position.x + Random.Range(-MaxSpawnDistance, MaxSpawnDistance),
                Player.transform.position.y,
                Player.transform.position.z + Random.Range(-MaxSpawnDistance, MaxSpawnDistance)
            );
    }

    IEnumerator Spawn()
    {
        //���I��������܂ł͒��I��������==========================================================
        while (GhostSpawn == false)
        {
            //=== ���̂܂܁F�����̒��I�����ێ��i�����͂��₷���悤�R�����g�̂݁j===================
            // 0~49 �̗����� 31~49 �������� �� ��38%/��B5�b���Ƃɒ��I�B
            GhostEncountChance = Random.Range(0, 50);
            if (GhostEncountChance > 30)
            {
                GhostSpawn = true;
            }
            yield return new WaitForSeconds(5.0f);
        }

        //���I�������������̏���=================================================================
        if (GhostSpawn == true)
        {
            //=== �ǉ��F�O���t�b�N�i�K�v�Ȃ���΍폜OK�j=========================================
            // �����Ń��C�g�_�ŁE�����E���������Ȃǂ��ĂԂƃt�F�A�ɂȂ�
            // e.g., EffectsManager.Instance.Foreshadow(GhostPosition, 2.0f);
            yield return new WaitForSeconds(0.25f); // �ق�̏������߁i�������j

            audioSource.PlayOneShot(SpawnSE);
            Instantiate(Ghost, GhostPosition, Quaternion.identity);

            GhostSpawn = false;
            StartCoroutine("Spawn"); // �����d�l�̂܂܍ĊJ
        }
    }
}
