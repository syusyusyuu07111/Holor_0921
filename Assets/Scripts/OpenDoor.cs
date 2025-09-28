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
    [Tooltip("プレイヤーが“ドアの表側”にいる必要があるか")]
    [SerializeField] bool requireFacingSide = false;
    [Tooltip("表側判定のしきい値（doorPivot.forward と プレイヤー方向の内積）。0で前方半球。")]
    [SerializeField, Range(-1f, 1f)] float facingDotThreshold = 0f;

    [Tooltip("自動で閉じるか（範囲外・裏側に回った等で）")]
    [SerializeField] bool autoClose = true;

    [Tooltip("開くのに操作入力が必要か（trueならEキーなどでトグル的に開く）")]
    [SerializeField] bool requireInteractInput = true;
    [SerializeField] KeyCode interactKey = KeyCode.E;

    [Tooltip("施錠中は開かない")]
    [SerializeField] bool isLocked = false;

    [Tooltip("起動時の姿勢を“閉”として採用する（= 現在の回転を参照）")]
    [SerializeField] bool captureClosedOnStart = true;

    Quaternion closedLocalRot;  // 現在の回転（基準）
    Quaternion openLocalRot;    // 基準 × Δ回転
    bool isOpen;

    InputSystem_Actions input;

    void Start()
    {
        if (!doorPivot) doorPivot = transform;

        // 基準姿勢（閉）
        closedLocalRot = doorPivot.localRotation;
        if (!captureClosedOnStart)
        {
            // 仕様が変わる余地があるならここで別の“閉”姿勢を与える
            closedLocalRot = doorPivot.localRotation;
        }

        RebuildOpenRotation();
    }
    private void Awake()
    {
        input = new InputSystem_Actions();
    }
    private void OnEnable()
    {
        input.Player.Enable();
    }

    void Update()
    {
        if (!player || !doorPivot) return;

        // 「開けてよい」かの判定
        bool shouldOpen = CanOpen();

        if (shouldOpen)
        {
            isOpen = true;
        }
        else if (autoClose && ShouldAutoClose())
        {
            isOpen = false;
        }
        // 開いているなら「開き姿勢」を、閉じているなら「閉じ姿勢」を目標にする
        Quaternion target;
        if (isOpen)
        {
            target = openLocalRot;    // 基準 × Δ（開き）
        }
        else
        {
            target = closedLocalRot;  // 基準（閉）
        }


        // 今の回転から目標まで一定角速度で回す
        float step = rotateSpeedDegPerSec * Time.deltaTime;
        doorPivot.localRotation = Quaternion.RotateTowards(doorPivot.localRotation, target, step);
    }

        //ドアをopenにする条件=================================================================================
    bool CanOpen()
    {
        if (isLocked)
        {
            return false;
        }

        // 1) 距離判定
        float dist = Vector3.Distance(player.position, doorPivot.position);
        if (dist >= openDistance)
        {
            return false;
        }
        // 2) 入力
            // 「今フレームで押されたら開けてよい」
            if (!input.Player.DoorOpen.WasPressedThisFrame())
            {
                    return false;
            }
        // ここまで通れば開けてよい
        return true;
    }
    /// 自動で閉じてよいか。基本は「開け条件を満たしていない」時に閉じる。
    bool ShouldAutoClose()
    {
        if (!player || !doorPivot) return false;

        // 距離外なら閉じる
        float dist = Vector3.Distance(player.position, doorPivot.position);
        if (dist >= openDistance) return true;

        // 表側必須なら、裏側に回ったら閉じる
        if (requireFacingSide)
        {
            Vector3 toPlayer = (player.position - doorPivot.position).normalized;
            float dot = Vector3.Dot(doorPivot.forward, toPlayer);
            if (dot < facingDotThreshold) return true;
        }

        // 施錠されたら閉じる
        if (isLocked) return true;

        return false;
    }

    public void SetClosedFromCurrent()
    {
        // 今の基準(=閉)を、実際に回している対象の現在回転で記録
        if (doorPivot != null)
        {
            closedLocalRot = doorPivot.localRotation;
        }
        RebuildOpenRotation(); // 基準を変えたので開き姿勢も作り直す
    }
    void RebuildOpenRotation()
    {
        var delta = Quaternion.Euler(openDeltaEuler * Mathf.Sign(direction));
        openLocalRot = closedLocalRot * delta;  // 「基準（現在）＋Δ」をクォータニオンで合成
    }
}
