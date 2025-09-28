using UnityEngine;

public class OpenDoor : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] Transform player;
    [SerializeField] Transform doorPivot;

    [Header("Trigger")]
    [SerializeField] float openDistance = 1.5f;

    [Header("Rotate (+��)")]
    // ���̉�]����u+�ǂ̂��炢�񂷂��v���e�����ƂɎw��
    [SerializeField] Vector3 openDeltaEuler = new Vector3(0f, 90f, 0f);
    [Tooltip("1 = �w�胢���̂܂܁A-1 = �w�胢�𔽓]�i���J��/�E�J���̐ؑւȂǁj")]
    [SerializeField] int direction = 1;

    [Header("Speed")]
    [SerializeField] float rotateSpeedDegPerSec = 180f;

    [Header("Options")]
    [Tooltip("�v���C���[���g�h�A�̕\���h�ɂ���K�v�����邩")]
    [SerializeField] bool requireFacingSide = false;
    [Tooltip("�\������̂������l�idoorPivot.forward �� �v���C���[�����̓��ρj�B0�őO�������B")]
    [SerializeField, Range(-1f, 1f)] float facingDotThreshold = 0f;

    [Tooltip("�����ŕ��邩�i�͈͊O�E�����ɉ�������Łj")]
    [SerializeField] bool autoClose = true;

    [Tooltip("�J���̂ɑ�����͂��K�v���itrue�Ȃ�E�L�[�ȂǂŃg�O���I�ɊJ���j")]
    [SerializeField] bool requireInteractInput = false;
    [SerializeField] KeyCode interactKey = KeyCode.E;

    [Tooltip("�{�����͊J���Ȃ�")]
    [SerializeField] bool isLocked = false;

    [Tooltip("�N�����̎p�����g�h�Ƃ��č̗p����i= ���݂̉�]���Q�Ɓj")]
    [SerializeField] bool captureClosedOnStart = true;

    Quaternion closedLocalRot;  // ���݂̉�]�i��j
    Quaternion openLocalRot;    // � �~ ����]
    bool isOpen;

    void Reset()
    {
        if (!doorPivot) doorPivot = transform;
        if (!player && Camera.main) player = Camera.main.transform;
    }

    void Start()
    {
        if (!doorPivot) doorPivot = transform;

        // ��p���i�j
        closedLocalRot = doorPivot.localRotation;
        if (!captureClosedOnStart)
        {
            // �d�l���ς��]�n������Ȃ炱���ŕʂ́g�h�p����^����
            closedLocalRot = doorPivot.localRotation;
        }

        RebuildOpenRotation();
    }

    void Update()
    {
        if (!player || !doorPivot) return;

        // �u�J���Ă悢�v���̔�����֐��ɏW��
        bool shouldOpen = CanOpen();

        if (shouldOpen) isOpen = true;
        else if (autoClose && ShouldAutoClose()) isOpen = false;

        // �ڕW�́u��v���u��~���v
        Quaternion target = isOpen ? openLocalRot : closedLocalRot;

        // ���̉�]����ڕW�܂ň��p���x�ŉ�
        float step = rotateSpeedDegPerSec * Time.deltaTime;
        doorPivot.localRotation = Quaternion.RotateTowards(doorPivot.localRotation, target, step);
    }

        //�h�A��open�ɂ������=================================================================================
    bool CanOpen()
    {
        if (isLocked) return false;

        // 1) ��������
        float dist = Vector3.Distance(player.position, doorPivot.position);
        if (dist >= openDistance)
        {
            return false;
        }

        // 2) �\���`�F�b�N�i�K�v�ȏꍇ�̂݁j
        //if (requireFacingSide)
        //{
        //    Vector3 toPlayer = (player.position - doorPivot.position).normalized;
        //    float dot = Vector3.Dot(doorPivot.forward, toPlayer); // forward�
        //    if (dot < facingDotThreshold) return false;
        //}

        // 3) ���͗v���i�K�v�ȏꍇ�̂݁j
        if (requireInteractInput)
        {
            // �u���t���[���ŉ����ꂽ��J���Ă悢�v
            if (!Input.GetKeyDown(interactKey)) return false;
        }

        // �����܂Œʂ�ΊJ���Ă悢
        return true;
    }

    /// <summary>
    /// �����ŕ��Ă悢���B��{�́u�J�������𖞂����Ă��Ȃ��v���ɕ���B
    /// _���͕K�{���[�h_ �ł��A�v���C���[���͈͊O/�����ֈړ�������߂�B
    /// </summary>
    bool ShouldAutoClose()
    {
        if (!player || !doorPivot) return false;

        // �����O�Ȃ����
        float dist = Vector3.Distance(player.position, doorPivot.position);
        if (dist >= openDistance) return true;

        // �\���K�{�Ȃ�A�����ɉ���������
        if (requireFacingSide)
        {
            Vector3 toPlayer = (player.position - doorPivot.position).normalized;
            float dot = Vector3.Dot(doorPivot.forward, toPlayer);
            if (dot < facingDotThreshold) return true;
        }

        // �{�����ꂽ�����
        if (isLocked) return true;

        return false;
    }

    [ContextMenu("Set Closed From Current")]
    public void SetClosedFromCurrent()
    {
        closedLocalRot = doorPivot ? doorPivot.localRotation : transform.localRotation;
        RebuildOpenRotation();
    }

    void RebuildOpenRotation()
    {
        var delta = Quaternion.Euler(openDeltaEuler * Mathf.Sign(direction));
        openLocalRot = closedLocalRot * delta;  // �u��i���݁j�{���v���N�H�[�^�j�I���ō���
    }

    void OnValidate()
    {
        if (doorPivot)
        {
            if (closedLocalRot == default) closedLocalRot = doorPivot.localRotation;
            RebuildOpenRotation();
        }
    }

    // --- �ǉ��Ŏg������JAPI�� ---
    public void Lock(bool locked)
    {
        isLocked = locked;
        if (locked) isOpen = false;
    }

    public void ForceOpen(bool open)
    {
        isOpen = open;
    }
}
