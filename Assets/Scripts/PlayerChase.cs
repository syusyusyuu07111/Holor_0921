using UnityEngine;

public class PlayerChase : MonoBehaviour
{
    [Header("�Q��")]
    public Transform Player;      // �v���C���[
    public Transform Ghost;       // �S�[�X�g

    [Header("�ړ��ݒ�")]
    public float moveSpeed = 3.0f;
    public float stopDistance = 0f;

    private bool isChasing = false;

    // -----------------------------------------
    // ��������y�ǉ��F�{��G�t�F�N�g�z
    [Header("�{��G�t�F�N�g")]
    [Tooltip("�ǐՒ��ɕ\������G�t�F�N�g��Prefab�iParticleSystem���j")]
    [SerializeField] private GameObject angryEffectPrefab;

    [Tooltip("�S�[�X�g�̃��[�J�����W�ł̃I�t�Z�b�g�i����ɏo���������� y ���グ��j")]
    [SerializeField] private Vector3 effectLocalOffset = new Vector3(0f, 1.5f, 0f);

    [Tooltip("�Ǐ]���@�Ftrue=�e�q�t���i�y�����y�j/ false=Update�ňʒu�����i�w���ʂ�j")]
    [SerializeField] private bool parentEffectToGhost = false;

    [Tooltip("�ǐՊJ�n�`�I���̂킸���Ȏc�����o���������̐������ԁi�b�j�B0�Ȃ瑦����")]
    [SerializeField] private float effectGraceSecondsOnStop = 0f;

    private Transform angryEffectInstance;   // ���������G�t�F�N�g��Transform
    private float effectStopTimer = 0f;
    // �����܂Ły�ǉ��z
    // -----------------------------------------

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isChasing = true;
            SpawnAngryEffect();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isChasing = false;
            BeginStopAngryEffect();
        }
    }

    private void Update()
    {
        // �Ǐ]�ړ�
        if (isChasing && Player != null && Ghost != null)
        {
            Vector3 toPlayer = Player.position - Ghost.position;
            toPlayer.y = 0f; // �����̂�

            float dist = toPlayer.magnitude;
            if (dist > stopDistance)
            {
                if (toPlayer != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(toPlayer);
                    Ghost.rotation = Quaternion.Slerp(Ghost.rotation, targetRot, 0.1f);
                }
                Ghost.position += Ghost.forward * moveSpeed * Time.deltaTime;
            }
        }

        // -----------------------------------------
        // ��������y�ǉ��F�{��G�t�F�N�g�̒Ǐ]����~�����z
        if (angryEffectInstance)
        {
            if (!parentEffectToGhost && Ghost)
            {
                // �e�q�ɂ��Ă��Ȃ��ꍇ�A���t���[���ʒu�ƌ����𓯊�
                angryEffectInstance.position = Ghost.TransformPoint(effectLocalOffset);
                angryEffectInstance.rotation = Ghost.rotation;
            }

            if (!isChasing)
            {
                if (effectGraceSecondsOnStop > 0f)
                {
                    effectStopTimer -= Time.deltaTime;
                    if (effectStopTimer <= 0f)
                        DestroyAngryEffect();
                }
                else
                {
                    DestroyAngryEffect();
                }
            }
        }
        // �����܂Ły�ǉ��z
        // -----------------------------------------
    }

    // -----------------------------------------
    // ��������y�ǉ��F�{��G�t�F�N�g����/�j���z
    private void SpawnAngryEffect()
    {
        if (!angryEffectPrefab || !Ghost) return;

        if (!angryEffectInstance)
        {
            // ����
            GameObject go = Instantiate(angryEffectPrefab);
            angryEffectInstance = go.transform;

            if (parentEffectToGhost)
            {
                // �e�q�t���F���R�ɒǏ]�i�p�t�H�[�}���X���j
                angryEffectInstance.SetParent(Ghost, worldPositionStays: false);
                angryEffectInstance.localPosition = effectLocalOffset;
                angryEffectInstance.localRotation = Quaternion.identity;
            }
            else
            {
                // ��e�q�FUpdate�ňʒu�����i�w��ʂ�j
                angryEffectInstance.position = Ghost.TransformPoint(effectLocalOffset);
                angryEffectInstance.rotation = Ghost.rotation;
            }

            // ����ParticleSystem�Ȃ珉����
            var ps = angryEffectInstance.GetComponent<ParticleSystem>();
            if (ps) ps.Play();
        }
        else
        {
            // ���ɂ���ꍇ�͍Đ������Z�b�g
            var ps = angryEffectInstance.GetComponent<ParticleSystem>();
            if (ps) { ps.Clear(); ps.Play(); }
        }

        effectStopTimer = effectGraceSecondsOnStop;
    }

    private void BeginStopAngryEffect()
    {
        // �c�����Ԃ��ݒ肳��Ă���� Update �Ń^�C�}�[�����炷
        if (angryEffectInstance)
        {
            effectStopTimer = effectGraceSecondsOnStop;
            if (effectGraceSecondsOnStop <= 0f) DestroyAngryEffect();
        }
    }

    private void DestroyAngryEffect()
    {
        if (angryEffectInstance)
        {
            Destroy(angryEffectInstance.gameObject);
            angryEffectInstance = null;
        }
    }
    // �����܂Ły�ǉ��z
    // -----------------------------------------
}
