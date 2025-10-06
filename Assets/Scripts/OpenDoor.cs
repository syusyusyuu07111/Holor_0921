using UnityEngine;

public class OpenDoor : MonoBehaviour
{
    [System.Serializable]
    public class DoorLeaf
    {
        [Header("Pivot")]
        public Transform pivot;

        [Header("Rotate (+Δ)")]
        [Tooltip("今の回転から各軸でどれだけ回すか（例: Y=90で横開き）")]
        public Vector3 openDeltaEuler = new Vector3(0f, 90f, 0f);
        [Tooltip("1 = 指定Δそのまま、-1 = 指定Δを反転（左右の開き方向反転）")]
        public int direction = 1;

        [HideInInspector] public Quaternion closedLocalRot; // 基準
        [HideInInspector] public Quaternion openLocalRot;   // 基準 × Δ
    }

    [Header("Refs")]
    [SerializeField] Transform player;

    [Header("Door Leaves (max 2)")]
    [Tooltip("両開きにしたい場合はサイズを2にして各ピボットを割り当て")]
    [SerializeField] DoorLeaf[] leaves = new DoorLeaf[1];

    [Header("Trigger")]
    [SerializeField] float openDistance = 1.5f;

    [Header("Speed")]
    [SerializeField] float rotateSpeedDegPerSec = 180f;

    [Header("Options")]
    [Tooltip("プレイヤーが“ドアの表側”にいる必要があるか（表側判定は0番リーフのforward基準）")]
    [SerializeField] bool requireFacingSide = false;
    [Tooltip("表側判定のしきい値（leaf[0].pivot.forward と プレイヤー方向の内積）。0で前方半球。")]
    [SerializeField, Range(-1f, 1f)] float facingDotThreshold = 0f;

    [Tooltip("自動で閉じるか（範囲外・裏側に回った等で）")]
    [SerializeField] bool autoClose = true;

    [Tooltip("施錠中は開かない")]
    [SerializeField] bool isLocked = false;

    [Tooltip("起動時の姿勢を“閉”として採用する（= 現在の回転を参照）")]
    [SerializeField] bool captureClosedOnStart = true;

    // 入力（新Input Systemの自動生成クラスを想定）
    InputSystem_Actions input;
    bool isOpen;

    void Awake()
    {
        input = new InputSystem_Actions();
    }

    void OnEnable()
    {
        input.Player.Enable();
    }

    void Start()
    {
        // ピボット未指定のリーフがある場合は、自身を代入（安全策）
        if (leaves != null)
        {
            for (int i = 0; i < leaves.Length; i++)
            {
                if (leaves[i] == null) continue;
                if (!leaves[i].pivot) leaves[i].pivot = transform;
            }
        }

        CaptureClosedFromCurrentIfNeeded();
        RebuildOpenRotations();
    }

    void Update()
    {
        bool shouldOpen = CanOpen();

        if (shouldOpen)
        {
            isOpen = true;
        }
        else if (autoClose && ShouldAutoClose())
        {
            isOpen = false;
        }

        // 目標回転へ各リーフを回す
        float step = rotateSpeedDegPerSec * Time.deltaTime;
        for (int i = 0; i < leaves.Length; i++)
        {
            var leaf = leaves[i];
            if (leaf == null || leaf.pivot == null) continue;

            Quaternion target = isOpen ? leaf.openLocalRot : leaf.closedLocalRot;
            leaf.pivot.localRotation = Quaternion.RotateTowards(leaf.pivot.localRotation, target, step);
        }
    }

    //================== 開閉条件 ==================//
    bool CanOpen()
    {
        if (isLocked) return false;
        if (!player || leaves == null || leaves.Length == 0) return false;

        // 1) 距離：最も近いピボットとの距離で判定
        if (NearestDistanceToAnyLeaf() >= openDistance) return false;

        // 2) 入力：今フレーム押されたらOK
        if (!input.Player.DoorOpen.WasPressedThisFrame()) return false;

        // 3) 表側必須なら0番リーフ基準でチェック
        if (requireFacingSide && !IsPlayerOnFacingSide()) return false;

        return true;
    }

    bool ShouldAutoClose()
    {
        if (!player || leaves == null || leaves.Length == 0) return false;

        // 距離外なら閉じる
        if (NearestDistanceToAnyLeaf() >= openDistance) return true;

        // 表側必須なら、裏側に回ったら閉じる（0番リーフ基準）
        if (requireFacingSide && !IsPlayerOnFacingSide()) return true;

        // 施錠されたら閉じる
        if (isLocked) return true;

        return false;
    }

    //================== ユーティリティ ==================//
    float NearestDistanceToAnyLeaf()
    {
        float minDist = float.PositiveInfinity;
        for (int i = 0; i < leaves.Length; i++)
        {
            var leaf = leaves[i];
            if (leaf == null || leaf.pivot == null) continue;
            float d = Vector3.Distance(player.position, leaf.pivot.position);
            if (d < minDist) minDist = d;
        }
        return minDist;
    }

    bool IsPlayerOnFacingSide()
    {
        var leaf0 = (leaves.Length > 0) ? leaves[0] : null;
        if (leaf0 == null || leaf0.pivot == null) return true; // 判断不能なら許容

        Vector3 toPlayer = (player.position - leaf0.pivot.position).normalized;
        float dot = Vector3.Dot(leaf0.pivot.forward, toPlayer);
        return dot >= facingDotThreshold;
    }

    void CaptureClosedFromCurrentIfNeeded()
    {
        if (leaves == null) return;

        for (int i = 0; i < leaves.Length; i++)
        {
            var leaf = leaves[i];
            if (leaf == null || leaf.pivot == null) continue;

            // 起動時の姿勢を「閉」として採用
            if (captureClosedOnStart)
            {
                leaf.closedLocalRot = leaf.pivot.localRotation;
            }
            else
            {
                // 必要なら別の方法で「閉」を決める余地
                leaf.closedLocalRot = leaf.pivot.localRotation;
            }
        }
    }

    void RebuildOpenRotations()
    {
        if (leaves == null) return;

        for (int i = 0; i < leaves.Length; i++)
        {
            var leaf = leaves[i];
            if (leaf == null || leaf.pivot == null) continue;

            // 「基準（閉）」×「Δ回転（方向±）」で開き姿勢を合成
            var delta = Quaternion.Euler(leaf.openDeltaEuler * Mathf.Sign(leaf.direction));
            leaf.openLocalRot = leaf.closedLocalRot * delta;
        }
    }

    // エディタから呼べるように残しておく（0〜全リーフ同時に閉基準を再キャプチャ）
    public void SetClosedFromCurrent()
    {
        if (leaves == null) return;

        for (int i = 0; i < leaves.Length; i++)
        {
            var leaf = leaves[i];
            if (leaf == null || leaf.pivot == null) continue;
            leaf.closedLocalRot = leaf.pivot.localRotation;
        }
        RebuildOpenRotations();
    }
}
