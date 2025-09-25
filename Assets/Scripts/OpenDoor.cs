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
    [SerializeField] bool autoClose = true;
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

        if (captureClosedOnStart)
            closedLocalRot = doorPivot.localRotation;  // �u���݂�rotate���Q�Ɓv
        else
            closedLocalRot = doorPivot.localRotation;

        RebuildOpenRotation();
    }

    void Update()
    {
        if (!player || !doorPivot) return;

        float dist = Vector3.Distance(player.position, doorPivot.position);
        bool shouldOpen = dist < openDistance;

        if (shouldOpen) isOpen = true;
        else if (autoClose) isOpen = false;

        // �ڕW�́u��v���u��~���v
        Quaternion target = isOpen ? openLocalRot : closedLocalRot;

        // ���̉�]����ڕW�܂ň��p���x�ŉ�
        float step = rotateSpeedDegPerSec * Time.deltaTime;
        doorPivot.localRotation = Quaternion.RotateTowards(doorPivot.localRotation, target, step);
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
        openLocalRot = closedLocalRot * delta;  // ���u��i���݁j�{���v���N�H�[�^�j�I���ō���
    }

    void OnValidate()
    {
        if (doorPivot)
        {
            if (closedLocalRot == default) closedLocalRot = doorPivot.localRotation;
            RebuildOpenRotation();
        }
    }
}
