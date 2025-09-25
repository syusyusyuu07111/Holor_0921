using UnityEngine;

public class OpenDoor : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] Transform player;
    [SerializeField] Transform doorPivot;

    [Header("Trigger")]
    [SerializeField] float openDistance = 1.5f;

    [Header("Rotate (+Δ)")]
    // 今の回転から「+どのくらい回すか」を各軸ごとに指定
    [SerializeField] Vector3 openDeltaEuler = new Vector3(0f, 90f, 0f);
    [Tooltip("1 = 指定Δそのまま、-1 = 指定Δを反転（左開き/右開きの切替など）")]
    [SerializeField] int direction = 1;

    [Header("Speed")]
    [SerializeField] float rotateSpeedDegPerSec = 180f;

    [Header("Options")]
    [SerializeField] bool autoClose = true;
    [Tooltip("起動時の姿勢を“閉”として採用する（= 現在の回転を参照）")]
    [SerializeField] bool captureClosedOnStart = true;

    Quaternion closedLocalRot;  // 現在の回転（基準）
    Quaternion openLocalRot;    // 基準 × Δ回転
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
            closedLocalRot = doorPivot.localRotation;  // 「現在のrotateを参照」
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

        // 目標は「基準」か「基準×Δ」
        Quaternion target = isOpen ? openLocalRot : closedLocalRot;

        // 今の回転から目標まで一定角速度で回す
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
        openLocalRot = closedLocalRot * delta;  // ←「基準（現在）＋Δ」をクォータニオンで合成
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
