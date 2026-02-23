using UnityEngine;

/*
     役割：
     プレイヤーが近づいてボタンを押すとドアを開閉するスクリプト

     できること：
     ・片開き / 両開き（DoorLeafを最大2枚想定）
     ・距離条件で開く
     ・入力（DoorOpen or Interact）で開く
     ・表側にいる時だけ開く（requireFacingSide）
     ・範囲外になったら自動で閉じる（autoClose）
     ・ロック中は開かない（isLocked）
     ・開閉の瞬間にSEを鳴らす（AudioManager経由）

     使い方（Inspector）：
     ・player：基本は未設定でOK（タグ"Player"を自動で拾う）
     ・audioManager：ドアSEを鳴らしたい場合に割り当て
     ・leaves[0].pivot：ドアの回転軸（ドアの蝶番位置）
       両開きなら leaves を 2 にして pivot を2枚分入れる
*/

public class OpenDoor : MonoBehaviour
{
    //================
    // DoorLeaf（ドア1枚ぶんの設定）
    //================

    /*
         DoorLeaf = ドアの「片側」1枚の情報

         pivot：
           実際に回転させるTransform（蝶番の位置）

         openDeltaEuler：
           「閉じてる回転」から、どれだけ回したら「開いた回転」になるか
           例）Y=90 なら横に90度開く

         direction：
           1 ならそのまま開く
           -1 なら逆方向に開く（左右反転用）

         closedLocalRot / openLocalRot：
           実行中に使う回転ターゲット
           ・Startで閉じ姿勢を記録し、そこから開き姿勢を計算する
    */
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

        [HideInInspector] public Quaternion closedLocalRot; // 「閉」ターゲット
        [HideInInspector] public Quaternion openLocalRot;   // 「開」ターゲット
    }

    //================
    // Inspector参照
    //================

    [Header("Refs")]
    [SerializeField] Transform player;                        // 操作主（未設定ならタグ"Player"を拾う）

    [Header("Audio")]
    [SerializeField] AudioManager audioManager;               // 開閉SEを鳴らす（未設定なら無音）

    [Header("Door Leaves (max 2)")]
    [Tooltip("両開きにしたい場合はサイズを2にして各ピボットを割り当て")]
    [SerializeField] DoorLeaf[] leaves = new DoorLeaf[1];     // ドアの枚数（片開き=1、両開き=2）

    //================
    // 開閉条件
    //================

    [Header("Trigger")]
    [SerializeField] float openDistance = 1.5f;               // 開閉できる距離（この距離以内）

    [Header("Speed")]
    [SerializeField] float rotateSpeedDegPerSec = 180f;       // 回転速度（度/秒）

    //================
    // オプション
    //================

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

    //================
    // 内部（入力 / 状態）
    //================

    // 入力（新Input Systemの自動生成クラスを想定）
    InputSystem_Actions input;

    // 現在のターゲット状態（開くべきか閉じるべきか）
    bool isOpen;

    // 前フレームの状態（開閉の瞬間だけSEを鳴らすために使う）
    bool prevIsOpen;

    //================
    // 参照の自己修復
    //================

    /*
         player が Inspector 未設定の場合に、
         タグ"Player"を探して自動で拾う保険
    */
    private void TryAssignPlayer()
    {
        if (player) return;
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go) player = go.transform;
    }

    //================
    // Unity Lifecycle
    //================

    void Awake()
    {
        // 入力クラスを生成
        input = new InputSystem_Actions();

        // player参照を確保（未設定ならタグ検索）
        TryAssignPlayer();
    }

    void OnEnable()
    {
        // Playerアクションだけ有効化（ドア操作に必要）
        input.Player.Enable();
    }

    void OnDisable()
    {
        input.Player.Disable();
    }

    void Start()
    {
        //================
        // pivot未設定の安全策
        //================

        /*
             leaves[i].pivot が未設定なら、
             とりあえず自分自身をpivotにする（落ちないための保険）
        */
        if (leaves != null)
        {
            for (int i = 0; i < leaves.Length; i++)
            {
                if (leaves[i] == null) continue;
                if (!leaves[i].pivot) leaves[i].pivot = transform;
            }
        }

        //================
        // 閉回転の記録 ＆ 開回転の計算
        //================

        CaptureClosedFromCurrentIfNeeded();                   // 現在の姿勢を「閉」として記録
        RebuildOpenRotations();                               // 「閉」×Δで「開」を作る

        //================
        // 初期状態の同期
        //================

        prevIsOpen = isOpen;
    }

    void Update()
    {
        //================
        // player参照の保険
        //================

        // player参照が外れている/未設定なら毎フレーム拾い直す
        TryAssignPlayer();

        //================
        // 今フレームの「開くべきか」を決める
        //================

        bool shouldOpen = CanOpen();

        // 入力が成立していれば開く
        if (shouldOpen)
        {
            isOpen = true;
        }
        // 入力が無くても、自動で閉める条件なら閉める
        else if (autoClose && ShouldAutoClose())
        {
            isOpen = false;
        }

        //================
        // 開閉の瞬間だけSEを鳴らす
        //================

        /*
             isOpen は「目標状態」なので、
             前フレームから変化した瞬間だけ音を鳴らす
        */
        if (isOpen != prevIsOpen)
        {
            // 開いた瞬間
            if (isOpen)
            {
                if (audioManager != null)
                    audioManager.PlayDoorOpen();
            }
            // 閉じた瞬間
            else
            {
                if (audioManager != null)
                    audioManager.PlayDoorClose();
            }

            // 今の状態を保存して、次フレームの比較に使う
            prevIsOpen = isOpen;
        }

        //================
        // ドアをターゲット回転へ寄せる（見た目の回転）
        //================

        /*
             isOpen は「開く/閉じるの目標」だけ決める
             実際の回転は RotateTowards で毎フレーム少しずつ近づける
        */
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

    //================
    // 開く条件（入力で開く）
    //================

    /*
         CanOpen が true になる条件（全部満たす必要がある）

         1) ロックされていない
         2) player と pivot が存在する
         3) プレイヤーが一定距離以内にいる
         4) 今フレーム入力が押された（DoorOpen か Interact）
         5) requireFacingSide=true の場合は「表側」にいる
    */
    bool CanOpen()
    {
        if (isLocked) return false;
        if (!player || leaves == null || leaves.Length == 0) return false;

        // 距離チェック
        if (NearestDistanceToAnyLeaf() >= openDistance) return false;

        // 入力チェック（今フレーム押された？）
        if (!(input.Player.DoorOpen.WasPressedThisFrame() ||
              input.Player.Interact.WasPressedThisFrame()))
            return false;

        // 表側チェック（必要な場合のみ）
        if (requireFacingSide && !IsPlayerOnFacingSide()) return false;

        return true;
    }

    //================
    // 自動で閉じる条件
    //================

    /*
         autoClose=true の場合に、閉めるべきか判定する

         ・距離外なら閉める
         ・表側限定で、裏側に回ったら閉める
         ・ロックされたら閉める
    */
    bool ShouldAutoClose()
    {
        if (!player || leaves == null || leaves.Length == 0) return false;

        if (NearestDistanceToAnyLeaf() >= openDistance) return true;
        if (requireFacingSide && !IsPlayerOnFacingSide()) return true;
        if (isLocked) return true;

        return false;
    }

    //================
    // ユーティリティ
    //================

    /*
         両開きの場合も含めて、最も近いpivotまでの距離を返す
         ・openDistance 判定で使う
    */
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

    /*
         プレイヤーが「表側」にいるかを判定する

         ・基準は leaves[0].pivot.forward
         ・pivot → player の方向ベクトルとの内積(dot)で判定
         ・dot が threshold 以上なら表側扱い
         ・判定できない場合は true（安全に開ける）
    */
    bool IsPlayerOnFacingSide()
    {
        var leaf0 = (leaves != null && leaves.Length > 0) ? leaves[0] : null;
        if (leaf0 == null || leaf0.pivot == null) return true;

        Vector3 toPlayer = (player.position - leaf0.pivot.position).normalized;
        float dot = Vector3.Dot(leaf0.pivot.forward, toPlayer);
        return dot >= facingDotThreshold;
    }

    //================
    // 回転ターゲットの準備
    //================

    /*
         起動時点の pivot.localRotation を「閉」回転として記録する
         ※現状の実装は captureClosedOnStart の分岐が同じ処理になっている
           （将来的に仕様が分かれた時に備えた形のまま）
    */
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

    /*
         「開」回転を計算する
         openLocalRot = closedLocalRot × Δ
         Δは openDeltaEuler と direction（反転）から作る
    */
    void RebuildOpenRotations()
    {
        if (leaves == null) return;

        for (int i = 0; i < leaves.Length; i++)
        {
            var leaf = leaves[i];
            if (leaf == null || leaf.pivot == null) continue;

            var delta = Quaternion.Euler(leaf.openDeltaEuler * Mathf.Sign(leaf.direction));
            leaf.openLocalRot = leaf.closedLocalRot * delta;
        }
    }

    //================
    // エディタ用：今の姿勢を「閉」にする
    //================

    /*
         エディタから呼ぶ用
         ・現在の姿勢を「閉」として再登録する
         ・その後、開回転を再計算する
    */
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

    //================
    // ロック制御
    //================

    // 外部から「今ロック中？」を読むため
    public bool IsLocked => isLocked;

    /*
         外部から施錠/解錠する
         ・locked=true の瞬間は閉じ方向にする（isOpen=false）
    */
    public void SetLocked(bool locked)
    {
        isLocked = locked;
        if (locked) isOpen = false;
    }
}