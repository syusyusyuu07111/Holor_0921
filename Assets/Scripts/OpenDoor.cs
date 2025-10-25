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

    // ★ここ追加：AudioManager への参照
    [Header("Audio")]
    [SerializeField] AudioManager audioManager;

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

    // 今ドアが開いているかどうか
    bool isOpen;

    // ★追加：直前フレームの状態を覚えておく
    bool prevIsOpen;

    //================== 参照の自己修復 ==================//
    private void TryAssignPlayer()
    {
        if (player) return;
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go) player = go.transform;
    }

    void Awake()
    {
        input = new InputSystem_Actions();
        TryAssignPlayer();
    }

    void OnEnable()
    {
        input.Player.Enable();
    }

    void OnDisable()
    {
        input.Player.Disable();
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

        // 初期状態をそろえる
        prevIsOpen = isOpen;
    }

    void Update()
    {
        // プレイヤー参照なかったら毎フレーム探す保険
        TryAssignPlayer();

        // --- ここで状態を決める ---
        bool shouldOpen = CanOpen();

        if (shouldOpen)
        {
            isOpen = true;
        }
        else if (autoClose && ShouldAutoClose())
        {
            isOpen = false;
        }

        // ★状態が変わった瞬間を検出
        if (isOpen != prevIsOpen)
        {
            // 開いた瞬間
            if (isOpen)
            {
                if (audioManager != null)
                {
                    audioManager.PlayDoorOpen();  // ← ここで開く音を鳴らす
                }
            }
            // 閉じた瞬間
            else
            {
                if (audioManager != null)
                {
                    audioManager.PlayDoorClose();  // ← ここで閉じる音を鳴らす
                }
            }

            // 今の状態を「前の状態」として記録
            prevIsOpen = isOpen;
        }

        // --- ドアの回転をターゲットに寄せる ---
        float step = rotateSpeedDegPerSec * Time.deltaTime;
        for (int i = 0; i < leaves.Length; i++)
        {
            var leaf = leaves[i];
            if (leaf == null || leaf.pivot == null) continue;

            Quaternion target = isOpen ? leaf.openLocalRot : leaf.closedLocalRot;
            leaf.pivot.localRotation = Quaternion.RotateTowards(
                leaf.pivot.localRotation,
                target,
                step
            );
        }
    }

    //================== 開閉条件 ==================//
    bool CanOpen()
    {
        if (isLocked) return false;
        if (!player || leaves == null || leaves.Length == 0) return false;

        // 1) 距離チェック
        if (NearestDistanceToAnyLeaf() >= openDistance) return false;

        // 2) 入力チェック（今フレーム押された？）
        if (!(input.Player.DoorOpen.WasPressedThisFrame() ||
              input.Player.Interact.WasPressedThisFrame()))
            return false;

        // 3) 向きのチェック
        if (requireFacingSide && !IsPlayerOnFacingSide()) return false;

        return true;
    }

    bool ShouldAutoClose()
    {
        if (!player || leaves == null || leaves.Length == 0) return false;

        // 距離外なら閉じる
        if (NearestDistanceToAnyLeaf() >= openDistance) return true;

        // 裏側に回ったら閉じる（オプション）
        if (requireFacingSide && !IsPlayerOnFacingSide()) return true;

        // ロックされたら閉じる
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
        var leaf0 = (leaves != null && leaves.Length > 0) ? leaves[0] : null;
        if (leaf0 == null || leaf0.pivot == null) return true; // 判定できないなら許可しちゃう

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

            // 起動時の姿勢を「閉じてる回転」として記録
            if (captureClosedOnStart)
            {
                leaf.closedLocalRot = leaf.pivot.localRotation;
            }
            else
            {
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

            // 「閉」×「開きたい差分角度」で開いた時の回転を作る
            var delta = Quaternion.Euler(leaf.openDeltaEuler * Mathf.Sign(leaf.direction));
            leaf.openLocalRot = leaf.closedLocalRot * delta;
        }
    }

    // エディタから呼べるように
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

    //=========== ドアのロック制御 ===========//
    public bool IsLocked => isLocked;

    public void SetLocked(bool locked)
    {
        isLocked = locked;

        // ロックされた瞬間は閉め方向に寄せる
        if (locked) isOpen = false;
    }
}
